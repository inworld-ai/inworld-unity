/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.Entities;
using Inworld.Sample;
using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;


#if UNITY_WEBGL
using AOT;
using System.Runtime.InteropServices;
#endif

// TODO(Yan): Rewrite with new class InworldAudioCapture
namespace Inworld.Audio
{
    /// <summary>
    /// YAN: This is a global Audio Capture controller.
    ///      For each separate InworldCharacter, we use class AudioInteraction to handle audio clips.
    /// </summary>
    [Obsolete]
    public class AudioCapture : MonoBehaviour
    {
        [SerializeField] protected MicSampleMode m_SamplingMode = MicSampleMode.NO_FILTER;
        [Range(0, 30)][SerializeField] protected float m_PlayerVolumeThreshold = 10f;
        [Range(0.3f, 2f)][SerializeField] protected float m_CaptureCheckingDuration = 0.5f;
        // Yan: The max bufferSec (2) is only for sampleRate 16k * 1. Any sample rate larger than that will exceed the limit.
        [Range(0.1f, 2f)][SerializeField] protected int m_BufferSeconds = 1;
        protected const int k_InputSampleRate = 16000; // Yan: We should not support input sample rate larger than 16k.
        [SerializeField] protected int m_AudioToPushCapacity = 100;
        [SerializeField] protected string m_DeviceName;
        [SerializeField] protected bool m_DetectPlayerSpeaking = true;
        [Tooltip("By checking this, client will not sample player's voice")]
        [SerializeField] protected bool m_MutePlayerMic;
        [Tooltip("By enabling testing mode, Inworld server will only send you the Text-To-Speech result, without any character's response.")]
        [SerializeField] protected bool m_TestMode;
        [Space(10)][SerializeField] protected AudioEvent m_AudioEvent;

#region Variables
        protected InputAction m_PushToTalkInputAction;
        protected float m_CharacterVolume = 1f;
        protected MicSampleMode m_PrevSampleMode;
        protected bool m_PrevDetectPlayerSpeaking;
        protected const int k_SizeofInt16 = sizeof(short);
        protected const int k_SampleRate = 16000;
        protected const int k_Channel = 1;
        protected int m_OutputSampleRate = k_SampleRate;
        protected int m_OutputChannels = k_Channel;
        protected AudioSource m_RecordingSource;
        protected IEnumerator m_AudioCoroutine;
        protected bool m_IsRecording;
        protected bool m_IsPlayerSpeaking;
        protected bool m_IsCapturing;
        protected float m_BackgroundNoise;
        protected float m_CalibratingTime;
        // Last known position in AudioClip buffer.
        protected int m_LastPosition;
        protected readonly ConcurrentQueue<AudioChunk> m_AudioToPush = new ConcurrentQueue<AudioChunk>();
        protected List<AudioDevice> m_Devices = new List<AudioDevice>();
        protected List<short> m_PlayerVolumeCheckBuffer = new List<short>();
        protected ConcurrentQueue<short> m_InputBuffer = new ConcurrentQueue<short>();
        protected float[] m_RawInput;
        protected List<short> m_ProcessedWaveData = new List<short>();
        protected float m_CapturingTimer;
        protected static int m_nPosition;
#if UNITY_WEBGL
        protected static float[] s_WebGLBuffer;
        public static bool WebGLPermission { get; set; }
#endif
#endregion
        
#region Properties
        /// <summary>
        /// Gets the recording audio source
        /// </summary>
        public AudioSource Recording
        {
            get
            {
                if (m_RecordingSource)
                    return m_RecordingSource;
                m_RecordingSource = GetComponent<AudioSource>();
                if (!m_RecordingSource)
                    m_RecordingSource = gameObject.AddComponent<AudioSource>();
                return m_RecordingSource;
            }
        }

        public bool IsMute
        {
            get => m_MutePlayerMic;
            set => m_MutePlayerMic = value;
        }

        /// <summary>
        /// Gets the event handler of AudioCapture.
        /// </summary>
        public AudioEvent Event => m_AudioEvent;

