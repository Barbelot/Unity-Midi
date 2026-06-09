using System.Collections.Generic;

namespace Midi
{
    /// <summary>
    /// Converts absolute MIDI ticks to milliseconds while honoring a file's full tempo map
    /// (any number of FF 51 tempo changes), instead of assuming a single constant BPM.
    ///
    /// A constant-BPM model breaks any file that contains tempo changes: every note after the
    /// first tempo change is shifted, and a deliberately fast lead-in measure (a count-in) gets
    /// stretched to the song tempo, inserting a phantom offset before the first note.
    /// </summary>
    public class MidiTempoMap
    {
        private const int DefaultMicrosecondsPerQuarter = 500000; // 120 BPM

        public readonly struct TempoChange
        {
            public readonly int Tick;
            public readonly int MicrosecondsPerQuarter;

            public TempoChange(int tick, int microsecondsPerQuarter)
            {
                Tick = tick;
                MicrosecondsPerQuarter = microsecondsPerQuarter;
            }
        }

        private struct Segment
        {
            public int Tick;
            public int MicrosecondsPerQuarter;
            public double CumulativeMs;
        }

        private readonly int _ticksPerQuarterNote;
        private readonly List<Segment> _segments = new List<Segment>();

        public MidiTempoMap(int ticksPerQuarterNote, IReadOnlyList<TempoChange> tempoChanges)
        {
            _ticksPerQuarterNote = ticksPerQuarterNote < 1 ? 1 : ticksPerQuarterNote;

            var changes = new List<TempoChange>();
            for (var i = 0; i < tempoChanges.Count; i++)
            {
                if (tempoChanges[i].MicrosecondsPerQuarter > 0)
                {
                    changes.Add(tempoChanges[i]);
                }
            }

            changes.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            // Guarantee a tempo is defined from tick 0 so any tick can be resolved.
            if (changes.Count == 0 || changes[0].Tick > 0)
            {
                changes.Insert(0, new TempoChange(0, DefaultMicrosecondsPerQuarter));
            }

            foreach (var change in changes)
            {
                // Multiple tempo events on the same tick: the last one wins.
                if (_segments.Count > 0 && _segments[_segments.Count - 1].Tick == change.Tick)
                {
                    var last = _segments[_segments.Count - 1];
                    last.MicrosecondsPerQuarter = change.MicrosecondsPerQuarter;
                    _segments[_segments.Count - 1] = last;
                    continue;
                }

                _segments.Add(new Segment { Tick = change.Tick, MicrosecondsPerQuarter = change.MicrosecondsPerQuarter });
            }

            // Precompute elapsed milliseconds at the start of each segment.
            for (var i = 1; i < _segments.Count; i++)
            {
                var prev = _segments[i - 1];
                var current = _segments[i];
                current.CumulativeMs = prev.CumulativeMs + TicksToMs(current.Tick - prev.Tick, prev.MicrosecondsPerQuarter);
                _segments[i] = current;
            }
        }

        public float TickToMs(int tick)
        {
            // Binary search for the last tempo segment starting at or before this tick.
            var lo = 0;
            var hi = _segments.Count - 1;
            var index = 0;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (_segments[mid].Tick <= tick)
                {
                    index = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            var segment = _segments[index];
            return (float)(segment.CumulativeMs + TicksToMs(tick - segment.Tick, segment.MicrosecondsPerQuarter));
        }

        private double TicksToMs(int ticks, int microsecondsPerQuarter)
        {
            return (double)ticks * microsecondsPerQuarter / _ticksPerQuarterNote / 1000.0;
        }
    }
}
