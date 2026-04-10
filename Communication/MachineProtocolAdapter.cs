using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace PipeBendingDashboard.Communication
{
    public interface IMachineProtocolAdapter
    {
        string MachineId { get; }
        ProtocolProfile Profile { get; }
        string? GetReadyCommand();
        string GetStatusCommand();
        string? ResolveCommand(string commandType);
        string? EncodeRequest(MachineCommandRequest request);
        MachineCommandResponse DecodeResponse(MachineCommandRequest request, string response);
        ParsedMachineStatus ParseStatus(string response);
        MachineRunState ParseRunState(string response);
    }

    public enum MachineRunState
    {
        READY,
        BUSY,
        DONE,
        FAULT,
        HOLD
    }

    public enum ProtocolProfile
    {
        Internal,
        VendorA,
        VendorB
    }

    public sealed class DefaultMachineProtocolAdapter : IMachineProtocolAdapter
    {
        private readonly Dictionary<string, string> _commands;
        private readonly string _wirePrefix;
        public string MachineId { get; }
        public ProtocolProfile Profile { get; }

        public DefaultMachineProtocolAdapter(string machineId, Dictionary<string, string> commands, ProtocolProfile profile = ProtocolProfile.Internal)
        {
            MachineId = machineId;
            _commands = commands;
            Profile = profile;
            _wirePrefix = commands.TryGetValue("STATUS", out var st) && st.Contains(':')
                ? st[..st.IndexOf(':')]
                : machineId;
        }

        public string? GetReadyCommand() => _commands.TryGetValue("READY", out var cmd) ? cmd : null;
        public string GetStatusCommand() => _commands.TryGetValue("STATUS", out var cmd) ? cmd : string.Empty;
        public string? ResolveCommand(string commandType) => _commands.TryGetValue(commandType.ToUpperInvariant(), out var cmd) ? cmd : null;
        public ParsedMachineStatus ParseStatus(string response) => MachineProtocol.ParseStatusResponse(response);
        public string? EncodeRequest(MachineCommandRequest request)
        {
            var key = request.CommandType.ToString().ToUpperInvariant();
            var legacy = ResolveCommand(key);
            if (!string.IsNullOrWhiteSpace(legacy)) return legacy;

            return request.CommandType switch
            {
                MachineCommandType.LoaderJob    => BuildInternalFrame("LOADER_JOB", request),
                MachineCommandType.LoadRequest  => BuildInternalFrame("LOAD_REQUEST", request),
                MachineCommandType.PrefetchLoad => BuildInternalFrame("PREFETCH_LOAD", request),
                MachineCommandType.BufferPrepare=> BuildInternalFrame("BUFFER_PREPARE", request),
                MachineCommandType.LoadJob      => BuildInternalFrame("JOB_LOAD", request),
                MachineCommandType.ExecuteJob   => BuildInternalFrame("JOB_EXEC", request),
                MachineCommandType.CuttingJob   => BuildInternalFrame("CUTTING_JOB", request),
                MachineCommandType.MarkingJob   => BuildInternalFrame("MARKING_JOB", request),
                MachineCommandType.BendingJob   => BuildInternalFrame("BENDING_JOB", request),
                MachineCommandType.MoveTransfer => BuildInternalFrame("ROBOT_TRANSFER", request),
                MachineCommandType.Abort        => BuildInternalFrame("ABORT", request),
                MachineCommandType.Custom       => BuildInternalFrame("CUSTOM", request),
                _ => null
            };
        }

        public MachineCommandResponse DecodeResponse(MachineCommandRequest request, string response)
            => MachineProtocol.ParseCommandResponse(MachineId, request.CommandType, response, request.CorrelationId);

        private string? BuildInternalFrame(string commandCode, MachineCommandRequest request)
        {
            var payloadRaw = request.Payload?.Raw?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(payloadRaw) && request.Payload?.Fields?.Count > 0)
            {
                payloadRaw = JsonSerializer.Serialize(request.Payload.Fields);
            }

            // 내부 표준 프레임 (벤더 중립, 사전 검증/시뮬 용도)
            // <PREFIX>:CMD=<CODE>;CID=<ID>;TS=<UTC>;PAYLOAD=<BASE64(JSON)>
            var payloadB64 = string.IsNullOrWhiteSpace(payloadRaw)
                ? ""
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadRaw));
            var frame = $"{_wirePrefix}:CMD={commandCode};CID={request.CorrelationId};TS={request.RequestedAtUtc:O}";
            if (!string.IsNullOrWhiteSpace(payloadB64))
            {
                frame += $";PAYLOAD={payloadB64}";
            }
            return frame + "\r\n";
        }

        public MachineRunState ParseRunState(string response)
        {
            var parsed = ParseStatus(response);
            return parsed.Status switch
            {
                "ALARM" => MachineRunState.FAULT,
                "WORKING" => MachineRunState.BUSY,
                "FINISH" => MachineRunState.DONE,
                _ => MachineRunState.READY,
            };
        }
    }

    public static class MachineProtocolRegistry
    {
        public static IReadOnlyDictionary<string, IMachineProtocolAdapter> BuildDefaults(ProtocolProfile profile = ProtocolProfile.Internal)
            => new Dictionary<string, IMachineProtocolAdapter>(StringComparer.OrdinalIgnoreCase)
            {
                ["LOADER"] = new DefaultMachineProtocolAdapter("LOADER", new Dictionary<string, string>{{"READY", MachineProtocol.Loader.Ready},{"START",MachineProtocol.Loader.Start},{"STOP",MachineProtocol.Loader.Stop},{"RESET",MachineProtocol.Loader.Reset},{"STATUS",MachineProtocol.Loader.Status}}, profile),
                ["CUTTING"] = new DefaultMachineProtocolAdapter("CUTTING", new Dictionary<string, string>{{"READY", MachineProtocol.Cutting.Ready},{"START",MachineProtocol.Cutting.Start},{"STOP",MachineProtocol.Cutting.Stop},{"RESET",MachineProtocol.Cutting.Reset},{"STATUS",MachineProtocol.Cutting.Status}}, profile),
                ["LASER"] = new DefaultMachineProtocolAdapter("LASER", new Dictionary<string, string>{{"READY", MachineProtocol.Laser.Ready},{"START",MachineProtocol.Laser.Start},{"STOP",MachineProtocol.Laser.Stop},{"RESET",MachineProtocol.Laser.Reset},{"STATUS",MachineProtocol.Laser.Status}}, profile),
                ["ROBOT"] = new DefaultMachineProtocolAdapter("ROBOT", new Dictionary<string, string>{{"READY", MachineProtocol.Robot.Ready},{"START",MachineProtocol.Robot.Start},{"STOP",MachineProtocol.Robot.Stop},{"RESET",MachineProtocol.Robot.Reset},{"STATUS",MachineProtocol.Robot.Status}}, profile),
                ["BENDING"] = new DefaultMachineProtocolAdapter("BENDING", new Dictionary<string, string>{{"READY", MachineProtocol.Bending.Ready},{"START",MachineProtocol.Bending.Start},{"STOP",MachineProtocol.Bending.Stop},{"RESET",MachineProtocol.Bending.Reset},{"STATUS",MachineProtocol.Bending.Status}}, profile),
                ["BENDING2"] = new DefaultMachineProtocolAdapter("BENDING2", new Dictionary<string, string>{{"READY", MachineProtocol.Bending2.Ready},{"START",MachineProtocol.Bending2.Start},{"STOP",MachineProtocol.Bending2.Stop},{"RESET",MachineProtocol.Bending2.Reset},{"STATUS",MachineProtocol.Bending2.Status}}, profile),
            };
    }
}
