using System.Buffers.Binary;

namespace MyAdventure.Shared.Feedback;

public static class ToneSynthesizer
{
    public const int SampleRate = 44_100;
    public const int HeaderSize = 44;

    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const short BlockAlign = Channels * BitsPerSample / 8;
    private const double Volume = 0.3;
    private const double AttackSeconds = 0.002;

    private readonly record struct Note(double StartHz, double EndHz, double Seconds, bool Square = false);

    public static byte[] Render(FeedbackCue cue) => ToWav(Synthesize(Score(cue)));

    private static Note[] Score(FeedbackCue cue) => cue switch
    {
        FeedbackCue.Tap => [new(1_400, 1_100, 0.025)],
        FeedbackCue.Purchase => [new(988, 988, 0.05), new(1_319, 1_319, 0.09)],
        FeedbackCue.Manager => [new(659, 659, 0.05), new(880, 880, 0.05), new(1_109, 1_109, 0.1)],
        FeedbackCue.Milestone => [new(523, 523, 0.06), new(659, 659, 0.06), new(784, 784, 0.06), new(1_047, 1_047, 0.16)],
        FeedbackCue.Prestige => [new(262, 1_047, 0.35), new(1_047, 1_568, 0.2)],
        FeedbackCue.Denied => [new(196, 175, 0.07, Square: true), new(0, 0, 0.03), new(175, 156, 0.09, Square: true)],
        _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null),
    };

    private static short[] Synthesize(Note[] notes)
    {
        var samples = new short[notes.Sum(n => SampleCount(n.Seconds))];
        var offset = 0;
        var phase = 0.0;

        foreach (var note in notes)
        {
            var count = SampleCount(note.Seconds);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / SampleRate;
                var progress = (double)i / count;
                var frequency = note.StartHz + (note.EndHz - note.StartHz) * progress;
                phase += 2 * Math.PI * frequency / SampleRate;

                var wave = Math.Sin(phase);
                if (note.Square) wave = Math.Sign(wave) * 0.5;

                var envelope = Math.Min(1.0, t / AttackSeconds) * Math.Pow(1.0 - progress, 2);
                samples[offset + i] = frequency <= 0 ? (short)0 : (short)(wave * envelope * Volume * short.MaxValue);
            }
            offset += count;
        }

        return samples;
    }

    private static int SampleCount(double seconds) => (int)(seconds * SampleRate);

    private static byte[] ToWav(short[] samples)
    {
        var dataSize = samples.Length * sizeof(short);
        var wav = new byte[HeaderSize + dataSize];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderSize - 8 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);

        var data = span[HeaderSize..];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * sizeof(short))..], samples[i]);
        }

        return wav;
    }
}
