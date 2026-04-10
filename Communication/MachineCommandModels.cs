using System;
using System.Collections.Generic;

namespace PipeBendingDashboard.Communication
{
    public enum MachineCommandType
    {
        Start,
        Stop,
        Status,
        Reset,
        Ready,
        LoadJob,
        ExecuteJob,
        CuttingJob,
        MarkingJob,
        BendingJob,
        MoveTransfer,
        Abort,
        Custom
    }

    public enum MachineResponseType
    {
        Ack,
        Rejected,
        InProgress,
        Completed,
        Alarm,
        State,
        Error,
        Unknown
    }

    public sealed class MachineCommandPayload
    {
        public string Raw { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class MachineCommandRequest
    {
        public string TargetMachineId { get; set; } = "";
        public MachineCommandType CommandType { get; set; } = MachineCommandType.Status;
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
        public MachineCommandPayload Payload { get; set; } = new();
    }

    public sealed class MachineCommandResponse
    {
        public string MachineId { get; set; } = "";
        public MachineCommandType CommandType { get; set; } = MachineCommandType.Status;
        public MachineResponseType ResponseType { get; set; } = MachineResponseType.Unknown;
        public bool IsSuccess { get; set; }
        public string RawResponse { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
