using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PipeBendingDashboard.Communication
{
    /// <summary>
    /// 장비 1대와 TCP/IP 통신하는 클라이언트
    /// 자동 재연결, 송수신, 로그 기능 포함
    /// </summary>
    public class TcpMachineClient : IDisposable
    {
        private TcpClient?     _client;
        private NetworkStream? _stream;

        private readonly string _machineName;
        private readonly string _ip;
        private readonly int    _port;

        public bool   IsConnected  => _client?.Connected ?? false;
        public string MachineName  => _machineName;
        public string IpAddress    => _ip;
        public int    Port         => _port;

        // 연결 상태 변경 이벤트
        public event Action<string, bool>?   ConnectionChanged; // (machineName, isConnected)
        public event Action<string, string>? LogReceived;       // (machineName, message)

        public TcpMachineClient(string machineName, string ip, int port)
        {
            _machineName = machineName;
            _ip          = ip;
            _port        = port;
        }

        // ── 연결 ──────────────────────────────────────────────────
        public async Task<bool> ConnectAsync()
        {
            try
            {
                Disconnect(); // 기존 연결 해제
                _client = new TcpClient
                {
                    ReceiveTimeout = 3000,
                    SendTimeout    = 3000
                };
                await _client.ConnectAsync(_ip, _port);
                _stream = _client.GetStream();

                Log($"연결 성공 [{_ip}:{_port}]");
                ConnectionChanged?.Invoke(_machineName, true);
                return true;
            }
            catch (Exception ex)
            {
                Log($"연결 실패 — {ex.Message}");
                ConnectionChanged?.Invoke(_machineName, false);
                return false;
            }
        }

        // ── 바이트 송신 ───────────────────────────────────────────
        public async Task<bool> SendAsync(byte[] data)
        {
            try
            {
                if (_stream == null || !IsConnected) return false;
                await _stream.WriteAsync(data);
                return true;
            }
            catch (Exception ex)
            {
                Log($"송신 실패 — {ex.Message}");
                await TryReconnectAsync();
                return false;
            }
        }

        // ── 문자열 송신 (ASCII/UTF8) ──────────────────────────────
        public async Task<bool> SendStringAsync(string command, Encoding? encoding = null)
            => await SendAsync((encoding ?? Encoding.ASCII).GetBytes(command));

        // ── 바이트 수신 ───────────────────────────────────────────
        public async Task<byte[]?> ReceiveAsync(int bufferSize = 1024)
        {
            try
            {
                if (_stream == null || !IsConnected) return null;
                var buffer = new byte[bufferSize];
                int bytesRead = await _stream.ReadAsync(buffer.AsMemory(0, bufferSize));
                if (bytesRead == 0) return null;
                return buffer[..bytesRead];
            }
            catch (Exception ex)
            {
                Log($"수신 실패 — {ex.Message}");
                return null;
            }
        }

        // ── 문자열 수신 ───────────────────────────────────────────
        public async Task<string?> ReceiveStringAsync(Encoding? encoding = null)
        {
            var data = await ReceiveAsync();
            return data == null ? null : (encoding ?? Encoding.ASCII).GetString(data);
        }

        // ── 송신 후 수신 (Request / Response) ────────────────────
        public async Task<byte[]?> SendReceiveAsync(byte[] command, int bufferSize = 1024)
        {
            if (!await SendAsync(command)) return null;
            return await ReceiveAsync(bufferSize);
        }

        public async Task<string?> SendReceiveStringAsync(string command, Encoding? encoding = null)
        {
            if (!await SendStringAsync(command, encoding)) return null;
            return await ReceiveStringAsync(encoding);
        }

        // ── 자동 재연결 ───────────────────────────────────────────
        private async Task TryReconnectAsync()
        {
            try
            {
                Log("재연결 시도 중...");
                ConnectionChanged?.Invoke(_machineName, false);
                await Task.Delay(2000);
                await ConnectAsync();
            }
            catch { }
        }

        // ── 연결 해제 ─────────────────────────────────────────────
        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
                _stream = null;
                _client = null;
            }
            catch { }
        }

        public void Dispose() => Disconnect();

        private void Log(string message)
        {
            var msg = $"[{_machineName}] {message}";
            System.Diagnostics.Debug.WriteLine(msg);
            LogReceived?.Invoke(_machineName, msg);
        }
    }
}
