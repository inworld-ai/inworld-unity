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
    public abstract class InworldAudioModule : MonoBehaviour
    {
        InworldAudioCapture m_Capture;

        public InworldAudioCapture Audio
        {
            get
            {
                if (m_Capture != null)
                    return m_Capture;
                m_Capture = InworldController.Audio;
                return m_Capture;
            }
        }
    }
    public class InworldModuleException : InworldException
    {
        public InworldModuleException(string moduleName) : base($"Module {moduleName} not found")
        {
        }
    }
    public interface IMicrophoneHandler
    {
        bool IsMicRecording {get;}
        bool StartMicrophone();
        bool ChangeInputDevice(string deviceName);
        bool StopMicrophone();
    }

    public interface ICollectAudioHandler
    {
        int OnCollectAudio();
        void ResetPointer();
    }

    public interface IProcessAudioHandler
    {
        bool OnProcessAudio();
    }

    public interface ISendAudioHandler
    {
        bool OnSendAudio();
    }
}