using System.Buffers.Binary;
using System.Text;
using MyAdventure.Shared.Feedback;
using Shouldly;

namespace MyAdventure.UI.Tests;

public class ToneSynthesizerTests
{
    [Theory]
    [InlineData(FeedbackCue.Tap)]
    [InlineData(FeedbackCue.Purchase)]
    [InlineData(FeedbackCue.Manager)]
    [InlineData(FeedbackCue.Milestone)]
    [InlineData(FeedbackCue.Prestige)]
    [InlineData(FeedbackCue.Denied)]
    public void Render_ProducesValidMono16BitPcmWave(FeedbackCue cue)
    {
        var wav = ToneSynthesizer.Render(cue);
        var span = wav.AsSpan();

        Encoding.ASCII.GetString(wav, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(wav, 8, 4).ShouldBe("WAVE");
        Encoding.ASCII.GetString(wav, 36, 4).ShouldBe("data");
        BinaryPrimitives.ReadInt32LittleEndian(span[4..]).ShouldBe(wav.Length - 8);
        BinaryPrimitives.ReadInt16LittleEndian(span[22..]).ShouldBe((short)1);
        BinaryPrimitives.ReadInt32LittleEndian(span[24..]).ShouldBe(ToneSynthesizer.SampleRate);
        BinaryPrimitives.ReadInt16LittleEndian(span[34..]).ShouldBe((short)16);
        BinaryPrimitives.ReadInt32LittleEndian(span[40..]).ShouldBe(wav.Length - ToneSynthesizer.HeaderSize);
    }

    [Theory]
    [InlineData(FeedbackCue.Tap)]
    [InlineData(FeedbackCue.Purchase)]
    [InlineData(FeedbackCue.Manager)]
    [InlineData(FeedbackCue.Milestone)]
    [InlineData(FeedbackCue.Prestige)]
    [InlineData(FeedbackCue.Denied)]
    public void Render_IsShortAudibleAndUnclipped(FeedbackCue cue)
    {
        var data = ToneSynthesizer.Render(cue).AsSpan(ToneSynthesizer.HeaderSize);
        var samples = data.Length / sizeof(short);
        var seconds = (double)samples / ToneSynthesizer.SampleRate;

        seconds.ShouldBeInRange(0.01, 0.6);

        var peak = 0;
        for (var i = 0; i < samples; i++)
        {
            peak = Math.Max(peak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..])));
        }

        peak.ShouldBeGreaterThan(1000);
        peak.ShouldBeLessThan(short.MaxValue / 2);
    }

    [Fact]
    public void Render_EveryCueIsCovered() =>
        Enum.GetValues<FeedbackCue>().Length.ShouldBe(6);

    [Fact]
    public void Render_IsDeterministic() =>
        ToneSynthesizer.Render(FeedbackCue.Milestone).SequenceEqual(ToneSynthesizer.Render(FeedbackCue.Milestone)).ShouldBeTrue();

    [Fact]
    public void Render_CuesSoundDifferent() =>
        ToneSynthesizer.Render(FeedbackCue.Purchase).SequenceEqual(ToneSynthesizer.Render(FeedbackCue.Denied)).ShouldBeFalse();
}
