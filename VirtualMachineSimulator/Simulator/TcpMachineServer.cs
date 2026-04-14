using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VirtualMachineSimulator.Models;

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
                var response = _simulator.HandleCommand(line);
                Console.WriteLine($"[{_setting.Id}] RX: {line}");
                Console.WriteLine($"[{_setting.Id}] TX: {response}");
                await writer.WriteLineAsync(response);
            }
        }

        _simulator.State.IsConnectedClientPresent = false;
        Console.WriteLine($"[{_setting.Id}] client disconnected");
    }
}
