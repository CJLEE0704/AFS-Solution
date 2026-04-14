namespace VirtualMachineSimulator.Simulator;

public enum SimRunState
{
    Ready,
    Busy,
    Complete,
    Alarm,
    EmergencyStop,
    Offline
}

public sealed class MachineState
{
    public string MachineId { get; }
    public string DisplayName { get; }
    public string Prefix { get; }
    public int Port { get; }

    public bool IsReady { get; set; } = true;
    public bool IsConnectedClientPresent { get; set; }
    public bool PermitGranted { get; set; }
    public bool CompleteLatched { get; set; }
    public SimRunState RunState { get; set; } = SimRunState.Ready;
    public string ErrorCode { get; set; } = string.Empty;
    public string LastCorrelationId { get; set; } = string.Empty;
    public long LastSequenceNo { get; set; }
    public string LastCommandCode { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = "INT/1.1";
    public string MachineProfile { get; set; } = "GENERIC";
    public string VendorProfile { get; set; } = "SIM";
    public bool SafeToMove { get; set; } = true;
    public bool InterlockOk { get; set; } = true;
    public bool MotionEnable { get; set; } = true;
    public bool Homed { get; set; } = true;
    public bool InPosition { get; set; } = true;
    public bool TargetReached { get; set; } = false;
    public bool MotionInProgress { get; set; } = false;
    public string LastPayloadJson { get; set; } = string.Empty;
    public DateTime? BusyUntilUtc { get; set; }

    public MachineState(string machineId, string displayName, string prefix, int port)
    {
        MachineId = machineId;
        DisplayName = displayName;
        Prefix = prefix;
        Port = port;
    }
}
