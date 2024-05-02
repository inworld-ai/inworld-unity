﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.Packet;
using System;
using System.Collections.Generic;
using UnityEngine.Scripting;


namespace Inworld.Entities
{
    [Serializable]
    public class AudioChunk
    {
        public string targetName; //YAN: character's full name or conversation's conversation ID.
        public string chunk;
    }
    [Serializable]
    public class AudioSessionInfo
    {
        public string audioSessionID;
        public List<string> currentBrainNames = new List<string>();
        public List<string> lastBrainNames = new List<string>();
        /// <summary>
        /// Gets if the current live session has any characters.
        /// </summary>
        public bool IsLive => currentBrainNames.Count > 0;

        /// <summary>
        /// Stops the current audio session.
        /// </summary>
        public void StopAudio()
        {
            InworldController.Client.StopAudioTo(currentBrainNames); 
            lastBrainNames = currentBrainNames;
            currentBrainNames.Clear();
        }
        /// <summary>
        /// Starts a new audio session.
        /// </summary>
        /// <param name="characterBrainNames">The brain names of the characters to enable audio interaction</param>
        public void StartAudio(List<string> characterBrainNames)
        {
            if (characterBrainNames.Count == 0)
                return;
            StopAudio();
            InworldController.Client.StartAudioTo(characterBrainNames);
            currentBrainNames = characterBrainNames;
        }
    }
    [Serializable]
    public class AudioDevice
    {
        public string deviceId;
        public string kind;
        public string label;
        public string groupId;
        [Preserve] public AudioDevice() {}
        [Preserve] public AudioDevice(string deviceId, string kind, string label, string groupId)
        {
            this.deviceId = deviceId;
            this.kind = kind;
            this.label = label;
            this.groupId = groupId;
        }
    }
    [Serializable]
    public class WebGLAudioDevicesData
    {
        public List<AudioDevice> devices;
    }
    
    [Serializable]
    public class WebGLAudioDeviceCapsData
    {
        public int[] caps;
    }
}
