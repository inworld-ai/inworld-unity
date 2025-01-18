﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using System;
using UnityEngine;

namespace Inworld.Audio
{
    /// <summary>
    /// The Base Module for controlling Microphone system.
    /// Please ensure there is ONLY one in the module list.
    ///
    /// The Start/Stop Mic will turn on/off the AudioCoroutine.
    /// Which will be separate from the IsRecording, which will control pushing data actually.
    /// Please seldom use these functions unless very necessarily.
    /// </summary>
    public class AudioCaptureModule : InworldAudioModule, IMicrophoneHandler
    {
        [SerializeField] bool m_AutoStart = true;
        protected const int k_InputSampleRate = 16000;
        protected const int k_InputChannels = 1;
        protected const int k_InputBufferSecond = 1;

        void Start()
        {
            if (m_AutoStart && !IsMicRecording)
            {
                if (StartMicrophone())
                    Audio.StartCalibrate();
            }
        }

        public virtual bool StartMicrophone()
        {
            InworldAI.LogWarning("Starting Microphone");
            Audio.RecordingClip = Microphone.Start(Audio.DeviceName, true, k_InputBufferSecond, k_InputSampleRate);
            Audio.ResetPointer();
            Audio.StartAudioThread();
            return Audio.RecordingClip;
        }
        public virtual bool ChangeInputDevice(string deviceName)
        {
            InworldAI.LogWarning($"Changing Microphone to {deviceName}");
            if (deviceName == Audio.DeviceName)
                return true;

            if (IsMicRecording)
                StopMicrophone();

            Audio.DeviceName = deviceName;
            if (!StartMicrophone())
                return false;
            Audio.StartCalibrate();
            return true;
        }
        public virtual bool StopMicrophone()
        {
            InworldAI.LogWarning("Ending Microphone");
            Microphone.End(Audio.DeviceName);
            Audio.InputBuffer.Clear();
            Audio.ResetPointer();
            Audio.StopAudioThread();
            return true;
        }
        public virtual bool IsMicRecording => Microphone.IsRecording(Audio.DeviceName);
    }
}