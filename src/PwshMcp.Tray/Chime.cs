using System.IO;
using System.Media;

namespace PwshMcp.Tray;

/// <summary>The notification chime, synthesized from <see cref="Sound"/> so the asset and the played sound are one source.</summary>
public static class Chime
{
    /// <summary>Mono 16-bit samples. Each note rises over a raised-cosine attack and decays; the tail fades to exact silence.</summary>
    public static short[] Samples()
    {
        var n = (int)Math.Round(Sound.Length * Sound.Rate);
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Sound.Rate, v = 0;
            foreach (var (hz, at) in Sound.Notes)
            {
                var u = t - at;
                if (u < 0) continue;
                var env = (u < Sound.Attack ? 0.5 - 0.5 * Math.Cos(Math.PI * u / Sound.Attack) : 1) * Math.Exp(-u / Sound.Decay);
                v += env * (Math.Sin(2 * Math.PI * hz * u) + Sound.Overtone * Math.Sin(4 * Math.PI * hz * u));
            }
            var left = Sound.Length - t;
            if (left < Sound.Release) v *= 0.5 - 0.5 * Math.Cos(Math.PI * Math.Max(0, left) / Sound.Release);
            pcm[i] = (short)Math.Round(Math.Clamp(v * Sound.Peak / (1 + Sound.Overtone), -1, 1) * short.MaxValue);
        }
        pcm[0] = 0; pcm[^1] = 0;
        return pcm;
    }

    /// <summary>RIFF/WAVE PCM container for <see cref="Samples"/>.</summary>
    public static byte[] Wav()
    {
        var pcm = Samples();
        int block = Sound.Channels * Sound.Bits / 8, data = pcm.Length * block;
        using var ms = new MemoryStream(44 + data);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)Sound.Channels);
        w.Write(Sound.Rate); w.Write(Sound.Rate * block); w.Write((short)block); w.Write((short)Sound.Bits);
        w.Write("data"u8); w.Write(data);
        foreach (var s in pcm) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Plays once on a worker so the UI never waits. Stream and player live until the sound ends; a failure faults the task.</summary>
    public static Task Play() => Task.Run(() =>
    {
        using var ms = new MemoryStream(Wav(), false);
        using var player = new SoundPlayer(ms);
        player.PlaySync();
    });
}
