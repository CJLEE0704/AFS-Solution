using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VirtualMachineSimulator.Models;
using VirtualMachineSimulator.Protocol;

namespace VirtualMachineSimulator.Simulator;

public sealed class TcpMachineServer
{
    private readonly MachineEndpointSetting _setting;
    private readonly MachineSimulator _simulator;
    private readonly IPAddress _bindAddress;
    private TcpListener? _listener;

    public TcpMachineServer(IPAddress bindAddress, MachineEndpointSetting setting, MachineSimulator simulator)
    {
        _bindAddress = bindAddress;
        _setting = setting;
        _simulator = simulator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener = new TcpListener(_bindAddress, _setting.Port);
            _listener.Start();
            Console.WriteLine($"[{_setting.Id}] listening on {_bindAddress}:{_setting.Port}");

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{_setting.Id}] server start/runtime error: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        _simulator.State.IsConnectedClientPresent = true;
        Console.WriteLine($"[{_setting.Id}] client connected");

        using (client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                _simulator.Tick();
                var parsed = ProtocolParsing.Parse(line);
                var response = _simulator.HandleCommand(line);
                var responseCategory = ClassifyResponse(response);
                Console.WriteLine($"[{_setting.Id}] RX: {line}");
                Console.WriteLine($"[{_setting.Id}] RX-PARSED: type={(parsed.IsLegacy ? "LEGACY" : "STRUCTURED")} cmd={parsed.CommandCode} cid={parsed.CorrelationId} seq={parsed.SequenceNo}");
                Console.WriteLine($"[{_setting.Id}] TX: {response}");
                Console.WriteLine($"[{_setting.Id}] TX-CATEGORY: {responseCategory}");
                Console.WriteLine($"[{_setting.Id}] STATE: {_simulator.Describe()}");
                await writer.WriteLineAsync(response);
            }
        }

        _simulator.State.IsConnectedClientPresent = false;
        Console.WriteLine($"[{_setting.Id}] client disconnected");
    }

    private static string ClassifyResponse(string response)
    {
        var raw = (response ?? string.Empty).Trim();
        var upper = raw.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(upper)) return "EMPTY";
        if (upper.StartsWith("OK")) return "ACK";
        if (upper.StartsWith("ERROR") || upper.StartsWith("FAIL") || upper.StartsWith("NACK")) return "ERROR/REJECTED";
        if (upper.Contains("EMERGENCY_STOP") || upper.Contains("ESTOP")) return "E_STOP";
        if (upper.Contains("ALARM") || upper.Contains("FAULT")) return "ALARM";
        if (upper.Contains("COMPLETE") || upper.Contains("FINISH") || upper.Contains("DONE")) return "COMPLETE";
        if (upper.Contains("WORKING") || upper.Contains("RUNNING") || upper.Contains("IN_PROGRESS")) return "STATE";
        if (upper.Contains("READY")) return "READY";
        if (upper.Contains("STOPPED") || upper.Contains("IDLE_STOP")) return "STOPPED";
        return "STATE";
    }
}
