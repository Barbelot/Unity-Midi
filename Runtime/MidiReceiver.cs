using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Midi
{
    public class MidiReceiver : MonoBehaviour
    {
        #region Types

        public enum StatusByte : byte
        {
            NoteOff         = 0x8,
            NoteOn          = 0x9,
            PolyAftertouch  = 0xA,
            ControlChange   = 0xB,
            ProgramChange   = 0xC,
            ChannelPressure = 0xD,
            PitchBend       = 0xE,
        }

        public enum PortSelectionMode { All, ByIndex }

        [Serializable]
        public class MidiMessage
        {
            public StatusByte Status;
            public byte Channel;
            public byte Data1;
            public byte Data2;

            public bool IsNoteOn  => Status == StatusByte.NoteOn  && Data2 > 0;
            public bool IsNoteOff => Status == StatusByte.NoteOff || (Status == StatusByte.NoteOn && Data2 == 0);

            public override string ToString()
                => $"{Status} ch{Channel + 1} d1={Data1} d2={Data2}";
        }

        #endregion

        #region Serialized fields

        [Header("Port")]
        public PortSelectionMode portSelectionMode = PortSelectionMode.All;
        [Tooltip("Used when Port Selection Mode is ByIndex.")]
        public List<int> portIndices = new() { 0 };

        [Header("Debug")]
        public bool displayDebugUi;
        public bool showLogs;
        [SerializeField] private int messageCount;

        [Header("Events")]
        public UnityEvent<MidiMessage> OnMessageReceived;
        public UnityEvent<MidiMessage> OnNoteOn;
        public UnityEvent<MidiMessage> OnNoteOff;
        public UnityEvent<MidiMessage> OnControlChange;
        public UnityEvent<MidiMessage> OnPitchBend;

        #endregion

        #region Private fields

        private RtMidi.MidiIn _probe;
        private readonly List<(RtMidi.MidiIn dev, string name)> _ports = new();
        private readonly byte[] _messageBuffer = new byte[32];
        private int _lastProbePortCount = -1;

        #endregion

        #region MonoBehaviour

        private void Start()
        {
            _probe = RtMidi.MidiIn.Create();
        }

        private void Update()
        {
            int probeCount = _probe.PortCount;
            if (probeCount != _lastProbePortCount)
            {
                _lastProbePortCount = probeCount;
                CloseAllPorts();
                OpenSelectedPorts();
            }

            PollAllPorts();
        }

        private void OnDestroy()
        {
            CloseAllPorts();
            _probe?.Dispose();
        }

        #endregion

        #region Port management

        private void OpenSelectedPorts()
        {
            int total = _probe.PortCount;

            for (int i = 0; i < total; i++)
            {
                if (portSelectionMode == PortSelectionMode.ByIndex && !portIndices.Contains(i))
                    continue;

                string name = _probe.GetPortName(i);
                var dev = RtMidi.MidiIn.Create();
                dev.OpenPort(i);
                _ports.Add((dev, name));

                if (showLogs)
                    Debug.Log($"[MidiReceiver] Opened port {i}: {name}");
            }
        }

        private void CloseAllPorts()
        {
            foreach (var p in _ports) p.dev?.Dispose();
            _ports.Clear();
        }

        #endregion

        #region Polling

        private void PollAllPorts()
        {
            foreach (var p in _ports)
            {
                if (p.dev == null) continue;

                Span<byte> buffer = _messageBuffer;
                while (true)
                {
                    var read = p.dev.GetMessage(buffer, out _);
                    if (read.Length == 0) break;
                    ParseAndDispatch(read);
                }
            }
        }

        #endregion

        #region Parsing & dispatch

        private void ParseAndDispatch(ReadOnlySpan<byte> data)
        {
            byte rawStatus = (byte)(data[0] >> 4);
            byte channel   = (byte)(data[0] & 0x0F);

            if (!Enum.IsDefined(typeof(StatusByte), rawStatus))
                return;

            var msg = new MidiMessage
            {
                Status  = (StatusByte)rawStatus,
                Channel = channel,
                Data1   = data.Length > 1 ? data[1] : (byte)0,
                Data2   = data.Length > 2 ? data[2] : (byte)0,
            };

            messageCount++;

            if (showLogs)
                Debug.Log($"[MidiReceiver] {msg}");

            OnMessageReceived?.Invoke(msg);

            switch (msg.Status)
            {
                case StatusByte.NoteOn when msg.Data2 > 0:
                    OnNoteOn?.Invoke(msg);
                    break;

                case StatusByte.NoteOff:
                case StatusByte.NoteOn when msg.Data2 == 0:
                    OnNoteOff?.Invoke(msg);
                    break;

                case StatusByte.ControlChange:
                    OnControlChange?.Invoke(msg);
                    break;

                case StatusByte.PitchBend:
                    OnPitchBend?.Invoke(msg);
                    break;
            }
        }

        #endregion

        #region Debug GUI

        private void OnGUI()
        {
            if (!displayDebugUi) return;

            string portList = string.Empty;
            for (int i = 0; i < _ports.Count; i++)
                portList += $"\n  [{i}] {_ports[i].name}";

            string text = $"MidiReceiver ({_ports.Count} port(s)){portList}\n"
                        + $"Messages received: {messageCount}";

            GUI.Label(new Rect(10, 10, 500, 200), text);
        }

        #endregion
    }
}