        /// <summary>
        /// Gets the global setting of the volumes (From 0 to 1). 
        /// </summary>
        public float Volume
        {
            get => m_CharacterVolume;
            set => m_CharacterVolume = value;
        }
        /// <summary>
        /// The sample mode used by the Microphone. Determines how audio input is handled and processed for interactions.
        /// </summary>
        public MicSampleMode SampleMode
        {
            get => m_SamplingMode;
            set => m_SamplingMode = value;
        }
		/// <summary>
        /// Whether the Input Action for Push-to-Talk has bindings.
        /// </summary>
        public bool IsValidPushToTalkInput => m_PushToTalkInputAction != null && m_PushToTalkInputAction.bindings.Count > 0;
		
        /// <summary>
        /// A flag to check if player is allowed to speak and without filtering
        /// </summary>
        public bool IsPlayerTurn => IsRecording || m_SamplingMode == MicSampleMode.NO_FILTER || 
            m_SamplingMode== MicSampleMode.TURN_BASED && !InworldController.CharacterHandler.IsAnyCharacterSpeaking;

        /// <summary>
        /// A flag to check if audio is available to send to server.
        ///     (Either Enable AEC or it's Player's turn to speak)
        /// </summary>
        public bool IsAudioAvailable => m_SamplingMode == MicSampleMode.AEC || IsPlayerTurn;
        /// <summary>
        /// Gets/Sets if this component is detecting player speaking automatically.
        /// </summary>
        public bool AutoDetectPlayerSpeaking
        {
            get => m_DetectPlayerSpeaking 
                   && (SampleMode != MicSampleMode.TURN_BASED || !InworldController.CharacterHandler.IsAnyCharacterSpeaking) 
                   && !IsValidPushToTalkInput; 
            set => m_DetectPlayerSpeaking = value;
        }
        /// <summary>
        /// By default, it's controlled by the Record UI button in PlayerController.
        /// Note: This status is overwritten by Push to talk Hot key.
        /// </summary>
        public bool IsRecording
        {
            get => m_IsRecording || (IsValidPushToTalkInput && m_PushToTalkInputAction.IsPressed());
            set
            {
                m_IsRecording = value;
                if (m_IsRecording)
                    m_ProcessedWaveData.Clear();
            }
        }
        /// <summary>
        /// Signifies if user is speaking based on audio amplitude and threshold.
        /// </summary>
        public bool IsPlayerSpeaking
        {
            get => m_IsPlayerSpeaking;
            protected set
            {
                if (m_IsPlayerSpeaking == value)
                    return;
                m_IsPlayerSpeaking = value;
                if (m_IsPlayerSpeaking)
                    Event.onPlayerStartSpeaking?.Invoke();
                else
                    Event.onPlayerStopSpeaking?.Invoke();
            }
        }
        /// <summary>
        /// Signifies it's currently capturing.
        /// </summary>
        public bool IsCapturing
        {
            get => m_IsCapturing;
            set
            {
                if (m_IsCapturing == value)
                    return;
                m_IsCapturing = value;
                if (m_IsCapturing)
                {
                    if (!EnableAEC)
                        m_ProcessedWaveData.Clear();
                    Event.onRecordingStart?.Invoke();
                    StartAudio();
                }
                else
                {
                    Event.onRecordingEnd?.Invoke();
                    StopAudio();
                }
            }
        }
        /// <summary>
        /// Get the background noises, including music.
        /// </summary>
        public float BackgroundNoise => m_BackgroundNoise;
        /// <summary>
        /// Get Audio Input Device Name for recording.
        /// </summary>
        public string DeviceName
        {
            get
            {
                if (string.IsNullOrEmpty(m_DeviceName))
                {
                    m_DeviceName = Devices.Count == 0 ? "" : m_Devices[0].label;
                }
                return m_DeviceName;
            }
        }

        public List<AudioDevice> Devices
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (m_Devices.Count == 0)
                {
                    m_Devices = JsonUtility.FromJson<WebGLAudioDevicesData>(WebGLGetDeviceData()).devices;
                }
                return m_Devices;
#else
                return null;
#endif
            }
        }
        /// <summary>
        /// Get if aec is enabled. The parent class by default is false.
        /// </summary>
        public virtual bool EnableAEC => false;

        /// <summary>
        /// Get if VAD is enabled. The parent class by default is false.
        /// </summary>
        public virtual bool EnableVAD => false;
