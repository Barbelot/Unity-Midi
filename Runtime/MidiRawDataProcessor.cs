using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Midi
{
    public class MidiRawDataProcessor
    {
        public readonly List<MidiData.MidiBlock> allBlocks = new List<MidiData.MidiBlock>();
        public readonly List<MidiData.MidiTrack> tracks;
        private readonly List<Dictionary<byte, NoteOnEvent>> _noteTimeMap = new List<Dictionary<byte, NoteOnEvent>>();
        public byte Bpm { get; private set; } = 120;
        
        private struct NoteOnEvent
        {
            public float StartTimeMs;
            public byte Velocity;
            public int Count;

            public bool Equals(NoteOnEvent other)
            {
                return StartTimeMs == other.StartTimeMs;
            }

            public override int GetHashCode()
            {
                return StartTimeMs.GetHashCode();
            }
        }

        public MidiRawDataProcessor(ParsedMidiFile midiFile, MidiImportSettings midiImportSettings)
        {
            tracks = new List<MidiData.MidiTrack>(midiFile.TracksCount);
            for (var i = 0; i < midiFile.TracksCount; i++)
            {
                tracks.Add(new MidiData.MidiTrack());
                _noteTimeMap.Add(new Dictionary<byte, NoteOnEvent>());
            }

            // Build a tempo map up-front so every tick is converted with the tempo actually in
            // effect at that point. A single global BPM shifts every note in files that contain
            // tempo changes (e.g. a fast count-in measure followed by the song tempo).
            var tempoChanges = new List<MidiTempoMap.TempoChange>();
            if (midiImportSettings.OverrideBpm)
            {
                var microsecondsPerQuarter = 60000000 / Mathf.Max(1, (int)midiImportSettings.Bpm);
                tempoChanges.Add(new MidiTempoMap.TempoChange(0, microsecondsPerQuarter));
            }
            else
            {
                foreach (var track in midiFile.Tracks)
                {
                    foreach (var midiEvent in track.MidiEvents)
                    {
                        if (midiEvent.MidiEventType == MidiRawData.MidiEventType.MetaEvent
                            && midiEvent.MetaEventType == MidiRawData.MetaEventType.Tempo)
                        {
                            tempoChanges.Add(new MidiTempoMap.TempoChange(midiEvent.Time, midiEvent.MicrosecondsPerQuarter));
                        }
                    }
                }
            }

            var tempoMap = new MidiTempoMap(midiFile.TicksPerQuarterNote, tempoChanges);
            Bpm = GetRepresentativeBpm(tempoChanges);

            foreach (var track in midiFile.Tracks)
            {
                var map = _noteTimeMap[track.Index];

                foreach (var midiEvent in track.MidiEvents)
                {
                    var timeMs = tempoMap.TickToMs(midiEvent.Time);
                    var note = midiEvent.Arg2;
                    
                    switch ((MidiRawData.MidiEventType)midiEvent.Type)
                    {
                        case MidiRawData.MidiEventType.NoteOff: // block end
                            // Stray/duplicate note-off with no matching open note-on: skip it
                            // rather than aborting the whole import (some MIDI files contain these).
                            if (!map.TryGetValue(note, out var noteOnEvent))
                            {
                                Debug.LogWarning($"[MIDI] NoteOff for note {note} with no matching NoteOn — skipping stray event.");
                                break;
                            }

                            // create new block
                            var block = new MidiData.MidiBlock
                            {
                                StartTimeMs = noteOnEvent.StartTimeMs,
                                EndTimeMs = timeMs,
                                Note = note,
                                Velocity = noteOnEvent.Velocity
                            };

                            tracks[track.Index].AddBlock(block);

                            if (noteOnEvent.Count == 1)
                            {
                                map.Remove(note);
                            }
                            else
                            {
                                map[note] = new NoteOnEvent
                                {
                                    Count = noteOnEvent.Count - 1,
                                    Velocity = noteOnEvent.Velocity,
                                    StartTimeMs = noteOnEvent.StartTimeMs
                                };
                            }

                            allBlocks.Add(block);
                            break;
                        case MidiRawData.MidiEventType.NoteOn:  // block start
                            if (map.TryGetValue(note, out var existingNoteOnEvent))
                            {
                                map[note] = new NoteOnEvent
                                {
                                    StartTimeMs = timeMs,
                                    Velocity = midiEvent.Arg3,
                                    Count = existingNoteOnEvent.Count + 1
                                };
                            }
                            else
                            {
                                map.Add(note, new NoteOnEvent
                                {
                                    StartTimeMs = timeMs,
                                    Velocity = midiEvent.Arg3,
                                    Count = 1
                                });
                            }
                            
                            break;
                        case MidiRawData.MidiEventType.KeyAfterTouch:
                            break;
                        case MidiRawData.MidiEventType.ControlChange:
                            break;
                        case MidiRawData.MidiEventType.ProgramChange:
                            break;
                        case MidiRawData.MidiEventType.ChannelAfterTouch:
                            break;
                        case MidiRawData.MidiEventType.PitchBendChange:
                            break;
                        case MidiRawData.MidiEventType.MetaEvent:
                            switch (midiEvent.MetaEventType)
                            {
                                case MidiRawData.MetaEventType.Tempo:
                                    // Tempo is resolved through the tempo map built before this loop;
                                    // nothing to do per-event here.
                                    break;
                                case MidiRawData.MetaEventType.TimeSignature:
                                    break;
                                case MidiRawData.MetaEventType.KeySignature:
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            foreach (var track in tracks)
            {
                if (track.Blocks.Count == 0)
                {
                    track.MinNote = track.MaxNote = 0;
                    track.MinVelocity = track.MaxVelocity = 0;
                }
            }

            //Sort track blocks by ascending start time
            foreach (var track in tracks)
            {
                if(track.Blocks.Count > 1)
                {
                    track.Blocks = track.Blocks.OrderBy(x => x.StartTimeMs).ToList();

                    //Assign block index and normalized values
                    for(int i=0; i<track.Blocks.Count; i++)
                    {
                        track.Blocks[i].Index = i;
                        track.Blocks[i].NormalizedNote = Mathf.InverseLerp(track.MinNote, track.MaxNote, track.Blocks[i].Note);
                        track.Blocks[i].NormalizedVelocity = Mathf.InverseLerp(track.MinVelocity, track.MaxVelocity, track.Blocks[i].Velocity);
                    }
                }
            }
        }

        // Representative BPM for display/back-compat (MidiData.Bpm). Uses the most frequently
        // occurring tempo so one-off spikes (such as a fast count-in measure) don't skew it.
        private static byte GetRepresentativeBpm(List<MidiTempoMap.TempoChange> tempoChanges)
        {
            if (tempoChanges.Count == 0)
            {
                return 120;
            }

            var counts = new Dictionary<int, int>();
            var bestMicrosecondsPerQuarter = 0;
            var bestCount = 0;
            foreach (var change in tempoChanges)
            {
                if (change.MicrosecondsPerQuarter <= 0)
                {
                    continue;
                }

                counts.TryGetValue(change.MicrosecondsPerQuarter, out var count);
                count++;
                counts[change.MicrosecondsPerQuarter] = count;

                if (count > bestCount)
                {
                    bestCount = count;
                    bestMicrosecondsPerQuarter = change.MicrosecondsPerQuarter;
                }
            }

            if (bestMicrosecondsPerQuarter <= 0)
            {
                return 120;
            }

            var bpm = Mathf.RoundToInt(60000000f / bestMicrosecondsPerQuarter);
            return (byte)Mathf.Clamp(bpm, 0, 255);
        }
    }
}