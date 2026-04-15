using VirtualMachineSimulator.Models;
using VirtualMachineSimulator.Protocol;

namespace VirtualMachineSimulator.Simulator;

public sealed class MachineSimulator
{
    private readonly MachineEndpointSetting _setting;
    private readonly int _defaultDurationMs;
    private readonly object _sync = new();

    public MachineState State { get; }

    public MachineSimulator(MachineEndpointSetting setting, int defaultDurationMs)
    {
        _setting = setting;
        _defaultDurationMs = defaultDurationMs;
        State = new MachineState(setting.Id, setting.DisplayName, setting.Prefix, setting.Port);
    }

    public string HandleCommand(string line)
    {
        var cmd = ProtocolParsing.Parse(line);
        Tick();

        lock (_sync)
        {
            if (State.RunState == SimRunState.EmergencyStop)
            {
                return BuildError(cmd, "ESTOP_ACTIVE", "E-STOP active", "CRITICAL", true);
            }

            if (cmd.IsReadyQuery)
            {
                    return State.IsReady && State.RunState is SimRunState.Ready or SimRunState.Complete
                    ? BuildAck(cmd)
                    : BuildNotReady(cmd);
            }

            if (cmd.IsStatusQuery)
            {
                return BuildStatus();
            }

            if (cmd.IsLegacy)
            {
                return HandleLegacy(cmd);
            }

            return HandleStructured(cmd);
        }
    }

    public void Tick()
    {
        lock (_sync)
        {
            if (State.BusyUntilUtc.HasValue && DateTime.UtcNow >= State.BusyUntilUtc.Value)
            {
                State.BusyUntilUtc = null;
                State.RunState = SimRunState.Complete;
                State.IsReady = true;
                State.CompleteLatched = true;
                State.PermitGranted = false;
                State.MotionInProgress = false;
                State.TargetReached = true;
                State.InPosition = true;
            }
        }
    }

    public void SetAlarm(string errorCode)
    {
        lock (_sync)
        {
            State.RunState = SimRunState.Alarm;
            State.IsReady = false;
            State.ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "SIM_ALARM" : errorCode;
        }
    }

    public void ClearAlarm()
    {
        lock (_sync)
        {
            State.RunState = SimRunState.Ready;
            State.IsReady = true;
            State.ErrorCode = string.Empty;
            State.CompleteLatched = false;
            State.BusyUntilUtc = null;
            State.PermitGranted = false;
            State.MotionEnable = true;
            State.InterlockOk = true;
            State.SafeToMove = true;
            State.MotionInProgress = false;
            State.TargetReached = false;
            State.InPosition = true;
        }
    }

    public void SetReady(bool ready)
    {
        lock (_sync)
        {
            State.IsReady = ready;
            if (!ready && State.RunState == SimRunState.Ready)
            {
                State.RunState = SimRunState.Offline;
            }
            else if (ready && State.RunState == SimRunState.Offline)
            {
                State.RunState = SimRunState.Ready;
            }
        }
    }

    public void ForceComplete()
    {
        lock (_sync)
        {
            State.BusyUntilUtc = null;
            State.RunState = SimRunState.Complete;
            State.IsReady = true;
            State.CompleteLatched = true;
            State.PermitGranted = false;
        }
    }

    public string Describe()
    {
        lock (_sync)
        {
            return $"{State.MachineId,-8} Port={State.Port} Client={(State.IsConnectedClientPresent ? "Y" : "N")} " +
                   $"State={State.RunState,-12} Ready={(State.IsReady ? 1 : 0)} Alarm={State.ErrorCode,-12} " +
                   $"Cmd={State.LastCommandCode,-14} CID={State.LastCorrelationId,-12} " +
                   $"SafeMove={(State.SafeToMove ? 1 : 0)} Interlock={(State.InterlockOk ? 1 : 0)} Motion={(State.MotionInProgress ? 1 : 0)} Target={(State.TargetReached ? 1 : 0)}";
        }
    }

    private string HandleLegacy(ParsedCommand cmd)
    {
        return cmd.CommandCode switch
        {
            "START" => StartWork(cmd.CorrelationId, "START", string.Empty),
            "STOP" => StopWork(cmd.CorrelationId, "STOP"),
            "ESTOP" or "E_STOP" or "EMERGENCY_STOP" => EmergencyStop(cmd.CorrelationId, "E_STOP"),
            "RESET" => Reset(cmd.CorrelationId),
            _ => BuildError(cmd, "UNSUPPORTED_LEGACY_COMMAND")
        };
    }

