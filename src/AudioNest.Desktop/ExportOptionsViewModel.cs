using System.ComponentModel;
using AudioNest.Hardware;

namespace AudioNest.Desktop;

public sealed record ExportRequest(string Path, AudioFileFormat Format);

public sealed class ExportOptionsViewModel(bool hasAudio) : INotifyPropertyChanged
{
    private string _path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mixdown.wav");
    private AudioFileType _format = AudioFileType.For(AudioFileFormat.Wav);
    public IReadOnlyList<AudioFileType> Formats => AudioFileType.All;
    public string Path
    {
        get => _path;
        set { _path = value; Changed(); }
    }
    public AudioFileType SelectedFormat
    {
        get => _format;
        set
        {
            if (value is null) return;
            _format = value;
            try { if (!string.IsNullOrWhiteSpace(Path)) _path = System.IO.Path.ChangeExtension(Path, value.Extension); }
            catch (ArgumentException) { }
            Changed();
        }
    }
    public string Error
    {
        get
        {
            if (!hasAudio) return "Add audio before exporting.";
            try
            {
                if (string.IsNullOrWhiteSpace(Path)) return "Choose an output file.";
                var full = System.IO.Path.GetFullPath(Path);
                if (!Directory.Exists(System.IO.Path.GetDirectoryName(full))) return "The output folder does not exist.";
                if (Directory.Exists(full)) return "Choose a file, not a folder.";
                if (!string.Equals(System.IO.Path.GetExtension(full), "." + SelectedFormat.Extension, StringComparison.OrdinalIgnoreCase))
                    return $"Use a .{SelectedFormat.Extension} extension for this format.";
                return "";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return "Invalid output file path."; }
        }
    }
    public bool CanExport => Error.Length == 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