#endregion

#if UNITY_WEBGL && !UNITY_EDITOR 
        public delegate void NativeCommand(string json);
        [DllImport("__Internal")] public static extern int WebGLInit(NativeCommand handler);
        [DllImport("__Internal")] public static extern int WebGLInitSamplesMemoryData(float[] array, int length);
        [DllImport("__Internal")] public static extern int WebGLIsRecording();
        [DllImport("__Internal")] public static extern string WebGLGetDeviceData();
        [DllImport("__Internal")] public static extern string WebGLGetDeviceCaps();
        [DllImport("__Internal")] public static extern int WebGLGetPosition();
        [DllImport("__Internal")] public static extern void WebGLMicStart(string deviceId, int frequency, int lengthSec);
        [DllImport("__Internal")] public static extern void WebGLMicEnd();
        [DllImport("__Internal")] public static extern void WebGLDispose();
        [DllImport("__Internal")] public static extern int WebGLIsPermitted();
#endif

#region Public Functions
        /// <summary>
        /// Change the device of microphone input.
        /// </summary>
        /// <param name="deviceName">the device name to input.</param>
        public bool ChangeInputDevice(string deviceName)
        {
            if (deviceName == m_DeviceName)
                return true;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (WebGLIsRecording() == 1)
                StopMicrophone(m_DeviceName);
#else
            if (Microphone.IsRecording(m_DeviceName))
                StopMicrophone(m_DeviceName);
#endif
            m_DeviceName = deviceName;
            if (!StartMicrophone(m_DeviceName))
                return false;
            Recalibrate();
            return true;
        }
        /// <summary>
        /// Send the audio chunk in the queue immediately to Inworld server.
        /// </summary>
        public bool PushAudioImmediate()
        {
            if (!m_AudioToPush.TryDequeue(out AudioChunk audioChunk))
                return false;
            SendAudio(audioChunk);
            return true;
        }
        /// <summary>
        /// Manually push the audio wave data to server.
        /// </summary>
        public IEnumerator PushAudio()
        {
            yield return new WaitForSeconds(1);
            foreach (AudioChunk audioData in m_AudioToPush)
            {
                SendAudio(audioData);
            }
            m_AudioToPush.Clear();
        }
        public virtual bool StopAudio()
        {
            foreach (AudioChunk audioData in m_AudioToPush)
            {
                SendAudio(audioData);
            }
            m_AudioToPush.Clear();
            return InworldController.Client.StopAudioTo();
        }
        public virtual bool StartAudio()
        {
            MicrophoneMode micMode = (SampleMode == MicSampleMode.AEC && EnableVAD) || SampleMode == MicSampleMode.PUSH_TO_TALK ? MicrophoneMode.EXPECT_AUDIO_END : MicrophoneMode.OPEN_MIC;
            UnderstandingMode understandingMode = m_TestMode ? UnderstandingMode.SPEECH_RECOGNITION_ONLY : UnderstandingMode.FULL;
            InworldCharacter character = InworldController.CharacterHandler.CurrentCharacter;
            return InworldController.Client.StartAudioTo(character ? character.BrainName : null, micMode, understandingMode);
        }
        public virtual bool SendAudio(AudioChunk chunk)
        {
            if (!InworldController.Client.Current.IsConversation && chunk.targetName != InworldController.Client.Current.Character?.brainName)
            {
                InworldController.Client.Current.Character = InworldController.CharacterHandler[chunk.targetName]?.Data;
            }
            return InworldController.Client.SendAudioTo(chunk.chunk);
        }
        /// <summary>
        /// Get the audio data from the AudioListener.
        /// Need AECProbe attached to the AudioListener first.
        /// </summary>
        /// <param name="data">the output data</param>
        public virtual void GetOutputData(float[] data, int channels)
        {
            
        }
        /// <summary>
        ///     Recalculate the background noise (including bg music, etc)
        ///     Please call it whenever audio environment changed in your game.
        /// </summary>
        public virtual void Recalibrate()
        {
            m_BackgroundNoise = 0;
            m_CalibratingTime = 0;
        }

