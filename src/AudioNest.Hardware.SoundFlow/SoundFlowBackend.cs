using System.Globalization;
using AudioNest.Hardware;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Interfaces;
using SoundFlow.Utils;

namespace AudioNest.Hardware.SoundFlow;

/// <summary>Only this assembly knows SoundFlow/MiniAudio types. Native initialization is lazy.</summary>
public sealed class SoundFlowBackend : IAudioBackend
{
    private MiniAudioEngine? _engine;
    private MiniAudioEngine Engine => _engine ??= new MiniAudioEngine();
    private const int MaximumAssetSeconds = 600;

    public IReadOnlyList<Hardware.AudioDevice> GetOutputDevices()
    {
        Engine.UpdateAudioDevicesInfo();
        return Map(Engine.PlaybackDevices);
    }

    public IReadOnlyList<Hardware.AudioDevice> GetInputDevices()
    {
        Engine.UpdateAudioDevicesInfo();
        return Map(Engine.CaptureDevices);
    }

    private static Hardware.AudioDevice[] Map(DeviceInfo[] devices) =>
        devices.Select((d, i) => new Hardware.AudioDevice(i.ToString(CultureInfo.InvariantCulture), d.Name, d.IsDefault)).ToArray();

    private DeviceInfo? Select(bool input, string? deviceId)
    {
        Engine.UpdateAudioDevicesInfo();
        var devices = input ? Engine.CaptureDevices : Engine.PlaybackDevices;
        if (devices.Length == 0) throw new InvalidOperationException($"No {(input ? "input" : "output")} audio device is available.");
        if (deviceId is null) return devices.FirstOrDefault(d => d.IsDefault, devices[0]);
        if (!int.TryParse(deviceId, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= devices.Length)
            throw new InvalidOperationException("The selected audio device is no longer available.");
        return devices[index];
    }

    private static AudioFormat Convert(PcmFormat format)
    {
        format.Validate();
        return new AudioFormat
        {
            Format = SampleFormat.F32,
            SampleRate = format.SampleRate,
            Channels = format.Channels,
            Layout = AudioFormat.GetLayoutFromChannels(format.Channels)
        };
    }

    public IAudioOutput OpenOutput(PcmFormat format, RenderAudio render, string? deviceId = null)
    {
        var nativeFormat = Convert(format);
        var device = Engine.InitializePlaybackDevice(Select(false, deviceId), nativeFormat);
        try
        {
            var source = new CallbackSource(Engine, nativeFormat, render);
            // SoundFlow's equal-power centered panning applies sqrt(0.5) per stage.
            // Compensate at the source and master so export and playback have unity gain.
            if (format.Channels == 2) device.MasterMixer.Volume = MathF.Sqrt(2);
            device.MasterMixer.AddComponent(source);
            return new Output(device, source);
        }
        catch { device.Dispose(); throw; }
    }

    public IAudioRecording StartRecording(PcmFormat format, string path, string? deviceId = null)
    {
        var device = Engine.InitializeCaptureDevice(Select(true, deviceId), Convert(format));
        var meter = new AudioPeakMeter();
        var preview = new RecordingPreview(format);
        AudioProcessCallback observe = (samples, _) => { meter.Measure(samples); preview.Append(samples); };
        Recorder? recorder = null;
        try
        {
            device.OnAudioProcessed += observe;
            recorder = new Recorder(device, path);
            var result = recorder.StartRecording();
            if (result.IsFailure) throw new IOException($"Cannot start recording: {result.Error}");
            device.Start();
            return new Recording(device, recorder, meter, preview, observe);
        }
        catch
        {
            device.OnAudioProcessed -= observe;
            recorder?.Dispose();
            device.Dispose();
            throw;
        }
    }

    public DecodedAudio Decode(string path, PcmFormat targetFormat, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var ffmpeg = extension is "aac" or "ogg" or "m4a" or "oga";
        using var decoder = ffmpeg
            ? new FFmpegCodecFactory().CreateDecoder(stream, extension == "oga" ? "ogg" : extension, Convert(targetFormat))
                ?? throw new NotSupportedException("Unsupported audio format.")
            : Engine.CreateDecoder(stream, out _, Convert(targetFormat));
        if (decoder.Channels is < 1 or > 2 || decoder.SampleRate <= 0)
            throw new NotSupportedException("Only mono and stereo sources are currently supported.");
        var samples = new List<float>();
        var block = new float[8192 * decoder.Channels];
        var limit = checked(decoder.SampleRate * decoder.Channels * MaximumAssetSeconds);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = decoder.Decode(block);
            if (count == 0) break;
            if (count < 0 || count > block.Length) throw new InvalidDataException("Decoder returned an invalid sample count.");
            if (samples.Count > limit - count)
                throw new NotSupportedException("The MVP supports up to 10 minutes per imported audio file.");
            for (var i = 0; i < count; i++) samples.Add(block[i]);
        }
        if (samples.Count == 0) throw new InvalidDataException("The file contains no decodable audio.");
        var data = samples.ToArray();
        if (decoder.SampleRate != targetFormat.SampleRate)
            data = MathHelper.ResampleLinear(data, decoder.Channels, decoder.SampleRate, targetFormat.SampleRate);
        if (decoder.Channels != targetFormat.Channels)
        {
            var converted = new float[data.Length / decoder.Channels * targetFormat.Channels];
            for (var frame = 0; frame < data.Length / decoder.Channels; frame++)
                for (var ch = 0; ch < targetFormat.Channels; ch++)
                    converted[frame * targetFormat.Channels + ch] = decoder.Channels == 1 ? data[frame]
                        : data[frame * 2] * 0.5f + data[frame * 2 + 1] * 0.5f;
            data = converted;
        }
        return new DecodedAudio(targetFormat, data);
    }

