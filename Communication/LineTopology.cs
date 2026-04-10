using System;
using System.Collections.Generic;
using System.Linq;

namespace PipeBendingDashboard.Communication
{
    public sealed class LineTopology
    {
        private static readonly string[] PreferredOrder = { "LOADER", "CUTTING", "LASER", "ROBOT", "BENDING", "BENDING2" };
        private readonly HashSet<string> _active;

        public IReadOnlyCollection<string> ActiveMachines => _active;

        public LineTopology(IEnumerable<string> activeMachineIds)
        {
            _active = new HashSet<string>(activeMachineIds.Select(s => s.ToUpperInvariant()));
        }

        public bool IsActive(string machineId) => _active.Contains(machineId.ToUpperInvariant());

        public IReadOnlyList<string> GetUpstreamCandidates(string machineId)
        {
            var target = machineId.ToUpperInvariant();
            var idx = Array.IndexOf(PreferredOrder, target);
            if (idx <= 0) return Array.Empty<string>();
            return PreferredOrder.Take(idx).Reverse().Where(IsActive).ToArray();
        }

        public IReadOnlyList<string> GetDownstreamCandidates(string machineId)
        {
            var target = machineId.ToUpperInvariant();
            var idx = Array.IndexOf(PreferredOrder, target);
            if (idx < 0 || idx >= PreferredOrder.Length - 1) return Array.Empty<string>();
            return PreferredOrder.Skip(idx + 1).Where(IsActive).ToArray();
        }

        public bool IsRouteValid(string fromMachineId, string toMachineId)
        {
            if (!IsActive(fromMachineId) || !IsActive(toMachineId)) return false;
            var fromIdx = Array.IndexOf(PreferredOrder, fromMachineId.ToUpperInvariant());
            var toIdx = Array.IndexOf(PreferredOrder, toMachineId.ToUpperInvariant());
            if (fromIdx < 0 || toIdx < 0 || fromIdx >= toIdx) return false;
            for (int i = fromIdx + 1; i <= toIdx; i++)
            {
                if (!_active.Contains(PreferredOrder[i])) return false;
            }
            return true;
        }

        public static bool IsMovementCommand(MachineCommandType type)
            => type is MachineCommandType.MoveTransfer
                or MachineCommandType.LoaderJob
                or MachineCommandType.LoadRequest
                or MachineCommandType.PrefetchLoad
                or MachineCommandType.BufferPrepare
                or MachineCommandType.CuttingJob
                or MachineCommandType.MarkingJob
                or MachineCommandType.BendingJob
                or MachineCommandType.ExecuteJob
                or MachineCommandType.LoadJob;
    }
}
