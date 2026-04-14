namespace VirtualMachineSimulator.Models;

public sealed class SimulatorSettings
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public int DefaultCommandDurationMs { get; set; } = 2500;
    public List<MachineEndpointSetting> Machines { get; set; } = new();
}

public sealed class MachineEndpointSetting
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public bool SupportsLegacyReady { get; set; } = true;
    public bool SupportsStructuredPayload { get; set; } = true;
}
