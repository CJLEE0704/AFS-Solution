using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PipeBendingDashboard.Communication;
using PipeBendingDashboard.Database;

namespace PipeBendingDashboard
{
    public partial class MainWindow : Window
    {
        private readonly string _wwwRoot = Path.Combine(
            AppContext.BaseDirectory, "wwwroot");

        private CommunicationManager? _commMgr;

        // ── 창 닫힘 플래그 — 이벤트 콜백에서 webView 접근 차단 ──
        private volatile bool _isClosing = false;

        private WindowChromeMode _windowChromeMode = WindowChromeMode.Normal;

        private enum WindowChromeMode
        {
            Normal,
            Borderless,
            CloseLocked
        }

        [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
        [DllImport("user32.dll")] private static extern bool DrawMenuBar(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);
        private const uint SC_CLOSE = 0xF060;
        private const uint MF_BYCOMMAND = 0x00000000;
        private const uint MF_ENABLED = 0x00000000;
        private const uint MF_GRAYED = 0x00000001;

        private void ApplyWindowChromeMode(WindowChromeMode mode)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ApplyWindowChromeMode(mode));
                return;
            }

            _windowChromeMode = mode;

            try
            {
                if (mode == WindowChromeMode.Borderless)
                {
                    WindowStyle = WindowStyle.None;
                    ResizeMode = ResizeMode.NoResize;
                }
                else
                {
                    if (WindowStyle != WindowStyle.SingleBorderWindow)
                        WindowStyle = WindowStyle.SingleBorderWindow;
                    if (ResizeMode != ResizeMode.CanResize)
                        ResizeMode = ResizeMode.CanResize;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var hwnd = new WindowInteropHelper(this).Handle;
                        if (hwnd == IntPtr.Zero) return;
                        var menu = GetSystemMenu(hwnd, false);
                        if (menu == IntPtr.Zero) return;

                        var state = mode == WindowChromeMode.CloseLocked
                            ? (MF_BYCOMMAND | MF_GRAYED)
                            : (MF_BYCOMMAND | MF_ENABLED);

                        EnableMenuItem(menu, SC_CLOSE, state);
                        DrawMenuBar(hwnd);
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }


    // ── DB 서비스 ────────────────────────────────────────────
    private DbService? _db;
    // ※ 실제 환경에 맞게 수정: Server=IP주소;Port=3306;Database=pbd_production;User=root;Password=iaan;
    private const string DB_CONN = "Server=127.0.0.1;Port=3306;Database=pbd_production;User=root;Password=iaan;CharSet=utf8mb4;";

        public MainWindow()
        {
            InitializeComponent();
            ApplyWindowChromeMode(WindowChromeMode.Normal);
            InitializeWebView();

            // Closing 이벤트에서 먼저 플래그 설정 후 리소스 해제
            Closing += (s, e) =>
            {
                if (_windowChromeMode == WindowChromeMode.CloseLocked)
                {
                    e.Cancel = true;
                    return;
                }

                _isClosing = true;
                _commMgr?.StopPolling();
            };

            Closed += (s, e) =>
            {
                _commMgr?.Dispose();
                _commMgr = null;

                try { webView?.Dispose(); } catch { }
            };
        }

        // ── WebView2 초기화 ──────────────────────────────────────
        private async void InitializeWebView()
        {
            try
            {
                UpdateLoadingText("WebView2 엔진 초기화 중...");
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PipeBendingDashboard", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

                // 창이 닫히는 중이면 초기화 중단
                if (_isClosing) return;

                await webView.EnsureCoreWebView2Async(env);

                var settings = webView.CoreWebView2.Settings;
                settings.IsZoomControlEnabled             = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsStatusBarEnabled               = false;
                settings.AreDefaultContextMenusEnabled    = false;
                settings.AreDevToolsEnabled               = false;

                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                UpdateLoadingText("대시보드 로딩 중...");
                LoadDashboard();
            }
            catch (Exception ex) when (!_isClosing)
            {
                MessageBox.Show(
                    $"WebView2 초기화 실패:\n{ex.Message}\n\n" +
                    "Microsoft Edge WebView2 Runtime이 설치되어 있는지 확인하세요.\n" +
                    "https://developer.microsoft.com/ko-kr/microsoft-edge/webview2/",
                    "초기화 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
            catch { /* 창 닫히는 중 발생한 예외는 무시 */ }
        }

        // ── 대시보드 HTML 로드 ───────────────────────────────────
        private void LoadDashboard()
        {
            var htmlFile = Path.Combine(_wwwRoot, "index.html");
            if (!File.Exists(htmlFile))
            {
                MessageBox.Show($"index.html 파일을 찾을 수 없습니다.\n경로: {htmlFile}",
                    "파일 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "dashboard.local", _wwwRoot,
                CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.Navigate("https://dashboard.local/index.html");
        }

        // ── 페이지 로드 완료 → TCP 통신 시작 ─────────────────────
        private void OnNavigationCompleted(object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_isClosing) return;

            if (e.IsSuccess)
            {
                Dispatcher.Invoke(() =>
                {
                    splashGrid.Visibility = Visibility.Collapsed;
                    webView.Visibility    = Visibility.Visible;
                });
                _ = StartCommunicationAsync();
                _ = InitializeDbAsync();   // DB 연결 (WebView 준비 완료 후)
            }
            else
            {
                MessageBox.Show($"페이지 로드 실패 (오류 코드: {e.WebErrorStatus})",
                    "로드 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── TCP/IP 통신 시작 ─────────────────────────────────────
        private async Task StartCommunicationAsync()
        {
            try
            {
                _commMgr = new CommunicationManager(
                    loaderIp:  "192.168.1.10", loaderPort:  5000,
                    cuttingIp: "192.168.1.11", cuttingPort: 5000,
                    laserIp:   "192.168.1.12", laserPort:   5000,
                    robotIp:   "192.168.1.14", robotPort:   5000,
                    bendingIp: "192.168.1.13", bendingPort: 5000,
                    bending2Ip: "192.168.1.15", bending2Port: 5000,
                    pollIntervalMs: 100);

                // ① 머신 상태 수신 → HTML 전달
                _commMgr.StatusUpdated += json =>
                {
                    if (_isClosing) return;
                    Dispatcher.Invoke(() =>
                        SendToWebView($"{{\"type\":\"machineStatus\",\"data\":{json}}}"));
                };

                // ② 로그 수신 → HTML 통신 로그창 전달
                _commMgr.LogAdded += (machineId, message) =>
                {
                    if (_isClosing) return;
                    var logJson = JsonSerializer.Serialize(new { machineId, message });
                    Dispatcher.Invoke(() =>
                        SendToWebView($"{{\"type\":\"commLog\",\"data\":{logJson}}}"));
                    // DB 통신 로그 저장
                    var logType = message.Contains("→") ? "SEND"
                                : message.Contains("←") ? "RECV"
                                : message.Contains("연결 성공") ? "CONNECTED"
                                : message.Contains("연결 실패") || message.Contains("연결 끊김") ? "DISCONNECTED"
                                : message.Contains("Ready") ? "READY" : "INFO";
                    _ = _db?.LogCommAsync(machineId == "SYS" ? null : machineId, logType, null, message, null);
                };

                // ③ 명령 ACK 수신 → HTML에 결과 전달
                _commMgr.CommandAck += (machineId, cmdType, response) =>
                {
                    if (_isClosing) return;
                    var ackJson = JsonSerializer.Serialize(new { machineId, cmdType, response });
                    Dispatcher.Invoke(() =>
                        SendToWebView($"{{\"type\":\"commandAck\",\"data\":{ackJson}}}"));
                    // DB 명령 ACK 로그
                    bool isOk = response?.StartsWith("OK", StringComparison.OrdinalIgnoreCase) ?? false;
                    _ = _db?.LogCommAsync(machineId, "RECV", cmdType, response, isOk);
                };

                // ④ Ready 확인 결과 → HTML 전달 (TCP 연결 후 READY? 응답)
                _commMgr.ReadyChecked += (machineId, isReady, response) =>
                {
                    if (_isClosing) return;
                    var readyJson = JsonSerializer.Serialize(new { machineId, ready = isReady, response });
                    Dispatcher.Invoke(() =>
                        SendToWebView($"{{\"type\":\"readyAck\",\"data\":{readyJson}}}"));
                };

                await _commMgr.ConnectAllAsync();
                if (_isClosing) return;

                // ④ 연결 후 현재 IP/Port 설정을 HTML에 동기화
                var settingsJson = _commMgr.GetCurrentSettingsJson();
                Dispatcher.Invoke(() =>
                    SendToWebView($"{{\"type\":\"syncSettings\",\"data\":{settingsJson}}}"));

                _commMgr.StartPolling();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[통신 오류] {ex.Message}");
            }
        }

        // ── HTML → C# 메시지 수신 ────────────────────────────────
        private async void OnWebMessageReceived(object? sender,
            CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_isClosing) return;
            try
            {
                var message = e.TryGetWebMessageAsString();
                System.Diagnostics.Debug.WriteLine($"[WebMessage] {message}");
                if (_commMgr == null) return;

                var cmd = JsonSerializer.Deserialize<WebCommand>(message,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cmd == null) return;
                if (cmd.Type.ToUpper() == "WINDOW_CHROME" && !string.IsNullOrEmpty(cmd.Data))
                {
                    try
                    {
                        var d = JsonSerializer.Deserialize<JsonElement>(cmd.Data);
                        var mode = d.TryGetProperty("mode", out var mv) ? mv.GetString() : "normal";
                        ApplyWindowChromeMode(mode switch
                        {
                            "borderless" => WindowChromeMode.Borderless,
                            "close_locked" => WindowChromeMode.CloseLocked,
                            _ => WindowChromeMode.Normal
                        });
                    }
                    catch { ApplyWindowChromeMode(WindowChromeMode.Normal); }
                    return;
                }


                // ── 활성 머신 목록 수신 ──────────────────────────────
                if (cmd.Type.ToUpper() == "SET_ACTIVE_MACHINES" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSetActiveMachinesAsync(cmd.Data);
                    return;
                }

                if (cmd.Type.ToUpper() == "SIM_MODE" && !string.IsNullOrEmpty(cmd.Data))
                {
                    try
                    {
                        var d = JsonSerializer.Deserialize<JsonElement>(cmd.Data);
                        var on = d.TryGetProperty("on", out var ov) && ov.GetBoolean();
                        _commMgr.SetSimulationMode(on);
                    }
                    catch { _commMgr.SetSimulationMode(false); }
                    return;
                }

                // ── 리포트 데이터 요청 (DB 집계) ─────────────────────
                if (cmd.Type.ToUpper() == "REQUEST_REPORT_DATA" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleRequestReportDataAsync(cmd.Data);
                    return;
                }

                // ── 배관 머신 처리 이력 저장 ─────────────────────────
                if (cmd.Type.ToUpper() == "SAVE_PIPE_MACHINE" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSavePipeMachineAsync(cmd.Data);
                    return;
                }
                if (cmd.Type.ToUpper() == "SAVE_PIPE_STAGE" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSavePipeStageAsync(cmd.Data);
                    return;
                }
                if (cmd.Type.ToUpper() == "SAVE_ALARM_HISTORY" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSaveAlarmHistoryAsync(cmd.Data);
                    return;
                }
                if (cmd.Type.ToUpper() == "SAVE_AUDIT_LOG" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSaveAuditLogAsync(cmd.Data);
                    return;
                }

                // ── 프로젝트 + 배관 일괄 저장 요청 ─────────────────────
                if (cmd.Type.ToUpper() == "SAVE_PROJECT_PIPES" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSaveProjectPipesAsync(cmd.Data);
                    return;
                }

                // ── 생산 실적 저장 요청 ──────────────────────────────
                if (cmd.Type.ToUpper() == "SAVE_PRODUCTION" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSaveProductionAsync(cmd.Data);
                    return;
                }
                if (cmd.Type.ToUpper() == "REQUEST_HISTORY" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleRequestHistoryAsync(cmd.Data);
                    return;
                }
                if (cmd.Type.ToUpper() == "AUTH_LOGIN" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleAuthLoginAsync(cmd.Data);
                    return;
                }

                if (cmd.Type.ToUpper() == "REQUEST_USERS")
                {
                    if (!IsAdminUserMgmtCommand(cmd))
                    {
                        await SendUserMgmtDeniedAsync("REQUEST_USERS");
                        return;
                    }
                    await HandleRequestUsersAsync();
                    return;
                }

                if (cmd.Type.ToUpper() == "UPSERT_USER" && !string.IsNullOrEmpty(cmd.Data))
                {
                    if (!IsAdminUserMgmtCommand(cmd))
                    {
                        await SendUserMgmtDeniedAsync("UPSERT_USER");
                        return;
                    }
                    await HandleUpsertUserAsync(cmd.Data);
                    return;
                }

                if (cmd.Type.ToUpper() == "DELETE_USER" && !string.IsNullOrEmpty(cmd.Data))
                {
                    if (!IsAdminUserMgmtCommand(cmd))
                    {
                        await SendUserMgmtDeniedAsync("DELETE_USER");
                        return;
                    }
                    await HandleDeleteUserAsync(cmd.Data);
                    return;
                }

                if (cmd.Type.ToUpper() == "SETTINGS" && !string.IsNullOrEmpty(cmd.Data))
                {
                    await HandleSettingsChangeAsync(cmd.Target, cmd.Data);
                    return;
                }

                await _commMgr.HandleWebCommandAsync(cmd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[명령 처리 오류] {ex.Message}");
            }
        }

        // ── 리포트용 DB 데이터 집계 → JS 전달 ──────────────────
        private async Task HandleRequestReportDataAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d    = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(dataJson);
                int year = d.TryGetProperty("year",  out var y) ? y.GetInt32() : DateTime.Now.Year;
                int month= d.TryGetProperty("month", out var m) ? m.GetInt32() : DateTime.Now.Month;

                var reportData = await _db.GetReportDataAsync(year, month);

                var json = System.Text.Json.JsonSerializer.Serialize(new {
                    type = "reportData",
                    data = reportData
                });
                Dispatcher.Invoke(() => SendToWebView(json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 리포트 조회 오류: {ex.Message}");
            }
        }

        // ── 배관 머신 처리 이력 DB 저장 ─────────────────────────
        private async Task HandleSavePipeMachineAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(dataJson);
                string pipeId      = d.GetProperty("pipeId").GetString()      ?? "";
                string projectId   = d.GetProperty("projectId").GetString()   ?? "";
                string machineId   = d.GetProperty("machineId").GetString()   ?? "";
                string? machineName= d.TryGetProperty("machineName", out var mn) ? mn.GetString() : null;
                string? configId   = d.TryGetProperty("configId",    out var ci) ? ci.GetString() : null;
                string? operatorId = d.TryGetProperty("operatorId",  out var oi) ? oi.GetString() : null;
                string  result     = d.TryGetProperty("result",      out var rs) ? rs.GetString() ?? "완료" : "완료";
                int? cycleTimeSec  = d.TryGetProperty("cycleTimeSec", out var ct) && ct.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? ct.GetInt32() : (int?)null;

                await _db.LogPipeMachineAsync(
                    pipeId, projectId, machineId, machineName,
                    configId, operatorId, result, cycleTimeSec);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 머신 이력 저장 오류: {ex.Message}");
            }
        }

        private async Task HandleSavePipeStageAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                await _db.SavePipeStageHistoryAsync(
                    d.GetProperty("pipeId").GetString() ?? "",
                    d.GetProperty("projectId").GetString() ?? "",
                    d.GetProperty("stageId").GetString() ?? "",
                    d.TryGetProperty("startedAt", out var st) && DateTime.TryParse(st.GetString(), out var started) ? started : DateTime.Now,
                    d.TryGetProperty("endedAt", out var et) && DateTime.TryParse(et.GetString(), out var ended) ? ended : null,
                    d.TryGetProperty("result", out var rs) ? rs.GetString() ?? "IN_PROGRESS" : "IN_PROGRESS",
                    d.TryGetProperty("holdReasonCode", out var hr) ? hr.GetString() : null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] stage 이력 저장 오류: {ex.Message}");
            }
        }

        private async Task HandleSaveAlarmHistoryAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                await _db.SaveAlarmHistoryAsync(
                    d.GetProperty("machineId").GetString() ?? "",
                    d.TryGetProperty("errorCode", out var ec) ? ec.GetString() ?? "UNKNOWN" : "UNKNOWN",
                    d.TryGetProperty("message", out var ms) ? ms.GetString() : null,
                    d.TryGetProperty("startedAt", out var st) && DateTime.TryParse(st.GetString(), out var started) ? started : DateTime.Now,
                    d.TryGetProperty("clearedAt", out var ct) && DateTime.TryParse(ct.GetString(), out var cleared) ? cleared : null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] alarm 이력 저장 오류: {ex.Message}");
            }
        }

        private async Task HandleSaveAuditLogAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                await _db.SaveAuditLogAsync(
                    d.TryGetProperty("userId", out var uid) ? uid.GetString() : null,
                    d.TryGetProperty("action", out var act) ? act.GetString() ?? "UNKNOWN" : "UNKNOWN",
                    d.TryGetProperty("target", out var tar) ? tar.GetString() : null,
                    d.TryGetProperty("payload", out var pl) ? pl.GetRawText() : null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] audit 저장 오류: {ex.Message}");
            }
        }

        // ── 프로젝트 + 배관 일괄 DB 저장 ────────────────────────
        private async Task HandleSaveProjectPipesAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            int savedPipes = 0, skippedPipes = 0;
            string projectName = "";
            try
            {
                var d = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(dataJson);
                string projectId   = d.GetProperty("projectId").GetString()   ?? "";
                projectName        = d.GetProperty("projectName").GetString() ?? projectId;
                string fileType    = d.TryGetProperty("fileType", out var ft) ? ft.GetString() ?? "CSV" : "CSV";
                string addedAtStr  = d.TryGetProperty("addedAt",  out var aa) ? aa.GetString() ?? "" : "";
                DateTime addedAt   = DateTime.TryParse(addedAtStr, out var dt) ? dt : DateTime.Today;

                // ① 프로젝트 저장 (이미 있으면 스킵)
                await _db.SaveProjectAsync(projectId, projectName, fileType, addedAt);

                // ② 배관 목록 저장
                if (d.TryGetProperty("pipes", out var pipesEl) && pipesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var p in pipesEl.EnumerateArray())
                    {
                        string pipeId  = p.GetProperty("pipeId").GetString()  ?? "";
                        string mat     = p.TryGetProperty("material", out var m) ? m.GetString() ?? "SS400" : "SS400";
                        int    size    = p.TryGetProperty("size",     out var s) ? s.GetInt32() : 32;
                        int? totalLength = p.TryGetProperty("totalLength", out var tl) && tl.ValueKind != JsonValueKind.Null ? tl.GetInt32() : null;
                        string? pipeName = p.TryGetProperty("pipeName", out var pn) ? pn.GetString() : null;
                        string status  = p.TryGetProperty("status",   out var st)? st.GetString() ?? "미완료" : "미완료";

                        bool isNew = await _db.SavePipeAsync(pipeId, projectId, projectName, mat, size, status, totalLength, pipeName);
                        if (isNew) savedPipes++;
                        else       skippedPipes++;
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[DB] 프로젝트 저장: {projectName} | 배관 신규 {savedPipes}개 | 중복 {skippedPipes}개");

                // ③ 저장 결과 HTML 피드백
                var feedbackJson = System.Text.Json.JsonSerializer.Serialize(new {
                    type = "dbProjectSaved",
                    data = new { projectId, projectName, savedPipes, skippedPipes }
                });
                Dispatcher.Invoke(() => SendToWebView(feedbackJson));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 프로젝트 저장 오류: {ex.Message}");
                var errJson = System.Text.Json.JsonSerializer.Serialize(new {
                    type = "dbProjectError",
                    data = new { message = ex.Message, projectName }
                });
                Dispatcher.Invoke(() => SendToWebView(errJson));
            }
        }

        // ── 생산 실적 DB 저장 ────────────────────────────────────
        private async Task HandleSaveProductionAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(dataJson);
                string pipeId    = d.GetProperty("pipeId").GetString()    ?? "";
                string projectId = d.GetProperty("projectId").GetString() ?? "";
                string configId  = d.GetProperty("configId").GetString()  ?? "";
                string result    = d.GetProperty("result").GetString()    ?? "완료";
                string? defect   = d.TryGetProperty("defectType", out var dt) ? dt.GetString() : null;
                int? cycleTime   = d.TryGetProperty("cycleTimeSec", out var ct) && ct.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? ct.GetInt32() : (int?)null;
                double oee       = d.TryGetProperty("oee", out var ov) ? ov.GetDouble() : 0;
                string? opId     = d.TryGetProperty("operatorId", out var op) ? op.GetString() : null;
                string pipeMat   = d.TryGetProperty("material", out var mt) ? mt.GetString() ?? "SS400" : "SS400";
                int pipeSize     = d.TryGetProperty("size", out var sz) ? sz.GetInt32() : 32;
                string pipeStatus= d.TryGetProperty("pipeStatus", out var ps) ? ps.GetString() ?? "완료" : "완료";
                string projName  = d.TryGetProperty("projName", out var pn) ? pn.GetString() ?? projectId : projectId;

                // ① project가 없으면 자동 생성
                await _db.SaveProjectAsync(projectId, projName, "DEMO", DateTime.Today);

                // ② pipe가 없으면 자동 생성
                await _db.SavePipeAsync(pipeId, projectId, projName, pipeMat, pipeSize, pipeStatus); // 중복이면 skip

                // ③ 생산 실적 저장
                await _db.SaveProductionRecordAsync(pipeId, projectId, configId, result, defect, cycleTime, oee, opId);

                System.Diagnostics.Debug.WriteLine($"[DB] 생산 실적 저장 완료: {pipeId} → {result} | 사이클:{cycleTime}초 | OEE:{oee}%");

                // ④ HTML에 저장 성공 알림 전달
                var feedbackJson = System.Text.Json.JsonSerializer.Serialize(new {
                    type = "dbSaved",
                    data = new { pipeId, result, cycleTime, oee }
                });
                Dispatcher.Invoke(() => SendToWebView(feedbackJson));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 생산 실적 저장 오류: {ex.Message}");
                // HTML에 오류 알림
                var errJson = System.Text.Json.JsonSerializer.Serialize(new {
                    type = "dbError",
                    data = new { message = ex.Message }
                });
                Dispatcher.Invoke(() => SendToWebView(errJson));
            }
        }

        private async Task HandleRequestHistoryAsync(string dataJson)
        {
            if (_db == null || !_db.IsAvailable) return;
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                DateTime? from = d.TryGetProperty("from", out var fv) && DateTime.TryParse(fv.GetString(), out var fd) ? fd : null;
                DateTime? to = d.TryGetProperty("to", out var tv) && DateTime.TryParse(tv.GetString(), out var td) ? td : null;
                string? projectId = d.TryGetProperty("projectId", out var pv) ? pv.GetString() : null;
                string? status = d.TryGetProperty("status", out var sv) ? sv.GetString() : null;
                var rows = await _db.QueryHistoryAsync(from, to, projectId, status);
                var json = JsonSerializer.Serialize(new { type = "historyData", data = rows });
                Dispatcher.Invoke(() => SendToWebView(json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] history 조회 오류: {ex.Message}");
            }
        }

        private async Task HandleAuthLoginAsync(string dataJson)
        {
            string userId = "";
            string reqId = "";
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                userId = d.GetProperty("id").GetString() ?? "";
                var pw = d.GetProperty("pw").GetString() ?? "";
                reqId = d.TryGetProperty("reqId", out var rq) ? rq.GetString() ?? "" : "";

                if (_db == null || !_db.IsAvailable)
                {
                    var failNoDb = JsonSerializer.Serialize(new
                    {
                        type = "authResult",
                        data = new { ok = false, role = "", userName = "", userId, reqId, message = "DB_UNAVAILABLE" }
                    });
                    Dispatcher.Invoke(() => SendToWebView(failNoDb));
                    return;
                }

                var result = await _db.AuthenticateAsync(userId, pw);
                var json = JsonSerializer.Serialize(new
                {
                    type = "authResult",
                    data = new
                    {
                        ok = result.ok,
                        role = result.role,
                        userName = result.userName,
                        userId,
                        reqId,
                        message = result.ok ? "OK" : "INVALID_CREDENTIALS"
                    }
                });
                Dispatcher.Invoke(() => SendToWebView(json));
                if (result.ok)
                    await _db.SaveAuditLogAsync(userId, "LOGIN_SUCCESS", "AUTH", "{}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] auth 오류: {ex.Message}");
                try
                {
                    var failJson = JsonSerializer.Serialize(new
                    {
                        type = "authResult",
                        data = new { ok = false, role = "", userName = "", userId, reqId, message = "AUTH_EXCEPTION" }
                    });
                    Dispatcher.Invoke(() => SendToWebView(failJson));
                }
                catch { }
            }
        }

        private async Task HandleRequestUsersAsync()
        {
            if (_db == null || !_db.IsAvailable)
            {
                Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                {
                    type = "usersData",
                    data = Array.Empty<object>()
                })));
                return;
            }
            var users = await _db.GetUsersAsync();
            Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
            {
                type = "usersData",
                data = users
            })));
        }

        private async Task HandleUpsertUserAsync(string dataJson)
        {
            try
            {
                if (_db == null || !_db.IsAvailable)
                {
                    Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                    {
                        type = "userSaved",
                        data = new { ok = false, message = "DB_UNAVAILABLE" }
                    })));
                    return;
                }
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                var userId = d.GetProperty("id").GetString() ?? "";
                var userName = d.TryGetProperty("name", out var nv) ? nv.GetString() ?? userId : userId;
                var pw = d.TryGetProperty("pw", out var pv) ? pv.GetString() ?? "" : "";
                var role = d.TryGetProperty("role", out var rv) ? rv.GetString() ?? "worker" : "worker";
                var res = await _db.UpsertUserAsync(userId, userName, pw, role);
                Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                {
                    type = "userSaved",
                    data = new { ok = res.ok, message = res.message, id = userId }
                })));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                {
                    type = "userSaved",
                    data = new { ok = false, message = ex.Message }
                })));
            }
        }

        private async Task HandleDeleteUserAsync(string dataJson)
        {
            try
            {
                if (_db == null || !_db.IsAvailable)
                {
                    Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                    {
                        type = "userDeleted",
                        data = new { ok = false, message = "DB_UNAVAILABLE" }
                    })));
                    return;
                }
                var d = JsonSerializer.Deserialize<JsonElement>(dataJson);
                var userId = d.GetProperty("id").GetString() ?? "";
                var res = await _db.DeactivateUserAsync(userId);
                Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                {
                    type = "userDeleted",
                    data = new { ok = res.ok, message = res.message, id = userId }
                })));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
                {
                    type = "userDeleted",
                    data = new { ok = false, message = ex.Message }
                })));
            }
        }

        private static bool IsAdminUserMgmtCommand(WebCommand cmd)
        {
            if (cmd == null) return false;
            if (!string.Equals(cmd.Target, "ADMIN", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (string.IsNullOrWhiteSpace(cmd.Data)) return false;
                var d = JsonSerializer.Deserialize<JsonElement>(cmd.Data);
                var actorRole = d.TryGetProperty("actorRole", out var rv) ? rv.GetString() ?? "" : "";
                return string.Equals(actorRole, "admin", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private Task SendUserMgmtDeniedAsync(string action)
        {
            Dispatcher.Invoke(() => SendToWebView(JsonSerializer.Serialize(new
            {
                type = "userSaved",
                data = new { ok = false, message = $"{action}:FORBIDDEN_ADMIN_ONLY" }
            })));
            return Task.CompletedTask;
        }

        // ── 활성 머신 목록 변경 처리 ─────────────────────────────
        // JS 구성 선택 시 호출 — 비활성 머신 연결 해제 + 활성 머신 연결 시도
        private async Task HandleSetActiveMachinesAsync(string dataJson)
        {
            try
            {
                // dataJson = ["CUTTING","BENDING"] 형태
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dataJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (ids == null) return;

                System.Diagnostics.Debug.WriteLine($"[활성 머신] {string.Join(", ", ids)}");

                // 비활성 머신 즉시 연결 해제 + 활성 머신만 연결 시도
                _commMgr.SetActiveMachines(ids);
                await _commMgr.ConnectAllAsync();
                // ※ syncSettings 전송 제거 — InitializeWebView에서 이미 1회 전송
                //   중복 전송 시 JS applyConfigToComm 반복 호출 → 설정 패널 강제 닫힘
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[활성 머신 설정 오류] {ex.Message}");
            }
        }

        // ── IP/Port 설정 변경 처리 ────────────────────────────────
        private async Task HandleSettingsChangeAsync(string machineId, string dataJson)
        {
            try
            {
                var setting = JsonSerializer.Deserialize<MachineNetworkSetting>(dataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (setting == null) return;

                System.Diagnostics.Debug.WriteLine(
                    $"[설정변경] {machineId} → IP: {setting.Ip}, Port: {setting.Port}");

                await _commMgr!.UpdateMachineSettingsAsync(machineId, setting.Ip, setting.Port);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[설정변경 오류] {ex.Message}");
            }
        }

        // ── C# → HTML 데이터 전송 ────────────────────────────────
        // _isClosing 체크 + ObjectDisposedException 방어
        public async void SendToWebView(string jsonData)
        {
            if (_isClosing) return;
            if (webView?.CoreWebView2 == null) return;
            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.onCSharpMessage && window.onCSharpMessage({jsonData})");
            }
            catch (ObjectDisposedException)
            {
                // 창이 닫히는 타이밍에 발생 — 무시
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SendToWebView 오류] {ex.Message}");
            }
        }

        // ── DB 초기화 ─────────────────────────────────────────────
        private async Task InitializeDbAsync()
        {
            try
            {
                _db = new DbService(DB_CONN);
                bool ok = await _db.TestConnectionAsync();
                System.Diagnostics.Debug.WriteLine(ok ? "[DB] ✅ MariaDB 연결 성공" : "[DB] ❌ MariaDB 연결 실패");
                // DB 연결 결과를 JSON 직렬화로 안전하게 전달
                var dbStatusJson = System.Text.Json.JsonSerializer.Serialize(new { type = "dbStatus", data = new { connected = ok } });
                Dispatcher.Invoke(() => SendToWebView(dbStatusJson));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 초기화 실패: {ex.Message}");
            }
        }

        private void UpdateLoadingText(string text)
        {
            if (_isClosing) return;
            Dispatcher.Invoke(() => { if (loadingText != null) loadingText.Text = text; });
        }
    }

    // ── IP/Port 설정 변경 데이터 모델 ────────────────────────────
    public class MachineNetworkSetting
    {
        public string Ip   { get; set; } = "";
        public int    Port { get; set; } = 5000;
    }
}
