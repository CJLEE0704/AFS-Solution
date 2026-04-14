using System;

namespace PipeBendingDashboard.Communication
{
    public sealed class ParsedMachineStatus
    {
        public string Status { get; set; } = "READY";
        public bool IsUnloadComplete { get; set; } = false;
        public bool? IsReady { get; set; } = null;
        public string ErrorCode { get; set; } = "";
        public bool SafeToMove { get; set; }
        public bool InterlockOk { get; set; }
        public bool MotionEnable { get; set; }
        public bool Homed { get; set; }
        public bool InPosition { get; set; }
        public bool TargetReached { get; set; }
        public bool MotionInProgress { get; set; }
    }

    /// <summary>
    /// 장비별 TCP 명령어 정의
    /// ※ 실제 장비 매뉴얼 확인 후 명령어 수정 필요
    /// ── 협의 완료 프로토콜 ──────────────────────────────────────
    /// Ready 확인: 로그인 후 TCP 연결 성공 시 자동 전송
    ///   전송: LOADER:READY?\r\n
    ///   응답: OK\r\n (정상) | NOT_READY\r\n (준비 안 됨)
    /// </summary>
    public static class MachineProtocol
    {
        // ── Auto Loader (192.168.1.10) ────────────────────────────
        public static class Loader
        {
            public const string Ready    = "LOADER:READY?\r\n";   // ← 신규: 연결 후 자동 전송
            public const string Start    = "LOADER:START\r\n";
            public const string Stop     = "LOADER:STOP\r\n";
            public const string Status   = "LOADER:STATUS?\r\n";
            public const string Reset    = "LOADER:RESET\r\n";
            public const string GetCount = "LOADER:COUNT?\r\n";
        }

        // ── Cutting M/C (192.168.1.11) ───────────────────────────
        public static class Cutting
        {
            public const string Ready    = "CUT:READY?\r\n";      // ← 신규
            public const string Start    = "CUT:START\r\n";
            public const string Stop     = "CUT:STOP\r\n";
            public const string Status   = "CUT:STATUS?\r\n";
            public const string Reset    = "CUT:RESET\r\n";
            public const string GetSpeed = "CUT:SPEED?\r\n";
        }

        // ── Laser Marking (192.168.1.12) ─────────────────────────
        public static class Laser
        {
            public const string Ready    = "LASER:READY?\r\n";    // ← 신규
            public const string Start    = "LASER:START\r\n";
            public const string Stop     = "LASER:STOP\r\n";
            public const string Status   = "LASER:STATUS?\r\n";
            public const string Reset    = "LASER:RESET\r\n";
            public const string GetPower = "LASER:POWER?\r\n";
        }

        // ── Bending M/C (192.168.1.13) ───────────────────────────
        public static class Bending
        {
            public const string Ready    = "BEND:READY?\r\n";     // ← 신규
            public const string Start    = "BEND:START\r\n";
            public const string Stop     = "BEND:STOP\r\n";
            public const string Status   = "BEND:STATUS?\r\n";
            public const string Reset    = "BEND:RESET\r\n";
            public const string GetAngle = "BEND:ANGLE?\r\n";
        }

        // ── Moving Robot (192.168.1.14) ──────────────────────────
        public static class Robot
        {
            public const string Ready    = "ROBOT:READY?\r\n";
            public const string Start    = "ROBOT:START\r\n";
            public const string Stop     = "ROBOT:STOP\r\n";
            public const string Status   = "ROBOT:STATUS?\r\n";
            public const string Reset    = "ROBOT:RESET\r\n";
        }

        // ── Bending M/C #2 (192.168.1.15) ───────────────────────
        public static class Bending2
        {
            public const string Ready    = "BEND2:READY?\r\n";
            public const string Start    = "BEND2:START\r\n";
            public const string Stop     = "BEND2:STOP\r\n";
            public const string Status   = "BEND2:STATUS?\r\n";
            public const string Reset    = "BEND2:RESET\r\n";
        }

        /// <summary>
        /// 장비 ID로 Ready 명령어 반환
        /// </summary>
        public static string? GetReady(string machineId) =>
            machineId.ToUpper() switch
            {
                "LOADER"  => Loader.Ready,
                "CUTTING" => Cutting.Ready,
                "LASER"   => Laser.Ready,
                "BENDING" => Bending.Ready,
                "ROBOT"   => Robot.Ready,
                "BENDING2"=> Bending2.Ready,
                _ => null
            };