#endregion

#region MonoBehaviour Functions
        protected virtual void Awake()
        {
            m_PushToTalkInputAction = InworldAI.InputActions["PushToTalk"];
            Init();
        }
        
        protected virtual void OnEnable()
        {
            if (PlayerController.Instance)
            {
                PlayerController.Instance.onCanvasOpen.AddListener(OnPlayerCanvasOpen);
                PlayerController.Instance.onCanvasClosed.AddListener(OnPlayerCanvasClosed);
            }
            m_AudioCoroutine = AudioCoroutine();
            StartCoroutine(m_AudioCoroutine);
        }

        protected virtual void OnDisable()
        {
            if (PlayerController.Instance)
            {
                PlayerController.Instance.onCanvasOpen.RemoveListener(OnPlayerCanvasOpen);
                PlayerController.Instance.onCanvasClosed.RemoveListener(OnPlayerCanvasClosed);
            }
            if (m_AudioCoroutine != null)
                StopCoroutine(m_AudioCoroutine);
            StopMicrophone(m_DeviceName);
        }
        protected virtual void OnDestroy()
        {
            m_Devices.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLDispose();
            s_WebGLBuffer = null;
#endif
            StopMicrophone(m_DeviceName);
        }
        protected void Update()
        {
            if (m_AudioToPush.Count > m_AudioToPushCapacity)
                m_AudioToPush.TryDequeue(out AudioChunk chunk);
        }

#endregion

#region Protected Functions
        protected virtual void OnPlayerCanvasOpen()
        {
            if (!m_DetectPlayerSpeaking)
                return;
            m_PrevSampleMode = m_SamplingMode;
            m_SamplingMode = MicSampleMode.PUSH_TO_TALK;
            m_PrevDetectPlayerSpeaking = m_DetectPlayerSpeaking;
            m_DetectPlayerSpeaking = false;
        }
        protected virtual void OnPlayerCanvasClosed()
        {
            if (m_DetectPlayerSpeaking)
                return;
            m_SamplingMode = m_PrevSampleMode;
            m_DetectPlayerSpeaking = m_PrevDetectPlayerSpeaking;
        }
        protected virtual void Init()
        {
            AudioConfiguration audioSetting = AudioSettings.GetConfiguration();
            m_OutputSampleRate = audioSetting.sampleRate;
            m_OutputChannels = audioSetting.speakerMode == AudioSpeakerMode.Stereo ? 2 : 1;
            m_PrevSampleMode = m_SamplingMode;
#if UNITY_WEBGL && !UNITY_EDITOR
            s_WebGLBuffer = new float[m_BufferSeconds * k_SampleRate * k_Channel];
            WebGLInit(OnWebGLInitialized);
#endif
        }
        protected virtual void Calibrate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // YAN: Due to the time sequence and permission issue, skip calibrating.
            m_BackgroundNoise = 0.0001f; 
            return;
