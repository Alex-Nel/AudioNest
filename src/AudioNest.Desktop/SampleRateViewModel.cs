using System.ComponentModel;
using System.Globalization;

namespace AudioNest.Desktop;

public sealed class SampleRateViewModel(int currentRate) : INotifyPropertyChanged
{
    private string _text = $"{currentRate} Hz";
    public IReadOnlyList<string> Presets { get; } =
        ["8 kHz", "11.025 kHz", "16 kHz", "22.05 kHz", "24 kHz", "32 kHz", "44.1 kHz", "48 kHz", "88.2 kHz", "96 kHz", "176.4 kHz", "192 kHz"];
    public string Text
    {
        get => _text;
        set { if (_text != value) { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); } }
    }
    public int? SampleRate
    {
        get
        {
            var value = Text.Trim();
            var multiplier = 1m;
            if (value.EndsWith("kHz", StringComparison.OrdinalIgnoreCase)) { value = value[..^3]; multiplier = 1000; }
            else if (value.EndsWith("k", StringComparison.OrdinalIgnoreCase)) { value = value[..^1]; multiplier = 1000; }
            else if (value.EndsWith("Hz", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
            if (!decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
                number < 0 || number > 192000) return null;
            number *= multiplier;
            return number is >= 8000 and <= 192000 && number == decimal.Truncate(number) ? (int)number : null;
        }
    }
    public string Error => SampleRate is null ? "Enter a whole-number rate from 8,000 to 192,000 Hz (for example 44100 or 44.1 kHz)." : "";
    public bool CanApply => SampleRate is { } rate && rate != currentRate;
    public event PropertyChangedEventHandler? PropertyChanged;
}