    public IAudioFileEncoder OpenEncoder(string path, PcmFormat format, AudioFileFormat fileFormat)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        try
        {
            if (fileFormat == AudioFileFormat.Aac) return new AacFileEncoder(stream, format);
            var encoder = new FFmpegCodecFactory().CreateEncoder(stream, AudioFileType.For(fileFormat).Extension, Convert(format))
                ?? throw new NotSupportedException("Encoder is not available.");
            return new FileEncoder(stream, encoder);
        }
        catch { stream.Dispose(); throw; }
    }
    private sealed class FileEncoder(FileStream stream, ISoundEncoder encoder) : IAudioFileEncoder
    {
        public void Write(Span<float> samples)
        {
            if (encoder.Encode(samples) != samples.Length) throw new IOException("Encoder did not consume the entire audio block.");
        }
        public void Dispose()
        {
            try { encoder.Dispose(); stream.Flush(true); }
            finally { stream.Dispose(); }
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }

    private sealed class CallbackSource : SoundComponent
    {
        private readonly RenderAudio _render;
        public CallbackSource(AudioEngine engine, AudioFormat format, RenderAudio render) : base(engine, format)
        {
            _render = render;
            if (format.Channels == 2) Volume = MathF.Sqrt(2);
        }
        protected override void GenerateAudio(Span<float> buffer, int channels) => _render(buffer);
    }

    private sealed class Output(AudioPlaybackDevice device, CallbackSource source) : IAudioOutput
    {
        public void Start() => device.Start();
        public void Stop() => device.Stop();
        public void Dispose()
        {
            device.Stop();
            device.MasterMixer.RemoveComponent(source);
            source.Dispose();
            device.Dispose();
        }
    }

    private sealed class Recording(AudioCaptureDevice device, Recorder recorder, AudioPeakMeter meter, RecordingPreview preview,
        AudioProcessCallback observe) : IAudioRecording
    {
        private bool _stopped;
        public AudioPeakMeter InputMeter => meter;
        public RecordingPreview Preview => preview;
        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            // Quiesce the callback before finalizing the encoder or stream.
            device.Stop();
            device.OnAudioProcessed -= observe;
            meter.Clear();
            try
            {
                var result = recorder.StopRecording();
                if (result.IsFailure) throw new IOException($"Cannot finalize recording: {result.Error}");
            }
            finally
            {
                recorder.Dispose();
                device.Dispose();
            }
        }
        public void Dispose() => Stop();
    }
}