    private string HandleStructured(ParsedCommand cmd)
    {
        var payloadJson = ProtocolParsing.TryDecodePayload(cmd.PayloadBase64) ?? string.Empty;
        return cmd.CommandCode switch
        {
            "LOADER_JOB" or "LOAD_REQUEST" or "PREFETCH_LOAD" or "BUFFER_PREPARE" or "JOB_LOAD" or
            "JOB_EXEC" or "CUTTING_JOB" or "MARKING_JOB" or "BENDING_JOB" or "ROBOT_TRANSFER" or
            "CUSTOM" => StartWork(cmd.CorrelationId, cmd.CommandCode, payloadJson),
            "ABORT" => StopWork(cmd.CorrelationId, "ABORT"),
            "E_STOP" or "ESTOP" => EmergencyStop(cmd.CorrelationId, "E_STOP"),
            _ => BuildError(cmd, "UNSUPPORTED_STRUCTURED_COMMAND")
        };
    }

    private string StartWork(string correlationId, string commandCode, string payloadJson)
    {
        if (State.RunState == SimRunState.Alarm)
        {
            return BuildError(correlationId, 0, commandCode, "ALARM_ACTIVE");
        }

        if (!State.IsReady || State.RunState == SimRunState.Busy)
        {
            return BuildError(correlationId, 0, commandCode, "NOT_READY");
        }

        State.LastCorrelationId = correlationId;
        State.LastSequenceNo = State.LastSequenceNo + 1;
        State.LastCommandCode = commandCode;
        State.LastPayloadJson = payloadJson;
        State.RunState = SimRunState.Busy;
        State.IsReady = false;
        State.PermitGranted = true;
        State.CompleteLatched = false;
        State.TargetReached = false;
        State.MotionInProgress = true;
        State.InPosition = false;
        State.BusyUntilUtc = DateTime.UtcNow.AddMilliseconds(_defaultDurationMs);
        return BuildAck(correlationId, State.LastSequenceNo, commandCode);
    }

    private string StopWork(string correlationId, string commandCode)
    {
        State.LastCorrelationId = correlationId;
        State.LastSequenceNo = State.LastSequenceNo + 1;
        State.LastCommandCode = commandCode;
        State.BusyUntilUtc = null;
        State.RunState = SimRunState.Ready;
        State.IsReady = true;
        State.PermitGranted = false;
        State.CompleteLatched = false;
        State.MotionInProgress = false;
        State.TargetReached = false;
        State.InPosition = true;
        State.ErrorCode = string.Empty;
        return BuildStopped(correlationId, State.LastSequenceNo, commandCode);
    }

    private string EmergencyStop(string correlationId, string commandCode)
    {
        State.LastCorrelationId = correlationId;
        State.LastSequenceNo = State.LastSequenceNo + 1;
        State.LastCommandCode = commandCode;
        State.RunState = SimRunState.EmergencyStop;
        State.IsReady = false;
        State.PermitGranted = false;
        State.MotionEnable = false;
        State.InterlockOk = false;
        State.SafeToMove = false;
        State.MotionInProgress = false;
        State.TargetReached = false;
        State.ErrorCode = "ESTOP";
        return BuildError(correlationId, State.LastSequenceNo, commandCode, "ESTOP_ACTIVE", "Emergency stop", "CRITICAL", true);
    }

    private string Reset(string correlationId)
    {
        ClearAlarm();
        State.LastCorrelationId = correlationId;
        State.LastSequenceNo = State.LastSequenceNo + 1;
        State.LastCommandCode = "RESET";
        State.MotionEnable = true;
        State.InterlockOk = true;
        State.SafeToMove = true;
        State.MotionInProgress = false;
        return BuildAck(correlationId, State.LastSequenceNo, "RESET");
    }

