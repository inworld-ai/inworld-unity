/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using System;
using System.Linq;
using UnityEngine;

namespace Inworld.Audio
{
    public class PlayerVoiceDetector : InworldAudioModule, ICalibrateAudioHandler
    {
        [SerializeField] [Range(0.1f, 1f)] float m_BufferSeconds = 0.5f; 
        protected float m_BackgroundNoise;
        protected float m_CalibratingTime;
        public void OnStartCalibration()
        {
            Audio.IsCalibrating = true;
        }

        public void OnStopCalibration()
        {
            Audio.IsCalibrating = false;
        }

        void FixedUpdate()
        {
            if (!Audio.IsCalibrating)
                return;
            OnCalibrate();
        }

        public void OnCalibrate()
        {
            float rms = CalculateRMS();
            if (rms > m_BackgroundNoise)
                m_BackgroundNoise = rms;
            m_CalibratingTime += Time.fixedUnscaledDeltaTime;
            if (m_CalibratingTime >= m_BufferSeconds)
                OnStopCalibration();
        }
        
        // Root Mean Square, used to measure the variation of the noise.
        protected float CalculateRMS()
        {
            double nMaxSample = Audio.ProcessedWaveData.Where(f => f > 0).Aggregate<short, double>(0, (current, sample) => current + (float)sample / short.MaxValue * sample / short.MaxValue);
            return Mathf.Sqrt((float)nMaxSample / Audio.ProcessedWaveData.Count);
        }
    }
}