using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PipeBendingDashboard.Communication
{
    public enum MachineCommandType
    {
        Start,
        Stop,
        Status,
        Reset,
        Ready,
        LoaderJob,
        LoadRequest,
        PrefetchLoad,
        BufferPrepare,
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
        Ready,
        PermitGranted,
        InProgress,
        Completed,
        Alarm,
        Fault,
        EmergencyStop,
        State,
        Error,
        Unknown
    }

    public sealed class MachineCommandPayload
    {
        public string Raw { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────
    // AFP 단계별 내부 Payload 모델 (벤더 중립)
    // ─────────────────────────────────────────────────────────────
    public sealed class CuttingJobPayload
    {
        public string JobId { get; set; } = "";
        public string PipeId { get; set; } = "";
        public string MaterialCode { get; set; } = "";
        public double LengthMm { get; set; }
        public int Quantity { get; set; } = 1;
        public string ProgramId { get; set; } = "";
    }

    public sealed class LoaderJobPayload
    {
        public string JobId { get; set; } = "";
        public string PipeId { get; set; } = "";
        public string SourceRack { get; set; } = "";
        public string SourceSlot { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public string TargetStage { get; set; } = "";
        public bool Prefetch { get; set; }
        public bool BufferPrepare { get; set; }
        public string SequenceNo { get; set; } = "";
    }

    public sealed class MarkingJobPayload
    {
        public string JobId { get; set; } = "";
        public string PipeId { get; set; } = "";
        public string MarkText { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string TemplateId { get; set; } = "";
    }

    public sealed class RobotTransferPayload
    {
        public string JobId { get; set; } = "";
        public string PipeId { get; set; } = "";
        public string FromStage { get; set; } = "";
        public string ToStage { get; set; } = "";
        public string GripProfile { get; set; } = "";
    }

    public sealed class BendingInstruction
    {
        public int Seq { get; set; }
        public double AngleDeg { get; set; }
        public double RadiusMm { get; set; }
        public double FeedMm { get; set; }
    }

    public sealed class BendingJobPayload
    {
        public string JobId { get; set; } = "";
        public string PipeId { get; set; } = "";
        public string ProgramId { get; set; } = "";
        public List<BendingInstruction> Steps { get; set; } = new();
    }

    public static class MachinePayloadFactory
    {
        private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static MachineCommandPayload FromCutting(CuttingJobPayload payload) => FromModel(payload);
        public static MachineCommandPayload FromLoader(LoaderJobPayload payload) => FromModel(payload);
        public static MachineCommandPayload FromMarking(MarkingJobPayload payload) => FromModel(payload);
        public static MachineCommandPayload FromRobotTransfer(RobotTransferPayload payload) => FromModel(payload);
        public static MachineCommandPayload FromBending(BendingJobPayload payload) => FromModel(payload);

        private static MachineCommandPayload FromModel<T>(T payload)
        {
            var raw = JsonSerializer.Serialize(payload, _jsonOpt);
            var result = new MachineCommandPayload { Raw = raw };
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    result.Fields[p.Name] = p.Value.ToString();
                }
            }
            return result;
        }
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
