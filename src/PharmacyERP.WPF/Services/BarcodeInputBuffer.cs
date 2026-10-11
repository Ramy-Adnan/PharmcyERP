using System.Text;

namespace PharmacyERP.WPF.Services;

/// <summary>
/// Buffers HID scans independently of the search box. A prefix identifies scanner
/// input reliably; without one, only rapid bursts outside editable controls qualify.
/// Times are monotonic milliseconds, supplied by the input adapter.
/// </summary>
public sealed class BarcodeInputBuffer
{
    private const int MaximumLength = 50;
    private const int MaximumGapMs = 50;
    private readonly StringBuilder _text = new();
    private long _lastInput;
    private bool _invalid;
    public bool IsPrefixed { get; private set; }
    public bool HasPendingInput => IsPrefixed || _text.Length > 0;
    public int IdleTimeoutMs => _text.Length == 0 && IsPrefixed ? 2000 : 180;

    public void BeginPrefixedScan(long now)
    {
        Reset(); IsPrefixed = true; _lastInput = now;
    }

    // Returns true only for explicitly prefixed input, which must not reach an editor.
    public bool Append(string text, long now, bool allowUnprefixed)
    {
        var consume = IsPrefixed;
        if (!IsPrefixed && !allowUnprefixed) { Reset(); return false; }
        if (text.Length == 0) return consume;
        if (_invalid) return consume;
        if (text.Any(c => c < '!' || c > '~')) { Reject(); return consume; }
        if (!IsPrefixed && _text.Length > 0 && now - _lastInput > MaximumGapMs) _text.Clear();
        if (_text.Length + text.Length > MaximumLength) { Reject(); return consume; }
        _text.Append(text); _lastInput = now;
        return consume;
    }

    public string? Complete()
    {
        var barcode = !_invalid && _text.Length >= (IsPrefixed ? 1 : 3) ? _text.ToString() : null;
        Reset();
        return barcode;
    }

    private void Reject()
    {
        if (!IsPrefixed) { Reset(); return; }
        _text.Clear(); _invalid = true;
    }
    public void Reset() { _text.Clear(); IsPrefixed = false; _invalid = false; _lastInput = 0; }
}