        public static ParsedMachineStatus ParseStatusResponse(string response)
        {
            var parsed = new ParsedMachineStatus();
            if (string.IsNullOrWhiteSpace(response)) return parsed;

            var upper = response.Trim().ToUpperInvariant();
            if (upper.Contains("UNLOAD_COMPLETE"))
            {
                parsed.IsUnloadComplete = true;
                parsed.Status = "FINISH";
            }
            else if (upper.Contains("ALARM") || upper.Contains("ERROR"))
            {
                parsed.Status = "ALARM";
            }
            else if (upper.Contains("WORKING") || upper.Contains("RUNNING"))
            {
                parsed.Status = "WORKING";
            }
            else if (upper.Contains("FINISH"))
            {
                parsed.Status = "FINISH";
            }
            else if (upper.Contains("READY") || upper.Contains("IDLE"))
            {
                parsed.Status = "READY";
            }

            if (upper.Contains("READY:1") || upper.Contains("READY=1") || upper.Contains("READY:TRUE")) parsed.IsReady = true;
            if (upper.Contains("READY:0") || upper.Contains("READY=0") || upper.Contains("READY:FALSE") || upper.Contains("NOT_READY")) parsed.IsReady = false;

            parsed.SafeToMove = upper.Contains("SAFE_TO_MOVE=1") || upper.Contains("SAFE_TO_MOVE:1");
            parsed.InterlockOk = upper.Contains("INTERLOCK_OK=1") || upper.Contains("INTERLOCK_OK:1");
            parsed.MotionEnable = upper.Contains("MOTION_ENABLE=1") || upper.Contains("MOTION_ENABLE:1");
            parsed.Homed = upper.Contains("HOMED=1") || upper.Contains("HOMED:1");
            parsed.InPosition = upper.Contains("IN_POSITION=1") || upper.Contains("IN_POSITION:1");
            parsed.TargetReached = upper.Contains("TARGET_REACHED=1") || upper.Contains("TARGET_REACHED:1");
            parsed.MotionInProgress = upper.Contains("MOTION_IN_PROGRESS=1") || upper.Contains("MOTION_IN_PROGRESS:1");

            var errorIdx = upper.IndexOf("ERROR_CODE", System.StringComparison.Ordinal);
            if (errorIdx < 0) errorIdx = upper.IndexOf("ERR", System.StringComparison.Ordinal);
            if (errorIdx >= 0)
            {
                var slice = response[errorIdx..];
                var sep = slice.IndexOfAny(new[] { ':', '=' });
                if (sep >= 0)
                {
                    var value = slice[(sep + 1)..].Trim();
                    var end = value.IndexOfAny(new[] { ',', ' ', '\r', '\n', ';' });
                    parsed.ErrorCode = (end >= 0 ? value[..end] : value).Trim();
                }
            }

            return parsed;
        }

