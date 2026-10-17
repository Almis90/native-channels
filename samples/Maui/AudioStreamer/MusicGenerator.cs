using System.Buffers.Binary;
using Plugin.Maui.AudioStreamer;

namespace AudioStreamer.Sample;

/// <summary>An original eight-bar instrumental loop, synthesized directly as PCM16 mono.</summary>
public sealed class MusicGenerator
{
    public const int DurationSeconds = 16;
    public const int LoopFrames = DurationSeconds * AudioFormat.SampleRate;
    private const int BeatFrames = AudioFormat.SampleRate / 2; // 120 BPM.
    private static readonly Note[] Score = CreateScore();
    private int _position;

    public byte[] Next(int frames, double level, bool fadeOut = false)
    {
        if (frames < 1 || frames > AudioFormat.MaxChunkFrames)
            throw new ArgumentOutOfRangeException(nameof(frames));
        if (!double.IsFinite(level) || level < 0 || level > 0.25)
            throw new ArgumentOutOfRangeException(nameof(level));

        var mix = new double[frames];
        var offset = 0;
        while (offset < frames)
        {
            var count = Math.Min(frames - offset, LoopFrames - _position);
            Render(mix.AsSpan(offset, count), _position);
            _position = (_position + count) % LoopFrames;
            offset += count;
        }

        var pcm = new byte[frames * 2];
        for (var index = 0; index < frames; index++)
        {
            var release = fadeOut ? (double)(frames - index - 1) / frames : 1;
            var sample = (short)Math.Round(Math.Tanh(mix[index]) * level * release * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(index * 2), sample);
        }
        return pcm;
    }

    private static void Render(Span<double> mix, int position)
    {
        foreach (var note in Score)
        {
            var first = Math.Max(position, note.Start);
            var last = Math.Min(position + mix.Length, note.End);
            for (var frame = first; frame < last; frame++)
            {
                var age = (double)(frame - note.Start) / AudioFormat.SampleRate;
                var remaining = (double)(note.End - frame - 1) / AudioFormat.SampleRate;
                var envelope = Math.Min(1, age / 0.006) * Math.Min(1, remaining / 0.08);
                var phase = 2 * Math.PI * note.Frequency * age;
                // Rounded piano-like notes: a strong fundamental and soft, decaying overtones.
                var sound = Math.Sin(phase) + 0.25 * Math.Sin(2 * phase) + 0.08 * Math.Sin(3 * phase);
                mix[frame - position] += sound * envelope * Math.Exp(-2.8 * age) * note.Gain;
            }
        }

        for (var index = 0; index < mix.Length; index++)
        {
            var frame = position + index;
            var beatAge = (double)(frame % BeatFrames) / AudioFormat.SampleRate;
            var attack = Math.Min(1, beatAge / 0.004);
            // A soft kick on each beat and a quiet brushed backbeat; no recordings or downloads.
            mix[index] +=
                0.2
                * attack
                * Math.Exp(-24 * beatAge)
                * Math.Sin(2 * Math.PI * (55 * beatAge + 2 * (1 - Math.Exp(-30 * beatAge))));
            if ((frame / BeatFrames) % 2 == 1)
            {
                var hash = unchecked((uint)frame * 747796405u + 2891336453u);
                hash = ((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u;
                var noise = ((hash >> 22) ^ hash) / (double)uint.MaxValue * 2 - 1;
                mix[index] += 0.06 * noise * attack * Math.Exp(-35 * beatAge);
            }
        }
    }

    private static Note[] CreateScore()
    {
        // Original melody over C–Am–F–G. MIDI pitches keep the arrangement easy to edit.
        int[][] chords =
        [
            [48, 52, 55],
            [45, 48, 52],
            [41, 45, 48],
            [43, 47, 50],
        ];
        int[][] melody =
        [
            [72, 76, 79, 76, 74, 72],
            [76, 79, 81, 79, 76, 72],
            [77, 76, 72, 69, 72, 76],
            [74, 79, 77, 74, 71, 67],
            [76, 79, 84, 79, 76, 74],
            [72, 76, 81, 79, 76, 72],
            [69, 72, 77, 76, 72, 69],
            [71, 74, 79, 74, 72, 72],
        ];
        double[] onsets = [0, 0.5, 1, 2, 2.5, 3];
        double[] lengths = [0.45, 0.45, 0.9, 0.45, 0.45, 0.9];
        var notes = new List<Note>();
        for (var bar = 0; bar < melody.Length; bar++)
        {
            var chord = chords[bar % chords.Length];
            for (var beat = 0; beat < 4; beat++)
            {
                Add(bar * 4 + beat, 0.8, chord[beat % chord.Length] + 12, 0.22);
                if (beat % 2 == 0)
                    Add(bar * 4 + beat, 1.7, chord[0] - 12, 0.35);
            }
            for (var note = 0; note < onsets.Length; note++)
                Add(bar * 4 + onsets[note], lengths[note], melody[bar][note], 0.55);
        }
        return notes.ToArray();

        void Add(double beat, double duration, int midi, double gain) =>
            notes.Add(
                new(
                    (int)(beat * BeatFrames),
                    (int)((beat + duration) * BeatFrames),
                    440 * Math.Pow(2, (midi - 69) / 12.0),
                    gain
                )
            );
    }

    private sealed record Note(int Start, int End, double Frequency, double Gain);
}