    private string BuildStatus()
    {
        if (State.RunState == SimRunState.Alarm)
        {
            return $"STATUS:ALARM;READY=0;ERROR_CODE={State.ErrorCode};SEV=HIGH;RESET_REQUIRED=1;{BuildStateFlags()}";
        }

        if (State.RunState == SimRunState.EmergencyStop)
        {
            return $"STATUS:EMERGENCY_STOP;READY=0;ERROR_CODE=ESTOP;SEV=CRITICAL;RESET_REQUIRED=1;{BuildStateFlags()}";
        }

        if (State.RunState == SimRunState.Busy)
        {
            return $"STATUS:WORKING;READY=0;CID={State.LastCorrelationId};SEQ={State.LastSequenceNo};CMD={State.LastCommandCode};{BuildStateFlags()}";
        }

        if (State.CompleteLatched)
        {
            State.CompleteLatched = false;
            State.RunState = SimRunState.Ready;
            State.MotionInProgress = false;
            State.TargetReached = true;
            State.InPosition = true;
            return $"STATUS:FINISH;READY=1;UNLOAD_COMPLETE;CID={State.LastCorrelationId};SEQ={State.LastSequenceNo};CMD={State.LastCommandCode};{BuildStateFlags()}";
        }

        return $"STATUS:READY;READY=1;CID={State.LastCorrelationId};SEQ={State.LastSequenceNo};CMD={State.LastCommandCode};{BuildStateFlags()}";
    }

    private string BuildAck(ParsedCommand cmd)
        => BuildAck(cmd.CorrelationId, cmd.SequenceNo, cmd.CommandCode);

    private string BuildAck(string correlationId, long sequenceNo, string commandCode)
        => string.IsNullOrWhiteSpace(correlationId)
            ? $"OK;SEQ={sequenceNo};CMD={commandCode};VER={State.ProtocolVersion};MPROF={State.MachineProfile};VPROF={State.VendorProfile}"
            : $"OK;CID={correlationId};SEQ={sequenceNo};CMD={commandCode};VER={State.ProtocolVersion};MPROF={State.MachineProfile};VPROF={State.VendorProfile}";

    private string BuildStopped(string correlationId, long sequenceNo, string commandCode)
        => string.IsNullOrWhiteSpace(correlationId)
            ? $"STOPPED;SEQ={sequenceNo};CMD={commandCode};READY=1;{BuildStateFlags()}"
            : $"STOPPED;CID={correlationId};SEQ={sequenceNo};CMD={commandCode};READY=1;{BuildStateFlags()}";

    private string BuildNotReady(ParsedCommand cmd)
        => $"NOT_READY;CID={cmd.CorrelationId};SEQ={cmd.SequenceNo};CMD={cmd.CommandCode};CODE=NOT_READY;{BuildStateFlags()}";

    private string BuildError(ParsedCommand cmd, string code, string message = "", string severity = "", bool resetRequired = false)
        => BuildError(cmd.CorrelationId, cmd.SequenceNo, cmd.CommandCode, code, message, severity, resetRequired);

    private string BuildError(string correlationId, long sequenceNo, string commandCode, string code, string message = "", string severity = "", bool resetRequired = false)
    {
        var msg = string.IsNullOrWhiteSpace(message) ? "" : $";MSG={message}";
        var sev = string.IsNullOrWhiteSpace(severity) ? "" : $";SEV={severity}";
        var reset = $";RESET_REQUIRED={(resetRequired ? 1 : 0)}";
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return $"ERROR;SEQ={sequenceNo};CMD={commandCode};CODE={code}{msg}{sev}{reset};VER={State.ProtocolVersion};MPROF={State.MachineProfile};VPROF={State.VendorProfile}";
        }
        return $"ERROR;CID={correlationId};SEQ={sequenceNo};CMD={commandCode};CODE={code}{msg}{sev}{reset};VER={State.ProtocolVersion};MPROF={State.MachineProfile};VPROF={State.VendorProfile}";
    }

    private string BuildStateFlags()
        => $"SAFE_TO_MOVE={(State.SafeToMove ? 1 : 0)};INTERLOCK_OK={(State.InterlockOk ? 1 : 0)};MOTION_ENABLE={(State.MotionEnable ? 1 : 0)};HOMED={(State.Homed ? 1 : 0)};IN_POSITION={(State.InPosition ? 1 : 0)};TARGET_REACHED={(State.TargetReached ? 1 : 0)};MOTION_IN_PROGRESS={(State.MotionInProgress ? 1 : 0)}";
}
