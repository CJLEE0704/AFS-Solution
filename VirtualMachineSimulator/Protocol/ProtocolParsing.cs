using System.Text;

namespace VirtualMachineSimulator.Protocol;

public sealed class ParsedCommand
{
    public string RawLine { get; init; } = string.Empty;
    public string MachinePrefix { get; init; } = string.Empty;
    public string CommandCode { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string PayloadBase64 { get; init; } = string.Empty;
    public bool IsLegacy { get; init; }
    public bool IsStatusQuery { get; init; }
    public bool IsReadyQuery { get; init; }
}

public static class ProtocolParsing
{
    public static ParsedCommand Parse(string line)
    {
        var raw = (line ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedCommand();
        }

        // Structured internal frame example:
        // LOADER:CMD=LOADER_JOB;CID=...;TS=...;PAYLOAD=BASE64...
        if (raw.Contains(":CMD=", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = raw[..raw.IndexOf(':')].Trim();
            return new ParsedCommand
            {
                RawLine = raw,
                MachinePrefix = prefix,
                CommandCode = GetToken(raw, "CMD").ToUpperInvariant(),
                CorrelationId = GetToken(raw, "CID"),
                PayloadBase64 = GetToken(raw, "PAYLOAD"),
                IsLegacy = false,
                IsStatusQuery = false,
                IsReadyQuery = false
            };
        }

        // Legacy examples:
        // CUT:READY?
        // CUT:STATUS?
        // CUT:START
        var splitIdx = raw.IndexOf(':');
        var machinePrefix = splitIdx >= 0 ? raw[..splitIdx].Trim() : string.Empty;
        var command = splitIdx >= 0 ? raw[(splitIdx + 1)..].Trim() : raw;
        var upper = command.ToUpperInvariant();

        return new ParsedCommand
        {
            RawLine = raw,
            MachinePrefix = machinePrefix,
            CommandCode = upper,
            CorrelationId = string.Empty,
            PayloadBase64 = string.Empty,
            IsLegacy = true,
            IsStatusQuery = upper == "STATUS?",
            IsReadyQuery = upper == "READY?"
        };
    }

    public static string? TryDecodePayload(string payloadBase64)
    {
        if (string.IsNullOrWhiteSpace(payloadBase64)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
        }
        catch
        {
            return null;
        }
    }

    private static string GetToken(string raw, string key)
    {
        var marker = key + "=";
        var idx = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return string.Empty;
        var start = idx + marker.Length;
        var remain = raw[start..];
        var end = remain.IndexOf(';');
        return end >= 0 ? remain[..end].Trim() : remain.Trim();
    }
}
