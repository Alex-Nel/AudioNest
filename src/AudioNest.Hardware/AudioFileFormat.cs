namespace AudioNest.Hardware;

public enum AudioFileFormat { Wav, Mp3, Aac, Ogg, Flac }

public sealed record AudioFileType(AudioFileFormat Format, string Name, string Extension)
{
    public static IReadOnlyList<AudioFileType> All { get; } =
    [
        new(AudioFileFormat.Wav, "WAV · PCM16", "wav"),
        new(AudioFileFormat.Mp3, "MP3", "mp3"),
        new(AudioFileFormat.Aac, "AAC · ADTS", "aac"),
        new(AudioFileFormat.Ogg, "OGG · Vorbis", "ogg"),
        new(AudioFileFormat.Flac, "FLAC", "flac")
    ];
    public static AudioFileType For(AudioFileFormat format) => All.First(t => t.Format == format);
}

public interface IAudioFileEncoder : IDisposable
{
    void Write(Span<float> samples);
}
