using System.Runtime.InteropServices;
using AudioNest.Hardware;
using SoundFlow.Enums;

namespace AudioNest.Hardware.SoundFlow;

/// <summary>
/// The SoundFlow factory maps AAC to "aac", but FFmpeg's AAC muxer is named
/// "adts". Use the same bundled native ABI with that container name, without
/// reflection or a separate ffmpeg executable.
/// </summary>
internal sealed partial class AacFileEncoder : IAudioFileEncoder
{
    private readonly FileStream _stream;
    private readonly int _channels;
    private readonly WriteCallback _writer;
    private nint _encoder;
    private Exception? _writeError;

    static AacFileEncoder()
    {
        NativeLibrary.SetDllImportResolver(typeof(AacFileEncoder).Assembly, (name, assembly, path) =>
        {
            if (name != "soundflow-ffmpeg") return 0;
            var file = OperatingSystem.IsWindows() ? name + ".dll" :
                OperatingSystem.IsMacOS() ? "lib" + name + ".dylib" : "lib" + name + ".so";
            if (NativeLibrary.TryLoad(file, assembly, path, out var handle)) return handle;
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "runtimes",
                RuntimeInformation.RuntimeIdentifier, "native", file));
        });
    }
    public AacFileEncoder(FileStream stream, PcmFormat format)
    {
        _stream = stream;
        _channels = format.Channels;
        _writer = WriteBytes;
        _encoder = Create();
        if (_encoder == 0) throw new IOException("Unable to create AAC encoder.");
        var result = Initialize(_encoder, "adts", _writer, 0, SampleFormat.F32, (uint)format.Channels, (uint)format.SampleRate);
        if (result != 0) { Free(_encoder); _encoder = 0; throw new IOException($"Unable to initialize AAC/ADTS encoder ({result})."); }
    }
    public unsafe void Write(Span<float> samples)
    {
        ObjectDisposedException.ThrowIf(_encoder == 0, this);
        fixed (float* data = samples)
        {
            var result = Encode(_encoder, (nint)data, samples.Length / _channels, out var frames);
            if (result != 0 || frames * _channels != samples.Length)
                throw new IOException($"AAC encoding failed ({result}).");
        }
        if (_writeError is not null) throw new IOException("Cannot write AAC output.", _writeError);
    }
    private unsafe nuint WriteBytes(nint state, nint data, nuint count)
    {
        try { _stream.Write(new ReadOnlySpan<byte>((void*)data, checked((int)count))); return count; }
        catch (Exception ex) { _writeError = ex; return 0; }
    }
    public void Dispose()
    {
        if (_encoder == 0) return;
        var encoder = _encoder;
        _encoder = 0;
        try
        {
            Free(encoder); // Flushes buffered AAC frames and the ADTS trailer.
            GC.KeepAlive(_writer);
            if (_writeError is not null) throw new IOException("Cannot finalize AAC output.", _writeError);
            _stream.Flush(true);
        }
        finally { _stream.Dispose(); GC.SuppressFinalize(this); }
    }
    ~AacFileEncoder() { try { Dispose(); } catch { /* No exception may escape a finalizer. */ } }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint WriteCallback(nint state, nint data, nuint count);
    [LibraryImport("soundflow-ffmpeg", EntryPoint = "sf_encoder_create")]
    private static partial nint Create();
    [LibraryImport("soundflow-ffmpeg", EntryPoint = "sf_encoder_init", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Initialize(nint encoder, string format, WriteCallback writer, nint state, SampleFormat sampleFormat, uint channels, uint sampleRate);
    [LibraryImport("soundflow-ffmpeg", EntryPoint = "sf_encoder_write_pcm_frames")]
    private static partial int Encode(nint encoder, nint data, long frames, out long written);
    [LibraryImport("soundflow-ffmpeg", EntryPoint = "sf_encoder_free")]
    private static partial void Free(nint encoder);
}
