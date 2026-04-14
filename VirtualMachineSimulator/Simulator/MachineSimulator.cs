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
                return BuildError(cmd.CorrelationId, "ESTOP_ACTIVE");
            }

            if (cmd.IsReadyQuery)
            {
                return State.IsReady && State.RunState is SimRunState.Ready or SimRunState.Complete
                    ? "OK"
                    : "NOT_READY";
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
            return $"{State.MachineId,-8} Port={State.Port} State={State.RunState,-12} Ready={State.IsReady,-5} Alarm={State.ErrorCode,-12} LastCmd={State.LastCommandCode,-16} CID={State.LastCorrelationId}";
        }
    }

    private string HandleLegacy(ParsedCommand cmd)
    {
        return cmd.CommandCode switch
        {
            "START" => StartWork(cmd.CorrelationId, "START", string.Empty),
            "STOP" => StopWork(cmd.CorrelationId, "STOP"),
            "RESET" => Reset(cmd.CorrelationId),
            _ => BuildError(cmd.CorrelationId, "UNSUPPORTED_LEGACY_COMMAND")
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
            _ => BuildError(cmd.CorrelationId, "UNSUPPORTED_STRUCTURED_COMMAND")
        };
    }

    private string StartWork(string correlationId, string commandCode, string payloadJson)
    {
        if (State.RunState == SimRunState.Alarm)
        {
            return BuildError(correlationId, "ALARM_ACTIVE");
        }

        if (!State.IsReady || State.RunState == SimRunState.Busy)
        {
            return BuildError(correlationId, "NOT_READY");
        }

        State.LastCorrelationId = correlationId;
        State.LastCommandCode = commandCode;
        State.LastPayloadJson = payloadJson;
        State.RunState = SimRunState.Busy;
        State.IsReady = false;
        State.PermitGranted = true;
        State.CompleteLatched = false;
        State.BusyUntilUtc = DateTime.UtcNow.AddMilliseconds(_defaultDurationMs);
        return BuildOk(correlationId);
    }

    private string StopWork(string correlationId, string commandCode)
    {
        State.LastCorrelationId = correlationId;
        State.LastCommandCode = commandCode;
        State.BusyUntilUtc = null;
        State.RunState = SimRunState.Ready;
        State.IsReady = true;
        State.PermitGranted = false;
        State.CompleteLatched = false;
        State.ErrorCode = string.Empty;
        return BuildOk(correlationId);
    }

    private string Reset(string correlationId)
    {
        ClearAlarm();
        State.LastCorrelationId = correlationId;
        State.LastCommandCode = "RESET";
        return BuildOk(correlationId);
    }

    private string BuildStatus()
    {
        if (State.RunState == SimRunState.Alarm)
        {
            return $"STATUS:ALARM;READY=0;ERROR_CODE={State.ErrorCode}";
        }

        if (State.RunState == SimRunState.EmergencyStop)
        {
            return "STATUS:EMERGENCY_STOP;READY=0;ERROR_CODE=ESTOP";
        }

        if (State.RunState == SimRunState.Busy)
        {
            return $"STATUS:WORKING;READY=0;CID={State.LastCorrelationId};CMD={State.LastCommandCode}";
        }

        if (State.CompleteLatched)
        {
            State.CompleteLatched = false;
            State.RunState = SimRunState.Ready;
            return $"STATUS:FINISH;READY=1;UNLOAD_COMPLETE;CID={State.LastCorrelationId};CMD={State.LastCommandCode}";
        }

        return "STATUS:READY;READY=1";
    }

    private static string BuildOk(string correlationId)
        => string.IsNullOrWhiteSpace(correlationId) ? "OK" : $"OK;CID={correlationId}";

    private static string BuildError(string correlationId, string code)
        => string.IsNullOrWhiteSpace(correlationId)
            ? $"ERROR;CODE={code}"
            : $"ERROR;CID={correlationId};CODE={code}";
}
