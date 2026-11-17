using AudioNest.Hardware;

namespace AudioNest.Audio;

public static class AudioExporter
{
    public static void Export(RenderPlan plan, string path, AudioFileFormat format, IAudioBackend backend,
        CancellationToken cancellationToken = default)
    {
        if (format == AudioFileFormat.Wav) { WaveExporter.Export(plan, path, cancellationToken); return; }
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var encoder = backend.OpenEncoder(temp, plan.Format, format))
            {
                var buffer = new float[4096 * plan.Format.Channels];
                for (long frame = 0; frame < plan.Duration; frame += 4096)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var block = buffer.AsSpan(0, (int)Math.Min(4096, plan.Duration - frame) * plan.Format.Channels);
                    plan.Render(frame, block);
                    encoder.Write(block);
                }
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