#endif
            if (m_CalibratingTime >= m_BufferSeconds)
                return;
            if (m_CalibratingTime == 0 && m_BackgroundNoise == 0)
                Event.onStartCalibrating?.Invoke();
            float rms = CalculateRMS();
            if (rms > m_BackgroundNoise)
                m_BackgroundNoise = rms;
            m_CalibratingTime += 0.1f;
            if (m_CalibratingTime >= m_BufferSeconds && m_BackgroundNoise != 0)
                Event.onStopCalibrating?.Invoke();
        }
        
        protected virtual IEnumerator AudioCoroutine()
        {
            while (true)
            {
                int nSize = GetAudioData();
                if (nSize > 0)
                {
                    ProcessAudio();
                    Calibrate();
                    Collect();
                    yield return OutputData();
                }
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }
        
        protected virtual void RemoveOverDueData(ref List<short> array, int sampleRate)
        {
            if (array.Count > sampleRate * m_BufferSeconds)
            {
                array.RemoveRange(0, array.Count - sampleRate * m_BufferSeconds);
            }
        }
        
        protected virtual void ProcessAudio()
        {
            m_ProcessedWaveData.AddRange(m_InputBuffer);
            m_PlayerVolumeCheckBuffer.Clear();
            m_PlayerVolumeCheckBuffer.AddRange(m_InputBuffer);
            m_InputBuffer.Clear();
            RemoveOverDueData(ref m_ProcessedWaveData, k_SampleRate);
        }
        protected virtual bool Collect()
        {
            if (m_SamplingMode == MicSampleMode.NO_MIC)
                return false;
            if (!IsRecording && !EnableVAD && m_BackgroundNoise == 0)
                return false;
            IsPlayerSpeaking = DetectPlayerSpeaking();
            if (IsRecording || IsPlayerSpeaking)
            {
                if (!EnableAEC)
                    IsCapturing = true;
                else
                {
                    m_CapturingTimer += 0.1f;
                    if (m_CapturingTimer > m_CaptureCheckingDuration)
                    {
                        m_CapturingTimer = m_CaptureCheckingDuration;
                        IsCapturing = true;
                    }
                }
            }
            else
            {
                m_CapturingTimer -= 0.1f;
                if (m_CapturingTimer < 0)
                {
                    IsCapturing = false;
                    m_CapturingTimer = 0;
                }
            }
            if (!IsCapturing)
                return false;
            string charName = InworldController.CharacterHandler.CurrentCharacter ? InworldController.CharacterHandler.CurrentCharacter.BrainName : "";
            byte[] output = Output(m_ProcessedWaveData.Count);
            m_ProcessedWaveData.Clear();
            string audioData = Convert.ToBase64String(output);
            m_AudioToPush.Enqueue(new AudioChunk
            {
                chunk = audioData,
                targetName = charName
            });
            return true;
        }
        protected virtual bool DetectPlayerSpeaking() => !IsMute && AutoDetectPlayerSpeaking && CalculateSNR() > m_PlayerVolumeThreshold;

        protected virtual IEnumerator OutputData()
        {
            if (InworldController.Client && InworldController.Client.Status == InworldConnectionStatus.Connected)
                PushAudioImmediate();
            if (m_AudioToPush.Count > m_AudioToPushCapacity)
                m_AudioToPush.TryDequeue(out AudioChunk chunk);
            yield break;
        }
        protected virtual int GetAudioData()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (WebGLIsRecording() == 0)
                StartMicrophone(m_DeviceName);
            m_nPosition = WebGLGetPosition();
#else
            if (!Microphone.IsRecording(m_DeviceName))
            {
                StartMicrophone(m_DeviceName);
            }
            m_nPosition = Microphone.GetPosition(m_DeviceName);
#endif
            if (m_nPosition < m_LastPosition)
                m_nPosition = Recording.clip.samples;
            if (m_nPosition <= m_LastPosition)
            {
                return -1;
            }
            int nSize = m_nPosition - m_LastPosition;
            
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!WebGLGetAudioData())
                return -1;
#else
            m_RawInput = new float[nSize];
            if (!Recording.clip)
                return -1;
            Recording.clip.GetData(m_RawInput, m_LastPosition);
            WavUtility.ConvertAudioClipDataToInt16Array(ref m_InputBuffer, m_RawInput, Recording.clip.frequency, Recording.clip.channels);
