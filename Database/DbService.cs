using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace PipeBendingDashboard.Database
{
    /// <summary>
    /// DB 저장/조회 서비스
    /// MainWindow에서 생성 후 통신 이벤트마다 호출
    /// </summary>
    public class DbService
    {
        private readonly string _connStr;
        private bool _isAvailable = false;

        public bool IsAvailable => _isAvailable;

        public DbService(string connectionString)
        {
            _connStr = connectionString;
        }

        // ── DB 컨텍스트 생성 헬퍼 ──────────────────────────────
        private AppDbContext CreateContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql(_connStr, ServerVersion.AutoDetect(_connStr),
                    o => o.CommandTimeout(10))
                .Options;
            return new AppDbContext(opt);
        }

        // ── 1. 연결 테스트 ──────────────────────────────────────
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync();
                await db.Database.CloseConnectionAsync();
                _isAvailable = true;
                System.Diagnostics.Debug.WriteLine("[DB] 연결 성공");
                return true;
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                System.Diagnostics.Debug.WriteLine($"[DB] 연결 실패: {ex.Message}");
                return false;
            }
        }

        // ── 2. 장비 상태 로그 저장 ──────────────────────────────
        public async Task LogMachineStatusAsync(string machineId, string status,
            bool isConnected, bool isReady, double oee, double speed, bool hasAlarm, string lastMessage)
        {
            if (!_isAvailable) return;
            try
            {
                await using var db = CreateContext();
                db.MachineStatusLogs.Add(new MachineStatusLogEntity
                {
                    MachineId   = machineId,
                    Status      = status,
                    IsConnected = isConnected,
                    IsReady     = isReady,
                    Oee         = (decimal)oee,
                    Speed       = (decimal)speed,
                    HasAlarm    = hasAlarm,
                    LastMessage = lastMessage,
                    LoggedAt    = DateTime.Now,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 상태 로그 저장 실패: {ex.Message}");
            }
        }

        // ── 3. 통신 로그 저장 ────────────────────────────────────
        public async Task LogCommAsync(string? machineId, string logType,
            string? command, string? response, bool? isSuccess)
        {
            if (!_isAvailable) return;
            try
            {
                await using var db = CreateContext();
                db.CommLogs.Add(new CommLogEntity
                {
                    MachineId = machineId,
                    LogType   = logType,
                    Command   = command,
                    Response  = response,
                    IsSuccess = isSuccess,
                    LoggedAt  = DateTime.Now,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 통신 로그 저장 실패: {ex.Message}");
            }
        }

        // ── 4. 생산 실적 저장 ────────────────────────────────────
        public async Task SaveProductionRecordAsync(string pipeId, string projectId,
            string configId, string result, string? defectType,
            int? cycleTimeSec, double oee, string? operatorId = null)
        {
            if (!_isAvailable) return;
            try
            {
                await using var db = CreateContext();
                db.ProductionRecords.Add(new ProductionRecordEntity
                {
                    PipeId      = pipeId,
                    ProjectId   = projectId,
                    OperatorId  = operatorId,
                    ConfigId    = configId,
                    Result      = result,
                    DefectType  = defectType,
                    CycleTimeS  = cycleTimeSec,
                    OeeSnapshot = (decimal)oee,
                    CompletedAt = DateTime.Now,
                });
                await db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"[DB] 생산 실적 저장: {pipeId} → {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 생산 실적 저장 실패: {ex.Message}");
            }
        }

        // ── 5. 프로젝트 저장 ─────────────────────────────────────
        public async Task SaveProjectAsync(string projectId, string projectName,
            string fileType, DateTime addedAt)
        {
            if (!_isAvailable) return;
            try
            {
                await using var db = CreateContext();
                // 이미 존재하면 스킵
                if (await db.Projects.AnyAsync(p => p.ProjectId == projectId)) return;
                db.Projects.Add(new ProjectEntity
                {
                    ProjectId   = projectId,
                    ProjectName = projectName,
                    FileType    = fileType,
                    AddedAt     = addedAt,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 프로젝트 저장 실패: {ex.Message}");
            }
        }

        // ── 6. 배관 저장 (신규=true, 중복=false 반환) ────────────
        public async Task<bool> SavePipeAsync(string pipeId, string projectId, string projName,
            string material, int size, string status)
        {
            if (!_isAvailable) return false;
            try
            {
                await using var db = CreateContext();
                if (await db.Pipes.AnyAsync(p => p.PipeId == pipeId)) return false; // 중복
                db.Pipes.Add(new PipeEntity
                {
                    PipeId    = pipeId,
                    ProjectId = projectId,
                    ProjName  = projName,
                    Material  = material,
                    Size      = size,
                    Status    = status,
                });
                await db.SaveChangesAsync();
                return true; // 신규 저장
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 배관 저장 실패: {ex.Message}");
                return false;
            }
        }

        // ── 8. 배관 머신 처리 이력 저장 ──────────────────────────
        public async Task LogPipeMachineAsync(
            string pipeId, string projectId,
            string machineId, string? machineName,
            string? configId, string? operatorId,
            string result, int? cycleTimeSec)
        {
            if (!_isAvailable) return;
            try
            {
                await using var db = CreateContext();
                db.PipeMachineLogs.Add(new PipeMachineLogEntity
                {
                    PipeId      = pipeId,
                    ProjectId   = projectId,
                    MachineId   = machineId,
                    MachineName = machineName,
                    ConfigId    = configId,
                    OperatorId  = operatorId,
                    Result      = result,
                    CycleTimeS  = cycleTimeSec,
                    ProcessedAt = DateTime.Now,
                });
                await db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[DB] 머신 이력: {pipeId} @ {machineId} → {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 머신 이력 저장 실패: {ex.Message}");
            }
        }
        public async Task SyncMachineSettingsAsync(string machineId, string ip, int port)
        {
            if (!_isAvailable) return;
            try
            {
                await using var db = CreateContext();
                var m = await db.Machines.FindAsync(machineId);
                if (m != null)
                {
                    m.IpAddress = ip;
                    m.Port      = port;
                    m.UpdatedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 머신 설정 동기화 실패: {ex.Message}");
            }
        }

        // ── 9. 리포트용 집계 데이터 조회 ─────────────────────────
        public async Task<object> GetReportDataAsync(int year, int month)
        {
            if (!_isAvailable) return new { };
            try
            {
                await using var db = CreateContext();
                var now       = DateTime.Now;
                var weekStart = now.AddDays(-(now.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)now.DayOfWeek - 1));
                weekStart     = weekStart.Date;
                var weekEnd   = weekStart.AddDays(7);
                var monthStart= new DateTime(year, month, 1);
                var monthEnd  = monthStart.AddMonths(1);
                var yearStart = new DateTime(year, 1, 1);
                var yearEnd   = new DateTime(year + 1, 1, 1);

                // ── 전체 레코드 로드 (범위별) ──────────────────────
                var weekRecs  = await db.ProductionRecords.Where(r => r.CompletedAt >= weekStart  && r.CompletedAt < weekEnd  ).ToListAsync();
                var monthRecs = await db.ProductionRecords.Where(r => r.CompletedAt >= monthStart && r.CompletedAt < monthEnd ).ToListAsync();
                var yearRecs  = await db.ProductionRecords.Where(r => r.CompletedAt >= yearStart  && r.CompletedAt < yearEnd  ).ToListAsync();

                // ── 주간 일별 행 ───────────────────────────────────
                var weeklyRows = Enumerable.Range(0, 7).Select(i => {
                    var day = weekStart.AddDays(i);
                    var recs = weekRecs.Where(r => r.CompletedAt.Date == day.Date).ToList();
                    return new { actual = recs.Count, defect = recs.Count(r => r.Result == "불량") };
                }).Cast<object>().ToList();

                // ── 월간 주차별 행 ─────────────────────────────────
                var monthlyRows = Enumerable.Range(0, 4).Select(i => {
                    var ws = monthStart.AddDays(i * 7);
                    var we = ws.AddDays(7);
                    var recs = monthRecs.Where(r => r.CompletedAt >= ws && r.CompletedAt < we).ToList();
                    return new { actual = recs.Count, defect = recs.Count(r => r.Result == "불량") };
                }).Cast<object>().ToList();

                // ── 연간 월별 행 ───────────────────────────────────
                var annualRows = Enumerable.Range(1, 12).Select(mo => {
                    var ms = new DateTime(year, mo, 1);
                    var me = ms.AddMonths(1);
                    var recs = yearRecs.Where(r => r.CompletedAt >= ms && r.CompletedAt < me).ToList();
                    return new { actual = recs.Count, defect = recs.Count(r => r.Result == "불량") };
                }).Cast<object>().ToList();

                // ── OEE 평균 ──────────────────────────────────────
                double Oee(System.Collections.Generic.List<ProductionRecordEntity> recs)
                    => recs.Any(r => r.OeeSnapshot > 0)
                       ? Math.Round((double)recs.Where(r=>r.OeeSnapshot>0).Average(r=>(double)r.OeeSnapshot!), 1)
                       : 0.0;

                // ── 머신별 통계 (이번 달 pipe_machine_log) ─────────
                var machPairs = new[] {
                    ("LOADER","Auto Loader"),("CUTTING","Cutting M/C"),
                    ("LASER","Laser Marking"),("ROBOT","Moving Robot"),
                    ("BENDING","Bending M/C #1"),("BENDING2","Bending M/C #2")
                };
                var machines = new System.Collections.Generic.List<object>();
                foreach (var (mid, mname) in machPairs)
                {
                    var logs = await db.PipeMachineLogs
                        .Where(l => l.MachineId == mid && l.ProcessedAt >= monthStart && l.ProcessedAt < monthEnd)
                        .ToListAsync();
                    var total = logs.Count;
                    var good  = logs.Count(l => l.Result == "완료");
                    var oee   = total > 0 ? Math.Round((double)good / total * 100, 1) : 0.0;
                    machines.Add(new { name = mname, oee, uptime = oee > 0 ? Math.Min(99.9, oee + 2.5) : 0.0 });
                }

                return new {
                    weekly  = new { total = weekRecs.Count,  defect = weekRecs.Count(r=>r.Result=="불량"),  oee = Oee(weekRecs)  },
                    monthly = new { total = monthRecs.Count, defect = monthRecs.Count(r=>r.Result=="불량"), oee = Oee(monthRecs) },
                    annual  = new { total = yearRecs.Count,  defect = yearRecs.Count(r=>r.Result=="불량"),  oee = Oee(yearRecs)  },
                    weeklyRows, monthlyRows, annualRows, machines,
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB] 리포트 조회 실패: {ex.Message}");
                return new { };
            }
        }
    }
}