        public static MachineCommandResponse ParseCommandResponse(
            string machineId,
            MachineCommandType commandType,
            string response,
            string correlationId = "")
        {
            var raw = response?.Trim() ?? "";
            var upper = raw.ToUpperInvariant();

            var result = new MachineCommandResponse
            {
                MachineId = machineId,
                CommandType = commandType,
                CommandCode = commandType.ToString().ToUpperInvariant(),
                RawResponse = raw,
                CorrelationId = correlationId,
                ReceivedAtUtc = DateTime.UtcNow,
                ResponseType = MachineResponseType.Unknown,
                IsSuccess = false
            };

            result.SequenceNo = TryExtractLong(raw, "SEQ");
            result.ProtocolVersion = TryExtractToken(raw, "VER");
            if (string.IsNullOrWhiteSpace(result.ProtocolVersion)) result.ProtocolVersion = "INT/1.1";
            result.MachineProfile = TryExtractToken(raw, "MPROF");
            if (string.IsNullOrWhiteSpace(result.MachineProfile)) result.MachineProfile = "GENERIC";
            result.VendorProfile = TryExtractToken(raw, "VPROF");
            if (string.IsNullOrWhiteSpace(result.VendorProfile)) result.VendorProfile = "INTERNAL";
            result.JobId = TryExtractToken(raw, "JOB");
            result.PipeId = TryExtractToken(raw, "PIPE");
            result.Stage = TryExtractToken(raw, "STAGE");
            result.TargetMachine = TryExtractToken(raw, "TARGET");
            result.MotionSafety.SafeToMove = IsTrueToken(TryExtractToken(raw, "SAFE_TO_MOVE"));
            result.MotionSafety.InterlockOk = IsTrueToken(TryExtractToken(raw, "INTERLOCK_OK"));
            result.MotionSafety.MotionEnable = IsTrueToken(TryExtractToken(raw, "MOTION_ENABLE"));
            result.MotionSafety.Homed = IsTrueToken(TryExtractToken(raw, "HOMED"));
            result.MotionSafety.InPosition = IsTrueToken(TryExtractToken(raw, "IN_POSITION"));
            result.MotionSafety.TargetReached = IsTrueToken(TryExtractToken(raw, "TARGET_REACHED"));
            result.MotionSafety.MotionInProgress = IsTrueToken(TryExtractToken(raw, "MOTION_IN_PROGRESS"));

            if (string.IsNullOrWhiteSpace(raw))
            {
                result.ResponseType = MachineResponseType.Error;
                result.ErrorCode = "NO_RESPONSE";
                return result;
            }

            if (upper.StartsWith("OK"))
            {
                result.ResponseType = MachineResponseType.Ack;
                result.IsSuccess = true;
                var cid = TryExtractToken(raw, "CID");
                if (!string.IsNullOrWhiteSpace(cid)) result.CorrelationId = cid;
                result.CommandCode = string.IsNullOrWhiteSpace(TryExtractToken(raw, "CMD"))
                    ? result.CommandCode
                    : TryExtractToken(raw, "CMD");
                return result;
            }

            if (upper.StartsWith("ERROR") || upper.StartsWith("FAIL") || upper.StartsWith("NACK"))
            {
                result.ResponseType = MachineResponseType.Rejected;
                result.ErrorCode = TryExtractToken(raw, "CODE");
                if (string.IsNullOrWhiteSpace(result.ErrorCode)) result.ErrorCode = "COMMAND_REJECTED";
                result.AlarmCode = result.ErrorCode;
                result.AlarmMessage = TryExtractToken(raw, "MSG");
                result.Severity = TryExtractToken(raw, "SEV");
                result.ResetRequired = IsTrueToken(TryExtractToken(raw, "RESET_REQUIRED"));
                return result;
            }

            if (upper.Contains("NOT_READY"))
            {
                result.ResponseType = MachineResponseType.NotReady;
                result.ErrorCode = "NOT_READY";
                return result;
            }

            if (upper.Contains("STOPPED") || upper.Contains("IDLE_STOP"))
            {
                result.ResponseType = MachineResponseType.Stopped;
                result.IsSuccess = true;
                return result;
            }

            if (upper.Contains("OFFLINE") || upper.Contains("DISCONNECTED"))
            {
                result.ResponseType = MachineResponseType.Offline;
                result.ErrorCode = "OFFLINE";
                return result;
            }

            if (upper.Contains("RUNNING") || upper.Contains("WORKING") || upper.Contains("IN_PROGRESS"))
            {
                result.ResponseType = MachineResponseType.InProgress;
                result.IsSuccess = true;
                result.MotionSafety.MotionInProgress = true;
                return result;
            }

            if (upper.Contains("PERMIT") || upper.Contains("ARMED"))
            {
                result.ResponseType = MachineResponseType.PermitGranted;
                result.IsSuccess = true;
                return result;
            }

            if (upper.Contains("ESTOP") || upper.Contains("EMERGENCY_STOP") || upper.Contains("MOTION_INHIBIT"))
            {
                result.ResponseType = MachineResponseType.EmergencyStop;
                result.ErrorCode = "EMERGENCY_STOP";
                result.AlarmCode = "EMERGENCY_STOP";
                result.Severity = "CRITICAL";
                result.ResetRequired = true;
                return result;
            }

            if (upper.Contains("FINISH") || upper.Contains("COMPLETE") || upper.Contains("DONE"))
            {
                result.ResponseType = upper.Contains("COMPLETE") ? MachineResponseType.Complete : MachineResponseType.Completed;
                result.IsSuccess = true;
                result.MotionSafety.TargetReached = true;
                return result;
            }

            if (upper.Contains("ALARM") || upper.Contains("FAULT"))
            {
                result.ResponseType = upper.Contains("FAULT") ? MachineResponseType.Fault : MachineResponseType.Alarm;
                result.ErrorCode = TryExtractToken(raw, "ERROR_CODE");
                if (string.IsNullOrWhiteSpace(result.ErrorCode)) result.ErrorCode = upper.Contains("FAULT") ? "FAULT" : "ALARM";
                result.AlarmCode = result.ErrorCode;
                result.AlarmMessage = TryExtractToken(raw, "MSG");
                result.Severity = TryExtractToken(raw, "SEV");
                result.ResetRequired = IsTrueToken(TryExtractToken(raw, "RESET_REQUIRED"));
                return result;
            }

            if (upper.Contains("STATUS") || upper.Contains("READY") || upper.Contains("IDLE") || upper.Contains("BUSY"))
            {
                result.ResponseType = upper.Contains("READY") || upper.Contains("IDLE")
                    ? MachineResponseType.Ready
                    : MachineResponseType.State;
                result.IsSuccess = true;
                return result;
            }

            return result;
        }

        private static string TryExtractToken(string raw, string key)
        {
            var idx = raw.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            var start = idx + key.Length + 1;
            if (start >= raw.Length) return "";
            var remain = raw[start..];
            var end = remain.IndexOfAny(new[] { ';', ',', ' ', '\r', '\n' });
            return (end < 0 ? remain : remain[..end]).Trim();
        }

        private static long TryExtractLong(string raw, string key)
        {
            var token = TryExtractToken(raw, key);
            return long.TryParse(token, out var n) ? n : 0;
        }

        private static bool IsTrueToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var upper = token.Trim().ToUpperInvariant();
            return upper == "1" || upper == "TRUE" || upper == "Y" || upper == "YES";
        }
    }
}
