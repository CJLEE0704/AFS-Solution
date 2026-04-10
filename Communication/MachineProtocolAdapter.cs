using System;
using System.Collections.Generic;

namespace PipeBendingDashboard.Communication
{
    public interface IMachineProtocolAdapter
    {
        string MachineId { get; }
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

    public sealed class DefaultMachineProtocolAdapter : IMachineProtocolAdapter
    {
        private readonly Dictionary<string, string> _commands;
        private readonly string _wirePrefix;
        public string MachineId { get; }

        public DefaultMachineProtocolAdapter(string machineId, Dictionary<string, string> commands)
        {
            MachineId = machineId;
            _commands = commands;
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

            // 확장 명령: 내부 표준 모델 -> 기본 TCP 텍스트 프레임
            var payload = request.Payload?.Raw?.Trim() ?? "";
            var suffix = string.IsNullOrWhiteSpace(payload) ? "" : $" {payload}";

            return request.CommandType switch
            {
                MachineCommandType.LoadJob      => $"{_wirePrefix}:JOB_LOAD{suffix}\r\n",
                MachineCommandType.ExecuteJob   => $"{_wirePrefix}:JOB_EXEC{suffix}\r\n",
                MachineCommandType.CuttingJob   => $"{_wirePrefix}:CUT_JOB{suffix}\r\n",
                MachineCommandType.MarkingJob   => $"{_wirePrefix}:MARK_JOB{suffix}\r\n",
                MachineCommandType.BendingJob   => $"{_wirePrefix}:BEND_JOB{suffix}\r\n",
                MachineCommandType.MoveTransfer => $"{_wirePrefix}:MOVE{suffix}\r\n",
                MachineCommandType.Abort        => $"{_wirePrefix}:ABORT\r\n",
                MachineCommandType.Custom       => string.IsNullOrWhiteSpace(payload) ? null : $"{_wirePrefix}:{payload}\r\n",
                _ => null
            };
        }

        public MachineCommandResponse DecodeResponse(MachineCommandRequest request, string response)
            => MachineProtocol.ParseCommandResponse(MachineId, request.CommandType, response, request.CorrelationId);

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
        public static IReadOnlyDictionary<string, IMachineProtocolAdapter> BuildDefaults()
            => new Dictionary<string, IMachineProtocolAdapter>(StringComparer.OrdinalIgnoreCase)
            {
                ["LOADER"] = new DefaultMachineProtocolAdapter("LOADER", new Dictionary<string, string>{{"READY", MachineProtocol.Loader.Ready},{"START",MachineProtocol.Loader.Start},{"STOP",MachineProtocol.Loader.Stop},{"RESET",MachineProtocol.Loader.Reset},{"STATUS",MachineProtocol.Loader.Status}}),
                ["CUTTING"] = new DefaultMachineProtocolAdapter("CUTTING", new Dictionary<string, string>{{"READY", MachineProtocol.Cutting.Ready},{"START",MachineProtocol.Cutting.Start},{"STOP",MachineProtocol.Cutting.Stop},{"RESET",MachineProtocol.Cutting.Reset},{"STATUS",MachineProtocol.Cutting.Status}}),
                ["LASER"] = new DefaultMachineProtocolAdapter("LASER", new Dictionary<string, string>{{"READY", MachineProtocol.Laser.Ready},{"START",MachineProtocol.Laser.Start},{"STOP",MachineProtocol.Laser.Stop},{"RESET",MachineProtocol.Laser.Reset},{"STATUS",MachineProtocol.Laser.Status}}),
                ["ROBOT"] = new DefaultMachineProtocolAdapter("ROBOT", new Dictionary<string, string>{{"READY", MachineProtocol.Robot.Ready},{"START",MachineProtocol.Robot.Start},{"STOP",MachineProtocol.Robot.Stop},{"RESET",MachineProtocol.Robot.Reset},{"STATUS",MachineProtocol.Robot.Status}}),
                ["BENDING"] = new DefaultMachineProtocolAdapter("BENDING", new Dictionary<string, string>{{"READY", MachineProtocol.Bending.Ready},{"START",MachineProtocol.Bending.Start},{"STOP",MachineProtocol.Bending.Stop},{"RESET",MachineProtocol.Bending.Reset},{"STATUS",MachineProtocol.Bending.Status}}),
                ["BENDING2"] = new DefaultMachineProtocolAdapter("BENDING2", new Dictionary<string, string>{{"READY", MachineProtocol.Bending2.Ready},{"START",MachineProtocol.Bending2.Start},{"STOP",MachineProtocol.Bending2.Stop},{"RESET",MachineProtocol.Bending2.Reset},{"STATUS",MachineProtocol.Bending2.Status}}),
            };
    }
}
