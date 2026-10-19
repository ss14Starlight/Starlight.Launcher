using System.Text;
using System.Text.RegularExpressions;
using Starlight.Launcher.WebUI.Models.Diagnostics;
using Starlight.Launcher.WebUI.Models.Helpers;

namespace Starlight.Launcher.Services.Diagnostics;

/// <summary>
/// Recent stdout/stderr of the game, for the console on the Development page. The full output goes to the client log.
/// </summary>
public sealed partial class ClientConsole
{
    private const int Capacity = 5000;

    private readonly RingBuffer<ConsoleLine> _lines = [with(Capacity)];
    private readonly Lock _lock = new();

    /// <summary>
    /// Raised from the pipe reader threads.
    /// </summary>
    public event Action<ConsoleLine>? Output;

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape { get; }

    public IReadOnlyList<ConsoleLine> Snapshot()
    {
        lock (_lock)
            return [.. _lines];
    }

    public void Clear()
    {
        lock (_lock)
            _lines.Clear();
    }

    public void AddLine(string text, bool isError)
    {
        var line = new ConsoleLine(DateTimeOffset.Now, AnsiEscape.Replace(text, ""), isError);
        lock (_lock)
            _lines.Add(line);

        Output?.Invoke(line);
    }

    /// <summary>
    /// Splits one output stream of the client into lines. Not thread-safe, one per stream.
    /// </summary>
    public LineSplitter CreateSplitter(bool isError) => new(this, isError);

    public sealed class LineSplitter(ClientConsole console, bool isError)
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _pending = new();
        private char[] _chars = new char[4096];

        public void Write(ReadOnlySpan<byte> bytes)
        {
            var needed = _decoder.GetCharCount(bytes, flush: false);
            if (_chars.Length < needed)
                _chars = new char[needed];

            var count = _decoder.GetChars(bytes, _chars, flush: false);
            foreach (var c in _chars.AsSpan(0, count))
            {
                if (c == '\n')
                {
                    console.AddLine(_pending.ToString().TrimEnd('\r'), isError);
                    _ = _pending.Clear();
                }
                else
                {
                    _ = _pending.Append(c);
                }
            }
        }

        public void Flush()
        {
            if (_pending.Length > 0)
                console.AddLine(_pending.ToString(), isError);

            _ = _pending.Clear();
        }
    }
}
