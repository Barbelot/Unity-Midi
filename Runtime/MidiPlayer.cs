using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace Midi
{
    public class MidiPlayer : MonoBehaviour
    {
        public enum TimeControlType { Manual, GameTime, AudioSource, Timeline }

        public MidiFile midiAsset;

        [Header("Time")]
        public float startTime = 0;
        public TimeControlType timeControlType;
        [Space]
        public float manualTime;
        public AudioSource audioSource;
        public PlayableDirector playableDirector;

        [Header("Volume")]
        public bool updateVolumeEveryFrame = false;
        [Tooltip("Normalize volume by track max velocity rather than max possible velocity (127).")] 
        public bool normalizeVolumePerTrack = true;
        public AnimationCurve volumeOverNoteCurve = AnimationCurve.Linear(0, 1, 1, 0);

        [Header("Debug")]
        public bool displayDebugUi;
        public bool displayDebugBlocks = true;
        [Space]
        [Tooltip("To get the time of the current frame, use the GetCurrentTime() method.")]
        [SerializeField] private float time;
        [Tooltip("To get the volume of the current frame, use the GetCurrentVolume() method.")]
        [SerializeField][Range(0, 6)] private float volume;
        public MidiData.MidiBlock nextBlock;

        public readonly List<TrackProgress> ActiveTracks = new List<TrackProgress>();
        
        public UnityEvent<MidiData.MidiBlock> OnBlockStarted; 
        public UnityEvent<MidiData.MidiBlock> OnBlockCompleted;


        private int timeFrame = -1;
        private int volumeFrame = -1;

        public class TrackProgress
        {
            public MidiData.MidiTrack Track;
            public List<MidiData.MidiBlock> ActiveBlocks = new List<MidiData.MidiBlock>();
            public int CurrentActiveBlockIndex = -1;
        }
        
        private void Awake()
        {
            ActiveTracks.Clear();
            foreach (var track in midiAsset.Data.Tracks)
            {
                // if (track.Blocks.Count > 0)
                {
                    ActiveTracks.Add(new TrackProgress
                    {
                        Track = track
                    });
                }
            }
        }

        private void Update()
        {
            UpdateTime();
            ReadMidiDataAtCurrentTime();

            if (updateVolumeEveryFrame)
                UpdateVolume();
        }

        public float GetCurrentTime()
        {
            UpdateTime();
            return time;
        }

        void UpdateTime()
        {
            //Avoid computing time more than once per frame
            if (timeFrame == Time.frameCount)
                return;

            switch (timeControlType)    
            {
                case TimeControlType.Manual:
                    time = manualTime;
                    break;
                case TimeControlType.GameTime:
                    time = Time.time - startTime;
                    break;
                case TimeControlType.AudioSource:
                    time = audioSource.time;
                    break;
                case TimeControlType.Timeline:
                    time = (float)playableDirector.time - startTime;
                    break;
            }

            timeFrame = Time.frameCount;
        }

        public float GetCurrentVolume()
        {
            UpdateVolume();
            return volume;
        }

        void UpdateVolume()
        {
            //Avoid computing volume more than once per frame
            if (volumeFrame == Time.frameCount)
                return;

            UpdateTime();

            //Get volume of each active block and keep the maximum
            volume = 0;

            foreach (var trackData in ActiveTracks)
            {
                foreach(var block in trackData.ActiveBlocks)
                {
                    float blockPercent = (time - block.StartTimeSec) / (block.EndTimeSec - block.StartTimeSec);
                    float blockVolume = volumeOverNoteCurve.Evaluate(blockPercent);
                    blockVolume *= normalizeVolumePerTrack ? block.NormalizedVelocity : (block.Velocity / 127.0f);

                    volume += blockVolume;
                }
            }
        }

        void ReadMidiDataAtCurrentTime()
        {
            // iterate all tracks
            foreach (var trackData in ActiveTracks)
            {
                /* Using Active Block Index */
                
                int currentActiveBlockIndex = trackData.CurrentActiveBlockIndex;

                if (trackData.CurrentActiveBlockIndex < 0 || trackData.Track.Blocks[currentActiveBlockIndex].StartTimeSec < time)
                {
                    //Search forward
                    while (currentActiveBlockIndex < trackData.Track.Blocks.Count - 1)
                    {
                        currentActiveBlockIndex++;

                        var nextBlock = trackData.Track.Blocks[currentActiveBlockIndex];

                        //If we are after block start
                        if (time >= nextBlock.StartTimeSec)
                        {

                            //If we are before block end so during block
                            if (time < nextBlock.EndTimeSec)
                            {
                                // add block to the list
                                trackData.ActiveBlocks.Add(nextBlock);
                                OnBlockStart(nextBlock);
                            }

                            trackData.CurrentActiveBlockIndex = currentActiveBlockIndex;
                        }
                        else
                        {
                            break;
                        }
                    }

                } else
                {
                    //Search backward
                    while (currentActiveBlockIndex > -1)
                    {
                        currentActiveBlockIndex--;

                        if(currentActiveBlockIndex == -1)
                        {
                            trackData.CurrentActiveBlockIndex = currentActiveBlockIndex;
                            break;
                        }

                        var nextBlock = trackData.Track.Blocks[currentActiveBlockIndex];

                        //If we are after block start
                        if (time >= nextBlock.StartTimeSec)
                        {
                            trackData.ActiveBlocks.Add(nextBlock);
                            OnBlockStart(nextBlock);
                            trackData.CurrentActiveBlockIndex = currentActiveBlockIndex;
                            break;
                        }
                    }
                }
                

                /* Brute force search of all blocks */

                //for(int i=0; i < trackData.Track.Blocks.Count; i++)
                //{
                //    var block = trackData.Track.Blocks[i];
                //    if (!trackData.ActiveBlocks.Contains(block) && time >= block.StartTimeSec && time < block.EndTimeSec)
                //    {
                //        trackData.ActiveBlocks.Add(block);
                //        OnBlockStart(block);
                //    }
                //}


                // check if any active block has finished
                for (int i = 0; i < trackData.ActiveBlocks.Count; i++)
                {
                    var block = trackData.ActiveBlocks[i];
                    if (time >= block.EndTimeSec || time < block.StartTimeSec)
                    {
                        // remove finished block from the list
                        trackData.ActiveBlocks.Remove(block);
                        i--;
                        OnBlockEnd(block);
                    }
                }
            }
        }

        private void OnBlockStart(MidiData.MidiBlock block)
        {
            OnBlockStarted?.Invoke(block);
        }
        
        private void OnBlockEnd(MidiData.MidiBlock block)
        {
            OnBlockCompleted?.Invoke(block);
        }
        
        void OnGUI()
        {
            if (!displayDebugUi)
                return;

            var text = $"MIDI {midiAsset.name} playing ({time:0.##}s)\n";
            for (var index = 0; index < ActiveTracks.Count; index++)
            {
                var trackData = ActiveTracks[index];
                text += $"track {index} - ({trackData.ActiveBlocks.Count} active blocks)\n";
            }

            GUI.Label(new Rect(10, 10, 400, 400), text);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!displayDebugBlocks || !midiAsset || !Application.isPlaying)
                return;

            var heightOffset = 10;

            var totalHeight = 10;
            foreach (var track in midiAsset.Data.Tracks)
            {
                totalHeight += track.MaxNote - track.MinNote + 10;
            }

            for (var trackIndex = 0; trackIndex < midiAsset.Data.Tracks.Count; trackIndex++)
            {
                var track = midiAsset.Data.Tracks[trackIndex];
                var activeTrackData = ActiveTracks[trackIndex];

                for (var blockIndex = 0; blockIndex < track.Blocks.Count; blockIndex++)
                {
                    var block = track.Blocks[blockIndex];

                    bool isActive = false;

                    if(time > block.EndTimeSec)
                    {
                        Color color = Color.gray * block.Velocity / 127f;
                        color.a = 1;
                        Gizmos.color = color;
                    } else if(time < block.StartTimeSec)
                    {
                        Color color = Color.white * block.Velocity / 127f;
                        color.a = 1;
                        Gizmos.color = color;
                    } else
                    {
                        Color color = Color.green * block.Velocity / 127f;
                        color.a = 1;
                        Gizmos.color = color;
                        isActive = true;
                    }

                    //if (activeTrackData.CurrentBlockIndex >= blockIndex)
                    //{
                    //    var blockInProgress = block.EndTimeSec > time;
                    //    Gizmos.color = blockInProgress? Color.blue : Color.green;
                    //}
                    //else
                    //{
                    //    Gizmos.color = Color.white;
                    //}

                    var height = track.MaxNote - block.Note + heightOffset;

                    var center = block.StartTimeSec + block.LengthSec / 2f;
                    Gizmos.DrawCube(new Vector3(center, -height + totalHeight, 0), new Vector3(block.LengthSec, 1, 1));

                    if (isActive)
                    {
                        DrawString("[Id "+blockIndex.ToString()+"][N "+block.Note.ToString()+"][V "+block.Velocity.ToString()+"]", new Vector3(center, -height + totalHeight, 0), Color.red);

                    }
                }

                heightOffset += track.MaxNote - track.MinNote + 10;
            }

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(new Vector3(time, 0), new Vector3(time, 127));
        }

        public void DrawString(string text, Vector3 worldPos, Color? textColor = null, Color? backColor = null)
        {
            UnityEditor.Handles.BeginGUI();
            var restoreTextColor = GUI.color;
            var restoreBackColor = GUI.backgroundColor;

            GUI.color = textColor ?? Color.white;
            GUI.backgroundColor = backColor ?? Color.black;

            var view = UnityEditor.SceneView.currentDrawingSceneView;
            if (view != null && view.camera != null)
            {
                Vector3 screenPos = view.camera.WorldToScreenPoint(worldPos);
                if (screenPos.y < 0 || screenPos.y > Screen.height || screenPos.x < 0 || screenPos.x > Screen.width || screenPos.z < 0)
                {
                    GUI.color = restoreTextColor;
                    UnityEditor.Handles.EndGUI();
                    return;
                }
                Vector2 size = GUI.skin.label.CalcSize(new GUIContent(text));
                //var r = new Rect(screenPos.x - (size.x / 2), -screenPos.y + view.position.height + 4, size.x, size.y);
                var r = new Rect(screenPos.x - (size.x / 2), -screenPos.y + view.position.height, size.x, size.y);
                GUI.Box(r, text, EditorStyles.numberField);
                GUI.Label(r, text);
                GUI.color = restoreTextColor;
                GUI.backgroundColor = restoreBackColor;
            }
            UnityEditor.Handles.EndGUI();
        }

#endif
    }
}