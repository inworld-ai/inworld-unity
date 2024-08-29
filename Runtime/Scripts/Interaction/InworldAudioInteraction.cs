/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.Packet;
using UnityEngine;
using System.Collections;
using System.Linq;

namespace Inworld.Interactions
{
    [RequireComponent(typeof(AudioSource))]
    public class InworldAudioInteraction : InworldInteraction
    {
        [Range (0, 1)][SerializeField] protected float m_VolumeOnPlayerSpeaking = 1f;

        float m_WaitTimer;
        const string k_NoAudioCapabilities = "Audio Capabilities have been disabled in the Inworld AI object. Audio is required to be enabled when using the InworldAudioInteraction component.";
        const float k_WaitTime = 2f;
        public override float AnimFactor => m_PlaybackSource ? m_PlaybackSource.time : base.AnimFactor;

        /// <summary>
        /// Mute/Unmute this character.
        /// </summary>
        public bool IsMute
        {
            get => m_PlaybackSource == null || !m_PlaybackSource.enabled || m_PlaybackSource.mute;
            set
            {
                if (m_PlaybackSource)
                    m_PlaybackSource.mute = value;
            }
        }
        protected override void OnPlayerStartSpeaking()
        {
            if (!m_PlaybackSource || !InworldController.Audio || !InworldController.Audio.EnableVAD)
                return;
            m_PlaybackSource.Pause();
        }
        protected override void OnPlayerStopSpeaking()
        {
            if (!m_PlaybackSource || !InworldController.Audio || !InworldController.Audio.EnableVAD)
                return;
            if (m_PlaybackSource.time == 0)
                m_PlaybackSource.Play();
            else
                m_PlaybackSource.UnPause();
        }
        /// <summary>
        /// Interrupt this character by cancelling its incoming responses.
        /// </summary>
        public override bool CancelResponse(bool isHardCancelling = true)
        {
            if (!base.CancelResponse(isHardCancelling))
                return false;
            if (m_Interruptable)
            {
                m_PlaybackSource.clip = null;
                m_PlaybackSource.Stop();
            }
                
            m_WaitTimer = 0;
            return true;
        }
        protected override void Awake()
        {
            base.Awake();
            m_PlaybackSource = GetComponent<AudioSource>();
            if(!m_PlaybackSource)
                m_PlaybackSource = gameObject.AddComponent<AudioSource>();
            m_PlaybackSource.playOnAwake = false;
            m_PlaybackSource.Stop();
            if (!InworldAI.Capabilities.audio)
                InworldAI.LogWarning(k_NoAudioCapabilities);
        }
        protected override IEnumerator InteractionCoroutine()
        {
            while (true)
            {
                yield return AdjustVolume();
                yield return RemoveExceedItems();
                yield return HandleNextUtterance();
                yield return null;
            }
        }
        protected override IEnumerator PlayNextUtterance()
        {
            if (!m_CurrentInteraction.CurrentUtterance.IsPlayable())
            {
                m_Character.OnInteractionChanged(m_CurrentInteraction.CurrentUtterance.Packets);
                m_CurrentInteraction.CurrentUtterance = null;
                m_WaitTimer = 0;
                yield break;
            }
            if (!m_CurrentInteraction.CurrentUtterance.ContainsTextAndAudio() && !m_CurrentInteraction.ReceivedInteractionEnd && m_WaitTimer < k_WaitTime)
            {
                m_WaitTimer += Time.unscaledDeltaTime;
                yield break;
            }
            AudioClip audioClip = m_CurrentInteraction.CurrentUtterance.GetAudioClip();
            if (audioClip == null)
            {
                m_Character.OnInteractionChanged(m_CurrentInteraction.CurrentUtterance.Packets);
                yield return new WaitForSeconds(m_CurrentInteraction.CurrentUtterance.GetTextSpeed() * m_TextSpeedMultipler);
            }
            else
            {
                if (audioClip != m_AudioClip)
                {
                    m_AudioClip = audioClip;
                    m_PlaybackSource.clip = m_AudioClip;
                    m_PlaybackSource.Play();
                }
                m_Character.OnInteractionChanged(m_CurrentInteraction.CurrentUtterance.Packets);
                yield return new WaitUntil(() => m_PlaybackSource.clip == null || m_PlaybackSource.time >= m_PlaybackSource.clip.length - Time.fixedUnscaledDeltaTime);
                m_PlaybackSource.clip = null;
            }
            if(m_CurrentInteraction != null)
                m_CurrentInteraction.CurrentUtterance = null;
            m_WaitTimer = 0;
        }
        protected override void SkipCurrentUtterance()
        {
            base.SkipCurrentUtterance();
            m_PlaybackSource.Stop();
            m_WaitTimer = 0;
        }
        protected IEnumerator AdjustVolume()
        {
            m_PlaybackSource.volume = (InworldController.Audio.IsPlayerSpeaking ? m_VolumeOnPlayerSpeaking : 1f) * InworldController.Audio.Volume;
            yield break;
        }
    }
}
