using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PipeBendingDashboard.Communication
{
    /// <summary>
    /// 6개 머신 TCP/IP 통신 통합 관리
    /// - 연결/해제/재연결
    /// - 100ms 주기 상태 폴링
    /// - 명령 전송 + ACK 응답 수신 (순서 보장)
    /// - 설정 파일 영구 저장 (machine_settings.json)
    /// </summary>
    public class CommunicationManager : IDisposable
    {
        // ── 설정 파일 경로 ─────────────────────────────────────────
        private static readonly string _settingsFile =
            Path.Combine(AppContext.BaseDirectory, "machine_settings.json");

        // ── 6개 머신 클라이언트 ──────────────────────────────────
        private TcpMachineClient _loaderClient;
        private TcpMachineClient _cuttingClient;
        private TcpMachineClient _laserClient;
        private TcpMachineClient _robotClient;
        private TcpMachineClient _bendingClient;
        private TcpMachineClient _bending2Client;

        // ── 상태 데이터 ──────────────────────────────────────────
        private readonly AllMachineStatus _status = new();

        // ── 폴링 타이머 ──────────────────────────────────────────
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private bool         _isPolling = false;
        private readonly int _pollIntervalMs;

        // ── 명령 ACK 직렬화 잠금 (동시 명령 방지) ───────────────
        private readonly SemaphoreSlim _cmdLock = new(1, 1);
        private readonly TimeSpan _readyRefreshInterval = TimeSpan.FromSeconds(1);
        private readonly IReadOnlyDictionary<string, IMachineProtocolAdapter> _protocolAdapters;
        private LineTopology _topology = new(new[] { "LOADER", "CUTTING", "LASER", "ROBOT", "BENDING", "BENDING2" });
        private readonly Dictionary<string, string> _lastCorrelationByMachine = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastReconnectAttemptUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reconnectingMachines = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _reconnectSync = new();
        private readonly TimeSpan _reconnectCooldown = TimeSpan.FromSeconds(3);
        private bool _simulationMode = false;

        // ── 이벤트 ───────────────────────────────────────────────
        public event Action<string>?                     StatusUpdated;  // JSON
        public event Action<string, string>?             LogAdded;       // (machineId, msg)
        public event Action<string, string, string>?     CommandAck;     // (machineId, cmdType, response)
        public event Action<MachineCommandResponse>?     CommandResponseReceived; // 구조화 응답
        /// <summary>Ready 확인 결과 이벤트 (machineId, isReady, response)</summary>
        public event Action<string, bool, string>?       ReadyChecked;

        // ══════════════════════════════════════════════════════════
        // 생성자 — 설정 파일에서 IP/Port 로드
        // ══════════════════════════════════════════════════════════
        public CommunicationManager(
            string loaderIp  = "192.168.1.10", int loaderPort  = 5000,
            string cuttingIp = "192.168.1.11", int cuttingPort = 5000,
            string laserIp   = "192.168.1.12", int laserPort   = 5000,
            string robotIp   = "192.168.1.14", int robotPort   = 5000,
            string bendingIp = "192.168.1.13", int bendingPort = 5000,
            string bending2Ip = "192.168.1.15", int bending2Port = 5000,
            int    pollIntervalMs = 100)
        {
            _pollIntervalMs = pollIntervalMs;
            _protocolAdapters = MachineProtocolRegistry.BuildDefaults();

            // ── 저장 파일 우선 적용 (파일 없으면 파라미터 기본값 사용) ──
            var saved = LoadSettingsFromFile();
            loaderIp   = GetSaved(saved, "LOADER",  "ip",   loaderIp);
            loaderPort = GetSavedInt(saved, "LOADER",  "port", loaderPort);
            cuttingIp  = GetSaved(saved, "CUTTING", "ip",   cuttingIp);
            cuttingPort= GetSavedInt(saved, "CUTTING", "port", cuttingPort);
            laserIp    = GetSaved(saved, "LASER",   "ip",   laserIp);
            laserPort  = GetSavedInt(saved, "LASER",   "port", laserPort);
            robotIp    = GetSaved(saved, "ROBOT",   "ip",   robotIp);
            robotPort  = GetSavedInt(saved, "ROBOT",   "port", robotPort);
            bendingIp  = GetSaved(saved, "BENDING", "ip",   bendingIp);
            bendingPort= GetSavedInt(saved, "BENDING", "port", bendingPort);
            bending2Ip  = GetSaved(saved, "BENDING2", "ip",   bending2Ip);
            bending2Port= GetSavedInt(saved, "BENDING2", "port", bending2Port);

            _loaderClient  = new TcpMachineClient("LOADER",  loaderIp,  loaderPort);
            _cuttingClient = new TcpMachineClient("CUTTING", cuttingIp, cuttingPort);
            _laserClient   = new TcpMachineClient("LASER",   laserIp,   laserPort);
            _robotClient   = new TcpMachineClient("ROBOT",   robotIp,   robotPort);
            _bendingClient = new TcpMachineClient("BENDING", bendingIp, bendingPort);
            _bending2Client = new TcpMachineClient("BENDING2", bending2Ip, bending2Port);

            _status.Loader.IpAddress  = loaderIp;  _status.Loader.Port  = loaderPort;
            _status.Cutting.IpAddress = cuttingIp; _status.Cutting.Port = cuttingPort;
            _status.Laser.IpAddress   = laserIp;   _status.Laser.Port   = laserPort;
            _status.Robot.IpAddress   = robotIp;   _status.Robot.Port   = robotPort;
            _status.Bending.IpAddress = bendingIp; _status.Bending.Port = bendingPort;
            _status.Bending2.IpAddress = bending2Ip; _status.Bending2.Port = bending2Port;

            SubscribeClientEvents(_loaderClient);
            SubscribeClientEvents(_cuttingClient);
            SubscribeClientEvents(_laserClient);
            SubscribeClientEvents(_robotClient);
            SubscribeClientEvents(_bendingClient);
            SubscribeClientEvents(_bending2Client);
        }

        private void SubscribeClientEvents(TcpMachineClient c)
        {
            c.ConnectionChanged += OnConnectionChanged;
            c.LogReceived       += (id, msg) => LogAdded?.Invoke(id, msg);
        }

        // ══════════════════════════════════════════════════════════
        // 설정 파일 저장 / 로드
        // ══════════════════════════════════════════════════════════

        private void SaveSettingsToFile()
        {
            try
            {
                var data = new[]
                {
                    new { id = "LOADER",  ip = _status.Loader.IpAddress,  port = _status.Loader.Port },
                    new { id = "CUTTING", ip = _status.Cutting.IpAddress, port = _status.Cutting.Port },
                    new { id = "LASER",   ip = _status.Laser.IpAddress,   port = _status.Laser.Port },
                    new { id = "ROBOT",   ip = _status.Robot.IpAddress,   port = _status.Robot.Port },
                    new { id = "BENDING", ip = _status.Bending.IpAddress, port = _status.Bending.Port },
                    new { id = "BENDING2", ip = _status.Bending2.IpAddress, port = _status.Bending2.Port },
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
                System.Diagnostics.Debug.WriteLine($"[설정저장] {_settingsFile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[설정저장실패] {ex.Message}");
            }
        }

        private static List<Dictionary<string, JsonElement>>? LoadSettingsFromFile()
        {
            try
            {
                if (!File.Exists(_settingsFile)) return null;
                var json = File.ReadAllText(_settingsFile);
                return JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            }
            catch { return null; }
        }

        private static string GetSaved(
            List<Dictionary<string, JsonElement>>? list,
            string machineId, string key, string def)
        {
            if (list == null) return def;
            foreach (var d in list)
            {
                if (d.TryGetValue("id", out var idEl) &&
                    idEl.GetString()?.Equals(machineId, StringComparison.OrdinalIgnoreCase) == true &&
                    d.TryGetValue(key, out var v))
                    return v.GetString() ?? def;
            }
            return def;
        }

        private static int GetSavedInt(
            List<Dictionary<string, JsonElement>>? list,
            string machineId, string key, int def)
        {
            if (list == null) return def;
            foreach (var d in list)
            {
                if (d.TryGetValue("id", out var idEl) &&
                    idEl.GetString()?.Equals(machineId, StringComparison.OrdinalIgnoreCase) == true &&
                    d.TryGetValue(key, out var v))
                    return v.TryGetInt32(out var n) ? n : def;
            }
            return def;
        }

        // ── 현재 설정 JSON 반환 (HTML 초기화 시 동기화용) ────────
        public string GetCurrentSettingsJson()
        {
            var list = new[]
            {
                new { id = "LOADER",  ip = _status.Loader.IpAddress,  port = _status.Loader.Port },
                new { id = "CUTTING", ip = _status.Cutting.IpAddress, port = _status.Cutting.Port },
                new { id = "LASER",   ip = _status.Laser.IpAddress,   port = _status.Laser.Port },
                new { id = "ROBOT",   ip = _status.Robot.IpAddress,   port = _status.Robot.Port },
                new { id = "BENDING", ip = _status.Bending.IpAddress, port = _status.Bending.Port },
                new { id = "BENDING2", ip = _status.Bending2.IpAddress, port = _status.Bending2.Port },
            };
            return JsonSerializer.Serialize(list,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        // ══════════════════════════════════════════════════════════
        // 연결
        // ══════════════════════════════════════════════════════════

        // ── 활성 머신 목록 (JS 구성에서 수신) ───────────────────
        private HashSet<string> _activeMachineIds = new() { "LOADER", "CUTTING", "LASER", "ROBOT", "BENDING", "BENDING2" };

        /// <summary>
        /// JS에서 선택된 구성의 활성 머신 목록을 설정
        /// 비활성 머신은 연결하지 않고 즉시 연결 해제
        /// </summary>
        public void SetActiveMachines(IEnumerable<string> machineIds)
        {
            var newActive = new HashSet<string>(machineIds.Select(s => s.ToUpper()));
            _activeMachineIds = newActive;
            _topology = new LineTopology(_activeMachineIds);

            // 비활성화된 머신 즉시 연결 해제
            foreach (var id in new[] { "LOADER", "CUTTING", "LASER", "ROBOT", "BENDING", "BENDING2" })
            {
                if (!_activeMachineIds.Contains(id))
                {
                    var client = GetClient(id);
                    client?.Disconnect();
                    var status = GetStatus(id);
                    if (status != null)
                    {
                        status.IsConnected = false;
                        status.IsReady     = false;
                        status.Status      = "READY";
                        status.LastMessage = "비활성 머신 (구성에서 제외됨)";
                    }
                }
            }
            NotifyStatusUpdate();
        }

        public void SetSimulationMode(bool on) => _simulationMode = on;

        public async Task ConnectAllAsync()
        {
            // 활성 머신만 연결 시도
            var tasks = new List<Task>();
            if (_activeMachineIds.Contains("LOADER"))  tasks.Add(ConnectOneAsync(_loaderClient,  _status.Loader));
            if (_activeMachineIds.Contains("CUTTING")) tasks.Add(ConnectOneAsync(_cuttingClient, _status.Cutting));
            if (_activeMachineIds.Contains("LASER"))   tasks.Add(ConnectOneAsync(_laserClient,   _status.Laser));
            if (_activeMachineIds.Contains("ROBOT"))   tasks.Add(ConnectOneAsync(_robotClient,   _status.Robot));
            if (_activeMachineIds.Contains("BENDING")) tasks.Add(ConnectOneAsync(_bendingClient, _status.Bending));
            if (_activeMachineIds.Contains("BENDING2")) tasks.Add(ConnectOneAsync(_bending2Client, _status.Bending2));

            if (tasks.Count > 0) await Task.WhenAll(tasks);
            NotifyStatusUpdate();
        }

        private async Task ConnectOneAsync(TcpMachineClient client, MachineStatus status)
        {
            status.IsConnected = await client.ConnectAsync();
            if (status.IsConnected)
            {
                status.Status      = "READY";
                status.LastMessage = "연결 성공 — Ready 확인 중...";
                // 연결 성공 시 Ready 명령 전송
                await CheckReadyAsync(client, status);
            }
            else
            {
                status.Status      = "FAULT";
                status.StateCode   = "FAULT";
                status.LastMessage = "연결 실패";
                status.IsReady     = false;
                status.HasAlarm    = true;
                status.ErrorCode   = "NET_CONNECT_FAIL";
            }
        }

        /// <summary>
        /// TCP 연결 성공 후 장비 Ready 상태 확인
        /// 응답: "OK" → Ready / 그 외 → Not Ready
        /// </summary>
        private async Task CheckReadyAsync(TcpMachineClient client, MachineStatus status)
        {
            try
            {
                var readyCmd = _protocolAdapters.TryGetValue(status.MachineId, out var adapter)
                    ? adapter.GetReadyCommand()
                    : MachineProtocol.GetReady(status.MachineId);
                if (readyCmd == null) return;

                System.Diagnostics.Debug.WriteLine($"[{status.MachineId}] Ready 확인 전송: {readyCmd.Trim()}");

                var response = await client.SendReceiveStringAsync(readyCmd);
                var resp = response?.Trim() ?? "";

                bool isReady = resp.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                status.IsReady     = isReady;
                status.LastReadyCheckedAtUtc = DateTime.UtcNow;
                if (!status.IsConnected) status.IsReady = false;
                status.LastMessage = isReady ? "Ready — 명령 대기 중" : $"Not Ready: {resp}";

                System.Diagnostics.Debug.WriteLine($"[{status.MachineId}] Ready 응답: {resp} → {(isReady ? "OK" : "NG")}");

                // HTML에 Ready 결과 전달 (이벤트 발생)
                ReadyChecked?.Invoke(status.MachineId, isReady, resp);
            }
            catch (Exception ex)
            {
                status.IsReady     = false;
                status.LastReadyCheckedAtUtc = DateTime.UtcNow;
                status.LastMessage = $"Ready 확인 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[{status.MachineId}] Ready 실패: {ex.Message}");
                ReadyChecked?.Invoke(status.MachineId, false, ex.Message);
            }
        }

        public void DisconnectAll()
        {
            StopPolling();
            _loaderClient.Disconnect();
            _cuttingClient.Disconnect();
            _laserClient.Disconnect();
            _robotClient.Disconnect();
            _bendingClient.Disconnect();
            _bending2Client.Disconnect();
        }

        // ══════════════════════════════════════════════════════════
        // 폴링
        // ══════════════════════════════════════════════════════════

        public void StartPolling()
        {
            if (_isPolling) return;
            _isPolling = true;
            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
        }

        public void StopPolling()
        {
            _isPolling = false;
            _pollCts?.Cancel();
            try { _pollTask?.Wait(1000); } catch { }
            _pollTask = null;
            _pollCts?.Dispose();
            _pollCts = null;
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await PollAllAsync(); } catch { }
                try { await Task.Delay(_pollIntervalMs, ct); }
                catch (TaskCanceledException) { break; }
            }
        }

        private async Task PollAllAsync()
        {
            if (!await _cmdLock.WaitAsync(0)) return;
            try
            {
                // 활성 머신만 폴링
                var tasks = new List<Task>();
                if (_activeMachineIds.Contains("LOADER"))  tasks.Add(PollOneAsync(_loaderClient,  _status.Loader,  _protocolAdapters["LOADER"].GetStatusCommand()));
                if (_activeMachineIds.Contains("CUTTING")) tasks.Add(PollOneAsync(_cuttingClient, _status.Cutting, _protocolAdapters["CUTTING"].GetStatusCommand()));
                if (_activeMachineIds.Contains("LASER"))   tasks.Add(PollOneAsync(_laserClient,   _status.Laser,   _protocolAdapters["LASER"].GetStatusCommand()));
                if (_activeMachineIds.Contains("ROBOT"))   tasks.Add(PollOneAsync(_robotClient,   _status.Robot,   _protocolAdapters["ROBOT"].GetStatusCommand()));
                if (_activeMachineIds.Contains("BENDING")) tasks.Add(PollOneAsync(_bendingClient, _status.Bending, _protocolAdapters["BENDING"].GetStatusCommand()));
                if (_activeMachineIds.Contains("BENDING2")) tasks.Add(PollOneAsync(_bending2Client, _status.Bending2, _protocolAdapters["BENDING2"].GetStatusCommand()));

                if (tasks.Count > 0) await Task.WhenAll(tasks);
                NotifyStatusUpdate();
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        private async Task PollOneAsync(TcpMachineClient client, MachineStatus status, string command)
        {
            if (!client.IsConnected)
            {
                status.IsConnected = false;
                status.IsReady = false;
                status.Status = "FAULT";
                status.StateCode = "FAULT";
                status.HasAlarm = true;
                status.ErrorCode = "NET_DISCONNECTED";
                status.LastMessage = "연결 끊김";
                _ = TryAutoReconnectAsync(status.MachineId);
                return;
            }
            try
            {
                var response = await client.SendReceiveStringAsync(command);
                if (response == null)
                {
                    status.IsConnected = false;
                    status.IsReady = false;
                    status.Status = "FAULT";
                    status.StateCode = "FAULT";
                    status.HasAlarm = true;
                    status.ErrorCode = "NET_NO_RESPONSE";
                    status.LastMessage = "응답 없음";
                    _ = TryAutoReconnectAsync(status.MachineId);
                    return;
                }
                status.IsConnected  = true;
                status.LastMessage  = response.Trim();
                var parsed = _protocolAdapters.TryGetValue(status.MachineId, out var adapter)
                    ? adapter.ParseStatus(response)
                    : MachineProtocol.ParseStatusResponse(response);
                ApplyParsedStatus(status, parsed);
                if (TryParseValue(response, "SPEED:", out double spd)) status.Speed = spd;
                if (TryParseValue(response, "OEE:",   out double oee)) status.Oee   = oee;
                await RefreshReadyStateAsync(client, status, response);
            }
            catch (Exception ex)
            {
                status.IsConnected = false;
                status.IsReady = false;
                status.Status = "FAULT";
                status.StateCode = "FAULT";
                status.HasAlarm = true;
                status.ErrorCode = "NET_EXCEPTION";
                status.LastMessage = ex.Message;
                _ = TryAutoReconnectAsync(status.MachineId);
            }
        }

        private Task TryAutoReconnectAsync(string machineId)
        {
            var id = machineId.ToUpperInvariant();
            if (!_activeMachineIds.Contains(id)) return Task.CompletedTask;

            lock (_reconnectSync)
            {
                if (_reconnectingMachines.Contains(id)) return Task.CompletedTask;
                if (_lastReconnectAttemptUtc.TryGetValue(id, out var lastAt)
                    && DateTime.UtcNow - lastAt < _reconnectCooldown)
                {
                    return Task.CompletedTask;
                }
                _lastReconnectAttemptUtc[id] = DateTime.UtcNow;
                _reconnectingMachines.Add(id);
            }

            return Task.Run(async () =>
            {
                try
                {
                    var client = GetClient(id);
                    var status = GetStatus(id);
                    if (client == null || status == null) return;
                    if (client.IsConnected) return;

                    LogAdded?.Invoke(id, $"[{id}] 자동 재연결 시도...");
                    await ConnectOneAsync(client, status);
                    NotifyStatusUpdate();
                }
                catch (Exception ex)
                {
                    LogAdded?.Invoke(id, $"[{id}] 자동 재연결 실패: {ex.Message}");
                }
                finally
                {
                    lock (_reconnectSync)
                    {
                        _reconnectingMachines.Remove(id);
                    }
                }
            });
        }

        // ══════════════════════════════════════════════════════════
        // 런타임 IP/Port 변경 → 파일 저장 → 재연결
        // ══════════════════════════════════════════════════════════
        public async Task UpdateMachineSettingsAsync(string machineId, string newIp, int newPort)
        {
            var oldClient = GetClient(machineId);
            if (oldClient == null) return;
            oldClient.Disconnect();

            var newClient = new TcpMachineClient(machineId, newIp, newPort);
            SubscribeClientEvents(newClient);

            switch (machineId.ToUpper())
            {
                case "LOADER":  _loaderClient  = newClient; break;
                case "CUTTING": _cuttingClient = newClient; break;
                case "LASER":   _laserClient   = newClient; break;
                case "ROBOT":   _robotClient   = newClient; break;
                case "BENDING": _bendingClient = newClient; break;
                case "BENDING2": _bending2Client = newClient; break;
                default: return;
            }

            var status = GetStatus(machineId);
            if (status != null) { status.IpAddress = newIp; status.Port = newPort; }

            // ── 설정 파일 영구 저장 ──────────────────────────────
            SaveSettingsToFile();

            await ConnectOneAsync(GetClient(machineId)!, status ?? new MachineStatus());
            NotifyStatusUpdate();
        }

        // ══════════════════════════════════════════════════════════
        // HTML → C# 명령 처리 (ACK 응답 수신 후 HTML로 전달)
        // ══════════════════════════════════════════════════════════
        public async Task HandleWebCommandAsync(WebCommand cmd)
        {
            if (cmd.Target.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAllTargetAsync(cmd);
                return;
            }

            if (!TryBuildCommandRequest(cmd, out var request, out var requestError))
            {
                CommandAck?.Invoke(cmd.Target, cmd.Type, requestError);
                return;
            }

            var adapter = _protocolAdapters.TryGetValue(request.TargetMachineId, out var found) ? found : null;
            if (adapter == null)
            {
                CommandAck?.Invoke(cmd.Target, cmd.Type, "ERROR:UNKNOWN_MACHINE");
                return;
            }
            var protocol = adapter.EncodeRequest(request);
            if (string.IsNullOrWhiteSpace(protocol))
            {
                CommandAck?.Invoke(request.TargetMachineId, request.CommandType.ToString().ToUpperInvariant(), "ERROR:UNSUPPORTED_COMMAND");
                return;
            }

            // ── 직렬화: 이전 명령 완료 후 다음 명령 처리 ──────────
            if (!await _cmdLock.WaitAsync(5000))
            {
                var timeoutMsg = "이전 명령 대기 중 — 타임아웃";
                LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ✗ {timeoutMsg}");
                CommandAck?.Invoke(request.TargetMachineId, request.CommandType.ToString().ToUpperInvariant(), "ERROR:TIMEOUT");
                return;
            }

            try
            {
                var targetId = request.TargetMachineId.ToUpperInvariant();
                var cmdType  = request.CommandType.ToString().ToUpperInvariant();
                if (!_activeMachineIds.Contains(targetId))
                {
                    LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ✗ 비활성 머신");
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, "ERROR:INACTIVE_MACHINE");
                    return;
                }

                var client = GetClient(request.TargetMachineId);
                if (client == null || !client.IsConnected)
                {
                    LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ✗ 연결되지 않음");
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, "ERROR:NOT_CONNECTED");
                    return;
                }

                var status = GetStatus(request.TargetMachineId);
                if (status == null)
                {
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, "ERROR:UNKNOWN_MACHINE");
                    return;
                }

                if (request.CommandType == MachineCommandType.Start && !status.IsReady)
                {
                    LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ✗ READY 인터락 미충족");
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, "ERROR:NOT_READY");
                    return;
                }

                var lineInterlockError = ValidateLineInterlock(request, status);
                if (lineInterlockError != null)
                {
                    LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ✗ 라인 인터락 거부: {lineInterlockError}");
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, lineInterlockError);
                    return;
                }

                LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] → 전송: {cmdType}");

                // ── 명령 전송 + 장비 ACK 응답 수신 ─────────────────
                var response = await client.SendReceiveStringAsync(protocol);

                if (string.IsNullOrWhiteSpace(response))
                {
                    LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ✗ ACK 없음 (응답 타임아웃)");
                    var noAck = new MachineCommandResponse
                    {
                        MachineId = request.TargetMachineId,
                        CommandType = request.CommandType,
                        ResponseType = MachineResponseType.Error,
                        IsSuccess = false,
                        ErrorCode = "NO_ACK",
                        CorrelationId = request.CorrelationId,
                        RawResponse = ""
                    };
                    CommandResponseReceived?.Invoke(noAck);
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, "ERROR:NO_ACK");
                    return;
                }

                var ack = response.Trim();
                LogAdded?.Invoke(request.TargetMachineId, $"[{request.TargetMachineId}] ← 응답: {SummarizeOperatorResponse(ack)}");
                var structured = adapter.DecodeResponse(request, ack);
                if (!string.IsNullOrWhiteSpace(structured.CorrelationId)
                    && !string.Equals(structured.CorrelationId, request.CorrelationId, StringComparison.OrdinalIgnoreCase))
                {
                    CommandAck?.Invoke(request.TargetMachineId, cmdType, "ERROR:STALE_RESPONSE");
                    return;
                }
                _lastCorrelationByMachine[request.TargetMachineId] = request.CorrelationId;
                CommandResponseReceived?.Invoke(structured);

                // ── 상태 즉시 갱신 ───────────────────────────────────
                status.LastMessage = ack;
                if (structured.ResponseType == MachineResponseType.Alarm
                    || structured.ResponseType == MachineResponseType.Fault
                    || structured.ResponseType == MachineResponseType.Offline
                    || structured.ResponseType == MachineResponseType.EmergencyStop
                    || structured.ResponseType == MachineResponseType.Rejected
                    || structured.ResponseType == MachineResponseType.Error)
                {
                    status.HasAlarm = true;
                    status.Status = "FAULT";
                    status.StateCode = "FAULT";
                    if (!string.IsNullOrWhiteSpace(structured.ErrorCode)) status.ErrorCode = structured.ErrorCode;
                }

                if (structured.IsSuccess)
                {
                    switch (request.CommandType)
                    {
                        case MachineCommandType.Start:
                        case MachineCommandType.ExecuteJob:
                        case MachineCommandType.CuttingJob:
                        case MachineCommandType.MarkingJob:
                        case MachineCommandType.BendingJob:
                        case MachineCommandType.MoveTransfer:
                            status.Status = "WORKING";
                            status.StateCode = "BUSY";
                            status.IsReady = false;
                            break;
                        case MachineCommandType.Stop:
                            status.Status = "STOPPED";
                            status.StateCode = "IDLE";
                            status.IsReady = true;
                            status.HasAlarm = false;
                            status.ErrorCode = "";
                            break;
                        case MachineCommandType.EmergencyStop:
                            status.Status = "FAULT";
                            status.StateCode = "ESTOP";
                            status.IsReady = false;
                            status.HasAlarm = true;
                            status.ErrorCode = "EMERGENCY_STOP";
                            break;
                        case MachineCommandType.Reset:
                        case MachineCommandType.Abort:
                            status.Status = "READY";
                            status.StateCode = "READY";
                            status.IsReady = true;
                            status.HasAlarm = false;
                            status.ErrorCode = "";
                            break;
                        case MachineCommandType.Status:
                        case MachineCommandType.Ready:
                        case MachineCommandType.LoadJob:
                        case MachineCommandType.Custom:
                            break;
                    }
                }

                // ── HTML에 ACK 결과 전달 → 다음 명령 허용 ──────────
                CommandAck?.Invoke(request.TargetMachineId, cmdType, ack);
                NotifyStatusUpdate();
            }
            finally
            {
                _cmdLock.Release();
            }
        }

        // ── 연결 상태 변경 콜백 ──────────────────────────────────
        private void OnConnectionChanged(string machineId, bool isConnected)
        {
            var status = GetStatus(machineId);
            if (status == null) return;
            status.IsConnected = isConnected;
            status.Status      = isConnected ? "READY" : "FAULT";
            status.StateCode   = isConnected ? "READY" : "FAULT";
            if (!isConnected)
            {
                status.IsReady = false;
                status.HasAlarm = true;
                status.ErrorCode = "NET_DISCONNECTED";
                status.LastMessage = "연결 끊김";
                _ = TryAutoReconnectAsync(machineId);
            }
            else
            {
                status.HasAlarm = false;
                status.ErrorCode = "";
            }
            NotifyStatusUpdate();
        }

        private void NotifyStatusUpdate()
        {
            try
            {
                var json = JsonSerializer.Serialize(_status,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                StatusUpdated?.Invoke(json);
            }
            catch { }
        }

        private TcpMachineClient? GetClient(string machineId) =>
            machineId.ToUpper() switch
            {
                "LOADER"  => _loaderClient,
                "CUTTING" => _cuttingClient,
                "LASER"   => _laserClient,
                "ROBOT"   => _robotClient,
                "BENDING" => _bendingClient,
                "BENDING2" => _bending2Client,
                _ => null
            };

        private MachineStatus? GetStatus(string machineId) =>
            machineId.ToUpper() switch
            {
                "LOADER"  => _status.Loader,
                "CUTTING" => _status.Cutting,
                "LASER"   => _status.Laser,
                "ROBOT"   => _status.Robot,
                "BENDING" => _status.Bending,
                "BENDING2" => _status.Bending2,
                _ => null
            };

        private static bool TryParseValue(string response, string key, out double value)
        {
            value = 0;
            int idx = response.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var rest = response[(idx + key.Length)..];
            var end  = rest.IndexOfAny(new[] { ',', '\r', '\n', ' ' });
            var num  = end < 0 ? rest : rest[..end];
            return double.TryParse(num, out value);
        }

        private static string SummarizeOperatorResponse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "(빈 응답)";
            var upper = raw.Trim().ToUpperInvariant();
            if (upper.StartsWith("OK")) return "OK";
            if (upper.StartsWith("ERROR") || upper.StartsWith("FAIL") || upper.StartsWith("NACK")) return "ERROR";
            if (upper.Contains("EMERGENCY_STOP") || upper.Contains("ESTOP")) return "EMERGENCY_STOP";
            if (upper.Contains("ALARM") || upper.Contains("FAULT")) return "ALARM/FAULT";
            if (upper.Contains("COMPLETE") || upper.Contains("FINISH") || upper.Contains("DONE")) return "COMPLETE";
            if (upper.Contains("WORKING") || upper.Contains("RUNNING") || upper.Contains("IN_PROGRESS")) return "IN_PROGRESS";
            if (upper.Contains("READY")) return "READY";
            return raw.Length > 64 ? raw[..64] + "..." : raw;
        }

        private async Task HandleAllTargetAsync(WebCommand cmd)
        {
            var type = cmd.Type.ToUpperInvariant();
            // 정책: ALL 대상은 STOP/STATUS/RESET/ABORT만 허용
            if (type != "STOP" && type != "STATUS" && type != "RESET" && type != "ABORT")
            {
                CommandAck?.Invoke("ALL", cmd.Type, "ERROR:ALL_NOT_ALLOWED");
                return;
            }

            var targets = _activeMachineIds.ToArray();
            var results = new List<string>(targets.Length);
            foreach (var target in targets)
            {
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void AckHandler(string machineId, string cmdType, string response)
                {
                    if (machineId.Equals(target, StringComparison.OrdinalIgnoreCase)
                        && cmdType.Equals(cmd.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        completion.TrySetResult(response);
                    }
                }

                CommandAck += AckHandler;
                try
                {
                    await HandleWebCommandAsync(new WebCommand { Type = cmd.Type, Target = target, Data = cmd.Data });
                    var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    results.Add($"{target}:{response}");
                }
                catch
                {
                    results.Add($"{target}:ERROR:TIMEOUT");
                }
                finally
                {
                    CommandAck -= AckHandler;
                }
            }

            CommandAck?.Invoke("ALL", cmd.Type, string.Join(",", results));
        }

        private async Task RefreshReadyStateAsync(TcpMachineClient client, MachineStatus status, string statusResponse)
        {
            var parsed = _protocolAdapters.TryGetValue(status.MachineId, out var adapter)
                ? adapter.ParseStatus(statusResponse)
                : MachineProtocol.ParseStatusResponse(statusResponse);
            if (parsed.IsReady == true)
            {
                status.IsReady = true;
                status.LastReadyCheckedAtUtc = DateTime.UtcNow;
                return;
            }

            if (parsed.IsReady == false)
            {
                status.IsReady = false;
                status.LastReadyCheckedAtUtc = DateTime.UtcNow;
                return;
            }

            if (DateTime.UtcNow - status.LastReadyCheckedAtUtc < _readyRefreshInterval) return;
            await CheckReadyAsync(client, status);
        }

        private static void ApplyParsedStatus(MachineStatus status, ParsedMachineStatus parsed)
        {
            var previous = status.Status;
            status.LastEvent = parsed.IsUnloadComplete ? "UNLOAD_COMPLETE" : "";

            if (parsed.Status == "ALARM")
            {
                status.Status = "FAULT";
                status.StateCode = "FAULT";
                status.HasAlarm = true;
                status.IsReady = false;
                if (!string.IsNullOrWhiteSpace(parsed.ErrorCode)) status.ErrorCode = parsed.ErrorCode;
                return;
            }

            if (previous == "FAULT")
            {
                status.IsReady = false;
                return;
            }

            if (parsed.IsUnloadComplete)
            {
                status.Status = "READY";
                status.StateCode = "READY";
                status.IsReady = true;
                status.HasAlarm = false;
                status.ErrorCode = "";
                return;
            }

            if (parsed.Status == "WORKING")
            {
                status.Status = "WORKING";
                status.StateCode = "BUSY";
                status.HasAlarm = false;
                status.ErrorCode = "";
                status.IsReady = false;
                return;
            }

            if (parsed.Status == "FINISH")
            {
                status.Status = "FINISH";
                status.StateCode = "DONE";
                status.IsReady = false;
                return;
            }

            // FINISH에서는 UNLOAD_COMPLETE 없이 READY로 직행 금지
            if (previous == "FINISH" && parsed.Status == "READY")
            {
                status.Status = "FINISH";
                status.StateCode = "DONE";
                status.IsReady = false;
                return;
            }

            // STOP 이후 READY 응답은 "연결 유지 + 정지 대기"로 유지
            if (previous == "STOPPED" && parsed.Status == "READY")
            {
                status.Status = "STOPPED";
                status.StateCode = "IDLE";
                if (parsed.IsReady.HasValue) status.IsReady = parsed.IsReady.Value;
                return;
            }

            status.Status = "READY";
            status.StateCode = "READY";
            if (parsed.IsReady.HasValue) status.IsReady = parsed.IsReady.Value;
        }

        private string? ValidateLineInterlock(MachineCommandRequest request, MachineStatus status)
        {
            var targetId = request.TargetMachineId.ToUpperInvariant();
            if (!_topology.IsActive(targetId)) return "ERROR:TOPOLOGY_INACTIVE_TARGET";

            bool IsReady(string id)
                => _topology.IsActive(id) && (GetStatus(id)?.StateCode == "READY");

            bool needsRouteCheck = request.CommandType == MachineCommandType.Start
                || request.CommandType == MachineCommandType.ExecuteJob
                || LineTopology.IsMovementCommand(request.CommandType);
            if (!needsRouteCheck) return null;

            var upstream = _topology.GetUpstreamCandidates(targetId).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(upstream) && !IsReady(upstream)) return "ERROR:UPSTREAM_NOT_READY";

            if (request.CommandType == MachineCommandType.MoveTransfer)
            {
                var payloadTarget = request.Payload.Fields.TryGetValue("toStage", out var toStage)
                    ? toStage?.ToUpperInvariant() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(payloadTarget)) return "ERROR:MISSING_TRANSFER_TARGET";
                if (!_topology.IsRouteValid(targetId, payloadTarget)) return "ERROR:INVALID_ROUTE";
                if (!IsReady(payloadTarget)) return "ERROR:DOWNSTREAM_NOT_READY";
            }

            if (request.CommandType is MachineCommandType.LoaderJob or MachineCommandType.LoadRequest or MachineCommandType.PrefetchLoad or MachineCommandType.BufferPrepare)
            {
                var down = _topology.GetDownstreamCandidates("LOADER").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(down)) return "ERROR:NO_ACTIVE_DOWNSTREAM";
                if (!IsReady("LOADER")) return "ERROR:LOADER_NOT_READY";
                if (!_simulationMode && !IsReady(down)) return "ERROR:DOWNSTREAM_NOT_READY";
            }

            if (!_simulationMode && LineTopology.IsMovementCommand(request.CommandType) && !status.IsReady)
            {
                return "ERROR:MOTION_PERMIT_NOT_READY";
            }

            return null;
        }

        public void Dispose()
        {
            _cmdLock.Dispose();
            StopPolling();
            _loaderClient.Dispose();
            _cuttingClient.Dispose();
            _laserClient.Dispose();
            _robotClient.Dispose();
            _bendingClient.Dispose();
            _bending2Client.Dispose();
        }

        public Task SendLoaderJobAsync(string targetMachineId, LoaderJobPayload payload, string? correlationId = null)
            => HandleWebCommandAsync(new WebCommand
            {
                Target = targetMachineId,
                Type = "LOADER_JOB",
                CommandType = "LOADER_JOB",
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Data = MachinePayloadFactory.FromLoader(payload).Raw
            });

        // ── 단계별 Payload 명령 전송 편의 메서드 (내부 모델 기반) ──
        public Task SendCuttingJobAsync(string targetMachineId, CuttingJobPayload payload, string? correlationId = null)
            => HandleWebCommandAsync(new WebCommand
            {
                Target = targetMachineId,
                Type = "CUTTING_JOB",
                CommandType = "CUTTING_JOB",
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Data = MachinePayloadFactory.FromCutting(payload).Raw
            });

        public Task SendMarkingJobAsync(string targetMachineId, MarkingJobPayload payload, string? correlationId = null)
            => HandleWebCommandAsync(new WebCommand
            {
                Target = targetMachineId,
                Type = "MARKING_JOB",
                CommandType = "MARKING_JOB",
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Data = MachinePayloadFactory.FromMarking(payload).Raw
            });

        public Task SendRobotTransferAsync(string targetMachineId, RobotTransferPayload payload, string? correlationId = null)
            => HandleWebCommandAsync(new WebCommand
            {
                Target = targetMachineId,
                Type = "MOVE_TRANSFER",
                CommandType = "MOVE_TRANSFER",
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Data = MachinePayloadFactory.FromRobotTransfer(payload).Raw
            });

        public Task SendBendingJobAsync(string targetMachineId, BendingJobPayload payload, string? correlationId = null)
            => HandleWebCommandAsync(new WebCommand
            {
                Target = targetMachineId,
                Type = "BENDING_JOB",
                CommandType = "BENDING_JOB",
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Data = MachinePayloadFactory.FromBending(payload).Raw
            });

        private static bool TryBuildCommandRequest(WebCommand cmd, out MachineCommandRequest request, out string error)
        {
            request = new MachineCommandRequest();
            error = "";
            if (cmd == null)
            {
                error = "ERROR:INVALID_COMMAND";
                return false;
            }

            var target = (cmd.Target ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "ERROR:TARGET_REQUIRED";
                return false;
            }

            var rawType = string.IsNullOrWhiteSpace(cmd.CommandType) ? cmd.Type : cmd.CommandType;
            if (!TryMapCommandType(rawType, out var mappedType))
            {
                error = "ERROR:UNKNOWN_COMMAND_TYPE";
                return false;
            }

            request.TargetMachineId = target;
            request.CommandType = mappedType;
            request.CommandCode = string.IsNullOrWhiteSpace(rawType) ? mappedType.ToString().ToUpperInvariant() : rawType.Trim().ToUpperInvariant();
            request.CorrelationId = string.IsNullOrWhiteSpace(cmd.CorrelationId) ? Guid.NewGuid().ToString("N") : cmd.CorrelationId.Trim();
            request.RequestedAtUtc = DateTime.TryParse(cmd.Timestamp, out var ts) ? ts.ToUniversalTime() : DateTime.UtcNow;
            request.Payload = ParsePayload(cmd.Data);
            request.Payload.TargetMachine = request.TargetMachineId;
            if (request.Payload.Fields.TryGetValue("jobId", out var jobId)) request.Payload.JobId = jobId;
            if (request.Payload.Fields.TryGetValue("pipeId", out var pipeId)) request.Payload.PipeId = pipeId;
            if (request.Payload.Fields.TryGetValue("stage", out var stage)) request.Payload.Stage = stage;
            if (request.Payload.Fields.TryGetValue("targetMachine", out var targetMachine)) request.Payload.TargetMachine = targetMachine;
            return true;
        }

        private static MachineCommandPayload ParsePayload(string? raw)
        {
            var payload = new MachineCommandPayload { Raw = raw?.Trim() ?? "" };
            if (string.IsNullOrWhiteSpace(payload.Raw)) return payload;
            try
            {
                using var doc = JsonDocument.Parse(payload.Raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return payload;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    payload.Fields[p.Name] = p.Value.ToString();
                }
            }
            catch
            {
                // legacy raw text payload 허용
            }
            return payload;
        }

        private static bool TryMapCommandType(string rawType, out MachineCommandType commandType)
        {
            commandType = MachineCommandType.Custom;
            var key = (rawType ?? "").Trim().ToUpperInvariant();
            switch (key)
            {
                case "START": commandType = MachineCommandType.Start; return true;
                case "STOP": commandType = MachineCommandType.Stop; return true;
                case "E_STOP":
                case "ESTOP":
                case "EMERGENCY_STOP":
                    commandType = MachineCommandType.EmergencyStop; return true;
                case "STATUS": commandType = MachineCommandType.Status; return true;
                case "RESET": commandType = MachineCommandType.Reset; return true;
                case "READY": case "READY?": commandType = MachineCommandType.Ready; return true;
                case "LOADER_JOB": commandType = MachineCommandType.LoaderJob; return true;
                case "LOAD_REQUEST": commandType = MachineCommandType.LoadRequest; return true;
                case "PREFETCH_LOAD": commandType = MachineCommandType.PrefetchLoad; return true;
                case "BUFFER_PREPARE": commandType = MachineCommandType.BufferPrepare; return true;
                case "LOAD_JOB": case "JOB_LOAD": commandType = MachineCommandType.LoadJob; return true;
                case "EXECUTE_JOB": case "JOB_EXEC": commandType = MachineCommandType.ExecuteJob; return true;
                case "CUTTING_JOB": case "CUT_JOB": commandType = MachineCommandType.CuttingJob; return true;
                case "MARKING_JOB": case "MARK_JOB": commandType = MachineCommandType.MarkingJob; return true;
                case "BENDING_JOB": case "BEND_JOB": commandType = MachineCommandType.BendingJob; return true;
                case "MOVE_TRANSFER": case "MOVE": commandType = MachineCommandType.MoveTransfer; return true;
                case "ABORT": commandType = MachineCommandType.Abort; return true;
                default:
                    if (string.IsNullOrWhiteSpace(key)) return false;
                    commandType = MachineCommandType.Custom;
                    return true;
            }
        }
    }
}
