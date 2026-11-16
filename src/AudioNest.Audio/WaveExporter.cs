using System.Text;

namespace AudioNest.Audio;

/// <summary>Streaming PCM16 RIFF writer. Uses the same render path as playback.</summary>
public static class WaveExporter
{
    public static void Export(RenderPlan plan, string path, CancellationToken cancellationToken = default)
    {
        var dataBytes = checked(plan.Duration * plan.Format.Channels * sizeof(short));
        if (dataBytes > uint.MaxValue - 36) throw new NotSupportedException("Mixdown exceeds WAV's 4 GB limit.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write("RIFF"u8);
                writer.Write((uint)(36 + dataBytes));
                writer.Write("WAVEfmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)plan.Format.Channels);
                writer.Write(plan.Format.SampleRate);
                writer.Write(plan.Format.SampleRate * plan.Format.Channels * 2);
                writer.Write((short)(plan.Format.Channels * 2));
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write((uint)dataBytes);
                var buffer = new float[4096 * plan.Format.Channels];
                for (long frame = 0; frame < plan.Duration; frame += 4096)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(4096, plan.Duration - frame) * plan.Format.Channels;
                    plan.Render(frame, buffer.AsSpan(0, count));
                    for (var i = 0; i < count; i++)
                        writer.Write((short)Math.Round(buffer[i] * short.MaxValue));
                }
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
