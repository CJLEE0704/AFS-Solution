using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly List<byte> _recvBuffer = new();
        private const int MaxLineBytes = 4096;

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

        /// <summary>
        /// CRLF/LF 라인 단위 수신 (TCP 패킷 분할/합침 대응)
        /// </summary>
        public async Task<string?> ReceiveLineAsync(Encoding? encoding = null)
        {
            try
            {
                if (_stream == null || !IsConnected) return null;
                encoding ??= Encoding.ASCII;

                while (true)
                {
                    var line = TryExtractLine(encoding);
                    if (line != null) return line;

                    var chunk = new byte[1024];
                    int bytesRead = await _stream.ReadAsync(chunk.AsMemory(0, chunk.Length));
                    if (bytesRead == 0) return null;

                    for (int i = 0; i < bytesRead; i++) _recvBuffer.Add(chunk[i]);
                    if (_recvBuffer.Count > MaxLineBytes)
                    {
                        Log($"수신 프레임 초과 ({_recvBuffer.Count} bytes)");
                        _recvBuffer.Clear();
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"라인 수신 실패 — {ex.Message}");
                return null;
            }
        }

        // ── 송신 후 수신 (Request / Response) ────────────────────
        public async Task<byte[]?> SendReceiveAsync(byte[] command, int bufferSize = 1024)
        {
            if (!await SendAsync(command)) return null;
            return await ReceiveAsync(bufferSize);
        }

        public async Task<string?> SendReceiveStringAsync(string command, Encoding? encoding = null)
        {
            await _ioLock.WaitAsync();
            try
            {
                if (!await SendStringAsync(command, encoding)) return null;
                var line = await ReceiveLineAsync(encoding);
                if (line == null) return null;
                if (!IsValidResponse(line))
                {
                    Log($"응답 형식 오류 — '{line}'");
                    return null;
                }
                return line;
            }
            finally
            {
                _ioLock.Release();
            }
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

        public void Dispose()
        {
            _ioLock.Dispose();
            Disconnect();
        }

        private void Log(string message)
        {
            var msg = $"[{_machineName}] {message}";
            System.Diagnostics.Debug.WriteLine(msg);
            LogReceived?.Invoke(_machineName, msg);
        }

        private string? TryExtractLine(Encoding encoding)
        {
            for (int i = 0; i < _recvBuffer.Count; i++)
            {
                if (_recvBuffer[i] == (byte)'\n')
                {
                    int len = i;
                    if (len > 0 && _recvBuffer[len - 1] == (byte)'\r') len--;

                    var lineBytes = _recvBuffer.GetRange(0, len).ToArray();
                    _recvBuffer.RemoveRange(0, i + 1);
                    return encoding.GetString(lineBytes).Trim();
                }
            }
            return null;
        }

        private static bool IsValidResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return false;
            var upper = response.Trim().ToUpperInvariant();
            return upper.StartsWith("OK")
                || upper.StartsWith("ERROR")
                || upper.StartsWith("NOT_READY")
                || upper.Contains("STATUS")
                || upper.Contains("RUNNING")
                || upper.Contains("WORKING")
                || upper.Contains("IDLE")
                || upper.Contains("READY")
                || upper.Contains("FINISH")
                || upper.Contains("UNLOAD_COMPLETE")
                || upper.Contains("ALARM");
        }
    }
}
