using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Midi
{
    public class MidiSimulator : MonoBehaviour
    {
        [Serializable]
        public struct KeyBinding
        {
            public Key key;
            public byte noteNumber;
        }

        public byte defaultVelocity = 100;
        public byte channel = 0;
        public List<KeyBinding> keyBindings = new();

        public UnityEvent<MidiReceiver.MidiMessage> OnNoteOn;
        public UnityEvent<MidiReceiver.MidiMessage> OnNoteOff;

        void Reset()
        {
            defaultVelocity = 100;
            channel = 0;
            keyBindings = new List<KeyBinding>
            {
                new() { key = Key.A, noteNumber = 60 }, // C4
                new() { key = Key.W, noteNumber = 61 }, // C#4
                new() { key = Key.S, noteNumber = 62 }, // D4
                new() { key = Key.E, noteNumber = 63 }, // D#4
                new() { key = Key.D, noteNumber = 64 }, // E4
                new() { key = Key.F, noteNumber = 65 }, // F4
                new() { key = Key.T, noteNumber = 66 }, // F#4
                new() { key = Key.G, noteNumber = 67 }, // G4
                new() { key = Key.Y, noteNumber = 68 }, // G#4
                new() { key = Key.H, noteNumber = 69 }, // A4
                new() { key = Key.U, noteNumber = 70 }, // A#4
                new() { key = Key.J, noteNumber = 71 }, // B4
                new() { key = Key.K, noteNumber = 72 }, // C5
                new() { key = Key.O, noteNumber = 73 }, // C#5
                new() { key = Key.L, noteNumber = 74 }, // D5
            };
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            foreach (var binding in keyBindings)
            {
                var keyControl = keyboard[binding.key];
                if (keyControl.wasPressedThisFrame)  FireNoteOn(binding.noteNumber);
                if (keyControl.wasReleasedThisFrame) FireNoteOff(binding.noteNumber);
            }
        }

        private void FireNoteOn(byte note)
        {
            var msg = new MidiReceiver.MidiMessage
            {
                Status  = MidiReceiver.StatusByte.NoteOn,
                Channel = channel,
                Data1   = note,
                Data2   = defaultVelocity,
            };
            OnNoteOn?.Invoke(msg);
        }

        private void FireNoteOff(byte note)
        {
            var msg = new MidiReceiver.MidiMessage
            {
                Status  = MidiReceiver.StatusByte.NoteOff,
                Channel = channel,
                Data1   = note,
                Data2   = 0,
            };
            OnNoteOff?.Invoke(msg);
        }
    }
}
