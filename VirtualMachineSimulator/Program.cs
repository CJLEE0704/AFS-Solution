using System.Net;
using System.Text.Json;
using VirtualMachineSimulator.Models;
using VirtualMachineSimulator.Simulator;

var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
if (!File.Exists(settingsPath))
{
    Console.Error.WriteLine($"appsettings.json not found: {settingsPath}");
    return 1;
}

var settings = JsonSerializer.Deserialize<SimulatorSettings>(await File.ReadAllTextAsync(settingsPath), new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
}) ?? new SimulatorSettings();

var bindAddress = IPAddress.Parse(settings.BindAddress);
var simulators = new Dictionary<string, MachineSimulator>(StringComparer.OrdinalIgnoreCase);
var servers = new List<TcpMachineServer>();
var cts = new CancellationTokenSource();

foreach (var machine in settings.Machines)
{
    var simulator = new MachineSimulator(machine, settings.DefaultCommandDurationMs);
    simulators[machine.Id] = simulator;
    servers.Add(new TcpMachineServer(bindAddress, machine, simulator));
}

var tasks = servers.Select(s => s.StartAsync(cts.Token)).ToArray();
Console.WriteLine();
Console.WriteLine("Virtual Machine Simulator started.");
Console.WriteLine("Commands:");
Console.WriteLine("  status");
Console.WriteLine("  alarm <MACHINE_ID> <ERROR_CODE>");
Console.WriteLine("  reset <MACHINE_ID>");
Console.WriteLine("  ready <MACHINE_ID> on|off");
Console.WriteLine("  complete <MACHINE_ID>");
Console.WriteLine("  estop <MACHINE_ID>");
Console.WriteLine("  quit");
Console.WriteLine();

while (true)
{
    foreach (var sim in simulators.Values) sim.Tick();
    Console.Write("> ");
    var input = Console.ReadLine();
    if (input == null) continue;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0) continue;

    var cmd = parts[0].ToLowerInvariant();
    if (cmd == "quit" || cmd == "exit") break;

    if (cmd == "status")
    {
        foreach (var sim in simulators.Values)
        {
            Console.WriteLine(sim.Describe());
        }
        continue;
    }

    if (parts.Length < 2)
    {
        Console.WriteLine("machine id required");
        continue;
    }

    if (!simulators.TryGetValue(parts[1], out var target))
    {
        Console.WriteLine($"unknown machine: {parts[1]}");
        continue;
    }

    switch (cmd)
    {
        case "alarm":
            target.SetAlarm(parts.Length >= 3 ? parts[2] : "SIM_ALARM");
            Console.WriteLine($"alarm set on {parts[1]}");
            break;
        case "reset":
            target.ClearAlarm();
            Console.WriteLine($"reset done on {parts[1]}");
            break;
        case "ready":
            if (parts.Length < 3)
            {
                Console.WriteLine("usage: ready <MACHINE_ID> on|off");
                break;
            }
            target.SetReady(parts[2].Equals("on", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"ready set on {parts[1]} = {parts[2]}");
            break;
        case "complete":
            target.ForceComplete();
            Console.WriteLine($"forced complete on {parts[1]}");
            break;
        case "estop":
            target.HandleCommand($"{parts[1]}:ESTOP");
            Console.WriteLine($"e-stop set on {parts[1]}");
            break;
        default:
            Console.WriteLine("unknown command");
            break;
    }
}

cts.Cancel();
foreach (var server in servers) server.Stop();
try { await Task.WhenAll(tasks); } catch { }
return 0;