#endif
            m_LastPosition = m_nPosition % Recording.clip.samples;
            return nSize;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        public bool StartWebMicrophone()
        {
            if (!WebGLPermission)
                return false;
            InworldAI.Log($"Audio Input Device {DeviceName}");
            m_AudioCoroutine = AudioCoroutine();
            StartCoroutine(m_AudioCoroutine);
            return true;
        }
        protected bool WebGLGetAudioData()
        {
            if (s_WebGLBuffer == null || s_WebGLBuffer.Length == 0)
                return false;
            for (int i = m_LastPosition; i < m_nPosition; i++)
            {
                float clampedSample = Math.Max(-1.0f, Math.Min(1.0f, s_WebGLBuffer[i]));
                m_InputBuffer.Enqueue((short)(clampedSample * 32767));
            }
            return true;
        }
        [MonoPInvokeCallback(typeof(NativeCommand))]
        static void OnWebGLInitialized(string json)
        {
            try
            {
                WebGLCommand<object> command = JsonUtility.FromJson<WebGLCommandData<object>>(json).command;
                switch (command.command)
                {
                    case "PermissionChanged":
                        WebGLCommand<bool> boolCmd = JsonUtility.FromJson<WebGLCommandData<bool>>(json).command;
                        if (boolCmd.data) // Permitted.
                        {
                            WebGLPermission = true;
                            InworldController.Audio.StartWebMicrophone();
                        }
                        break;
                    case "StreamChunkReceived":
                        WebGLCommand<string> strCmd = JsonUtility.FromJson<WebGLCommandData<string>>(json).command;
                        string[] split = strCmd.data.Split(':');

                        int index = int.Parse(split[0]);
                        int length = int.Parse(split[1]);
                        int bufferLength = int.Parse(split[2]);
                        if (bufferLength == 0)
                        {
                            // Somehow the buffer will be dropped in the middle.
                            InworldAI.Log("Buffer released, reinstall");
                            WebGLInitSamplesMemoryData(s_WebGLBuffer, s_WebGLBuffer.Length); 
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                if (InworldAI.IsDebugMode)
                {
                    Debug.LogException(ex);
                }
            }
        }  
        string GetWebGLMicDeviceID(string deviceName) => m_Devices.FirstOrDefault(d => d.label == deviceName)?.deviceId;
#endif

        
        protected virtual byte[] Output(int nSize)
        {
            int nWavCount = nSize * k_SizeofInt16;
            byte[] output = new byte[nWavCount];
            Buffer.BlockCopy(m_ProcessedWaveData.ToArray(), 0, output, 0, nWavCount);
            return output;
        }
        // Root Mean Square, used to measure the variation of the noise.
        protected float CalculateRMS()
        {
            if (m_PlayerVolumeCheckBuffer == null || m_PlayerVolumeCheckBuffer.Count == 0)
                return 0;
            double nMaxSample = m_PlayerVolumeCheckBuffer.Aggregate<short, double>(0, (current, sample) => current + (float)sample / short.MaxValue * sample / short.MaxValue);
            return Mathf.Sqrt((float)nMaxSample / m_PlayerVolumeCheckBuffer.Count);
        }
        // Sound Noise Ratio (dB). Used to check how loud the input voice is.
        protected float CalculateSNR()
        {
            if (m_BackgroundNoise == 0)
                return 0;  // Need to calibrate first.
            return 20.0f * Mathf.Log10(CalculateRMS() / m_BackgroundNoise); 
        }
        

        public virtual bool StartMicrophone(string deviceName)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            deviceName = string.IsNullOrEmpty(deviceName) ? m_DeviceName : deviceName;
            string microphoneDeviceIDFromName = GetWebGLMicDeviceID(deviceName);
            if (string.IsNullOrEmpty(microphoneDeviceIDFromName))
                throw new ArgumentException("Couldn't acquire device ID for device name " + deviceName);
            if (WebGLIsRecording() == 1)
                return false;
            if (Recording.clip)
                Destroy(Recording.clip);
            Recording.clip = AudioClip.Create("Microphone", k_SampleRate * m_BufferSeconds, 1, k_SampleRate, false);
            if (s_WebGLBuffer == null || s_WebGLBuffer.Length == 0)
                s_WebGLBuffer = new float[k_SampleRate];
            WebGLInitSamplesMemoryData(s_WebGLBuffer, s_WebGLBuffer.Length);
            WebGLMicStart(microphoneDeviceIDFromName, k_SampleRate, m_BufferSeconds);
            m_LastPosition = 0;
            return true;
#else
            Microphone.GetDeviceCaps(deviceName, out int minFreq, out int maxFreq);
            // if minFreq == 0 and maxFreq == 0 then the device supports any frequency
            Recording.clip = Microphone.Start(deviceName, true, m_BufferSeconds, k_InputSampleRate);
            m_LastPosition = 0;
            return Recording.clip;
#endif
        }
        protected virtual void StopMicrophone(string deviceName)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLMicEnd();
            m_InputBuffer.Clear();
#else
            Microphone.End(deviceName);
#endif
        }
#endregion
    }
}
