﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.Packet;
using Inworld.Entities;
using Inworld.Interactions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Inworld
{
    public class InworldClient : MonoBehaviour
    {
        [SerializeField] protected InworldServerConfig m_ServerConfig;
        [SerializeField] protected string m_SceneFullName;
        [SerializeField] protected string m_APIKey;
        [SerializeField] protected string m_APISecret;
        [SerializeField] protected string m_CustomToken;
        [SerializeField] protected string m_PublicWorkspace;
        [Tooltip("If checked, we'll automatically find the first scene belonged to the characters.")] 
        [SerializeField] protected bool m_AutoScene = false;
        [Tooltip("This is for the new previous data")] 
        [SerializeField] protected Continuation m_Continuation;
        [SerializeField] protected int m_MaxWaitingListSize = 100;
        public event Action<InworldConnectionStatus> OnStatusChanged;
        public event Action<InworldError> OnErrorReceived;
        public delegate void PacketDelegate(InworldPacket packet);

        public delegate void ErrorDelegate(InworldError error);
        
        public PacketDelegate OnPacketSent;
        public PacketDelegate OnGlobalPacketReceived;
        public PacketDelegate OnPacketReceived;

        
        const string k_NotImplemented = "No InworldClient found. Need at least one connection protocol";
        // These data will always be updated once session is refreshed and character ID is fetched. 
        // key by character's brain ID. Value contains its live session ID.
        protected readonly Dictionary<string, InworldCharacterData> m_LiveSessionData = new Dictionary<string, InworldCharacterData>();
        // The history feedback, key by interaction ID.
        protected readonly Dictionary<string, Feedback> m_Feedbacks = new Dictionary<string, Feedback>();
        protected readonly IndexQueue<OutgoingPacket> m_Prepared = new IndexQueue<OutgoingPacket>();
        protected readonly IndexQueue<OutgoingPacket> m_Sent = new IndexQueue<OutgoingPacket>();
        protected Token m_Token;
        protected IEnumerator m_OutgoingCoroutine;
        InworldConnectionStatus m_Status;
        protected InworldError m_Error;

        /// <summary>
        /// Gets the live session data.
        /// key by character's full name (aka brainName) value by its agent ID.
        /// </summary>
        public Dictionary<string, InworldCharacterData> LiveSessionData => m_LiveSessionData;
        
        /// <summary>
        /// Gets the InworldCharacterData by the given agentID.
        /// Usually used when processing packets, but don't know it's sender/receiver of characters.
        /// </summary>
        public InworldCharacterData GetCharacterDataByID(string agentID) => 
            LiveSessionData.Values.FirstOrDefault(c => !string.IsNullOrEmpty(agentID) && c.agentId == agentID);

        /// <summary>
        /// Get/Set the session history.
        /// Session History is a string
        /// </summary>
        public string SessionHistory { get; set; }
        /// <summary>
        /// Get/Set if client will automatically search for a scene for the selected characters.
        /// </summary>
        public bool AutoSceneSearch
        {
            get => m_AutoScene;
            set => m_AutoScene = value;
        }
        /// <summary>
        /// Gets/Sets the current Inworld server this client is connecting.
        /// </summary>
        public InworldServerConfig Server
        {
            get => m_ServerConfig;
            internal set => m_ServerConfig = value;
        }
        /// <summary>
        /// Gets/Sets the token used to login Runtime server of Inworld.
        /// </summary>
        public Token Token
        {
            get => m_Token;
            set => m_Token = value;
        }
        public string CurrentScene => m_SceneFullName;
        /// <summary>
        /// Gets if the current token is valid.
        /// </summary>
        public virtual bool IsTokenValid => m_Token != null && m_Token.IsValid;
        /// <summary>
        /// Gets/Sets the current status of Inworld client.
        /// If set, it'll invoke OnStatusChanged events.
        /// </summary>
        public virtual InworldConnectionStatus Status
        {
            get => m_Status;
            set
            {
                if (m_Status == value)
                    return;
                m_Status = value;
                OnStatusChanged?.Invoke(value);
            }
        }
        /// <summary>
        /// Gets/Sets the error message.
        /// If set, it'll also set the status of this client.
        /// </summary>
        public virtual string ErrorMessage
        {
            get => m_Error?.message ?? "";
            protected set
            {
                if (string.IsNullOrEmpty(value))
                {
                    m_Error = null;
                    return;
                }
                m_Error = new InworldError(value);
                InworldAI.LogError(m_Error.message);
                OnErrorReceived?.Invoke(m_Error);
            }
        }
        public virtual InworldError Error
        {
            get => m_Error;
            set
            {
                m_Error = value;
                if (m_Error == null || !m_Error.IsValid)
                {
                    return;
                }
                InworldAI.LogError(m_Error.message);
                OnErrorReceived?.Invoke(m_Error);
                if (m_Error.RetryType == ReconnectionType.NO_RETRY)
                    Status = InworldConnectionStatus.Error; 
            }
        }
        // Send Feedback data to server.
        // Implemented directly in parent class as it does not go through GRPC.
        public virtual void SendFeedbackAsync(string charFullName, Feedback feedback)
        {
            StartCoroutine(_SendFeedBack(charFullName, feedback));
        }
        IEnumerator _SendFeedBack(string charFullName, Feedback feedback)
        {
            if (string.IsNullOrEmpty(feedback.InteractionID))
            {
                InworldAI.LogError("No interaction ID for feedback");
                yield break;
            }

            if (m_Feedbacks.ContainsKey(feedback.InteractionID))
                yield return PatchFeedback(feedback); // Patch
            else
                yield return PostFeedback(feedback);
            
        }
        IEnumerator PostFeedback(Feedback feedback)
        {
            string sessionFullName = _GetSessionFullName(m_SceneFullName);
            string callbackRef = _GetCallbackReference(sessionFullName, feedback.InteractionID, feedback.CorrelationID);
            UnityWebRequest uwr = new UnityWebRequest(m_ServerConfig.FeedbackURL(callbackRef), "POST");
            uwr.SetRequestHeader("Grpc-Metadata-session-id", m_Token.sessionId);
            uwr.SetRequestHeader("Authorization", $"Bearer {m_Token.token}");
            uwr.SetRequestHeader("Content-Type", "application/json");
            uwr.downloadHandler = new DownloadHandlerBuffer();
            yield return uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                ErrorMessage = $"Error Posting feedbacks {uwr.downloadHandler.text} Error: {uwr.error}";
                yield break;
            }
            string responseJson = uwr.downloadHandler.text;
            FeedbackData feedbackData = JsonUtility.FromJson<FeedbackData>(responseJson);
            InworldAI.Log($"Post Feedback: {feedbackData.name}");
            feedback.SetCallbackReference(feedbackData.name);
            string json = JsonUtility.ToJson(feedback);
            InworldAI.Log(json);
            UnityWebRequest uwr2 = new UnityWebRequest(m_ServerConfig.FeedbackURL(callbackRef), "POST");
            uwr2.SetRequestHeader("Grpc-Metadata-session-id", m_Token.sessionId);
            uwr2.SetRequestHeader("Authorization", $"Bearer {m_Token.token}");
            uwr2.SetRequestHeader("Content-Type", "application/json");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            uwr2.uploadHandler = new UploadHandlerRaw(bodyRaw);
            uwr2.downloadHandler = new DownloadHandlerBuffer();
            yield return uwr2.SendWebRequest();
            if (uwr2.result != UnityWebRequest.Result.Success)
            {
                ErrorMessage = $"Error Posting feedbacks {uwr2.downloadHandler.text} Error: {uwr2.error}";
                yield break;
            }
            string newResponseJson = uwr2.downloadHandler.text;
            InworldAI.Log($"Updated Feedback: {newResponseJson}");
        }
        IEnumerator PatchFeedback(Feedback feedback) 
        {
            yield return PostFeedback(feedback); //TODO(Yan): Use Patch instead of Post for detailed json.
        }

        public virtual void GetHistoryAsync(string sceneFullName) => ErrorMessage = k_NotImplemented;
        public virtual void SendPackets() => ErrorMessage = k_NotImplemented; 
        /// <summary>
        /// Gets the access token. Would be implemented by child class.
        /// </summary>
        public virtual void GetAccessToken()
        {
            ErrorMessage = k_NotImplemented;
        }
        /// <summary>
        /// Reconnect session or start a new session if the current session is invalid.
        /// </summary>
        public void Reconnect() 
        {
            if (IsTokenValid)
                StartSession();
            else
                GetAccessToken();
        }
        /// <summary>
        /// Gets the live session info once load scene completed.
        /// The returned LoadSceneResponse contains the session ID and all the live session ID for each InworldCharacters in this InworldScene.
        /// </summary>
        public virtual LoadSceneResponse GetLiveSessionInfo()
        {
            ErrorMessage = k_NotImplemented;
            return null;
        }
        /// <summary>
        /// Use the input json string of token instead of API key/secret to load scene.
        /// This token can be fetched by other applications such as InworldWebSDK.
        /// </summary>
        /// <param name="token">the custom token to init.</param>
        public virtual bool InitWithCustomToken(string token)
        {
            m_Token = JsonUtility.FromJson<Token>(token);
            if (!IsTokenValid)
            {
                ErrorMessage = "Get Token Failed";
                return false;
            }
            Status = InworldConnectionStatus.Initialized;
            return true;
        }
        /// <summary>
        /// Start the session by the session ID.
        /// </summary>
        public virtual void StartSession() => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Disconnect Inworld Server.
        /// </summary>
        public virtual void Disconnect() => ErrorMessage = k_NotImplemented;
        // /// <summary>
        // /// Send LoadScene request to Inworld Server.
        // /// </summary>
        // /// <param name="sceneFullName">the full string of the scene to load.</param>
        // /// <param name="history">the full string of the encrypted history content to send.</param>
        // public virtual void LoadScene(string sceneFullName, string history = "") => Error = k_NotImplented;
        /// <summary>
        /// Send LoadScene request to Inworld Server.
        /// </summary>
        /// <param name="sceneFullName">the full string of the scene to load.</param>
        public virtual void LoadScene(string sceneFullName = "") => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Gets when packet is received.
        /// </summary>
        /// <param name="packet">incoming packet</param>
        public virtual void Enqueue(InworldPacket packet) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Send Capabilities to Inworld Server.
        /// It should be sent immediately after session started to enable all the conversations. 
        /// </summary>
        public virtual void SendCapabilities() => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Send Session Config to Inworld Server.
        /// It should be sent right after sending Capabilities. 
        /// </summary>
        public virtual void SendSessionConfig() => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Send Client Config to Inworld Server.
        /// It should be sent right after sending Session Config. 
        /// </summary>
        public virtual void SendClientConfig() => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Send User Config to Inworld Server.
        /// It should be sent right after sending Client Config. 
        /// </summary>
        public virtual void SendUserConfig() => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Send the previous dialog (New version) to specific scene.
        /// Can be supported by either previous state (base64) or previous dialog (actor: text)
        /// </summary>
        /// <param name="sceneFullName">target scene to send</param>
        public virtual void SendHistory() => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// New Send messages to an InworldCharacter in this current scene.
        /// NOTE: 1. New method uses brain ID (aka character's full name) instead of live session ID
        ///       2. New method support broadcasting to multiple characters.
        /// </summary>
        /// <param name="textToSend">the message to send.</param>
        /// <param name="characters">the list of the characters full name.</param>
        public virtual void SendTextTo(string textToSend, List<string> characters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Legacy Send messages to an InworldCharacter in this current scene.
        /// </summary>
        /// <param name="characterID">the live session ID of the single character to send</param>
        /// <param name="textToSend">the message to send.</param>
        public virtual void SendText(string characterID, string textToSend) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// New Send the CancelResponse Event to InworldServer to interrupt the character's speaking.
        /// NOTE: 1. New method uses brain ID (aka character's full name) instead of live session ID
        ///       2. New method support broadcasting to multiple characters.
        /// </summary>
        /// <param name="interactionID">the handle of the dialog context that needs to be cancelled.</param>
        /// <param name="utteranceID">the current utterance ID that needs to be cancelled.</param>
        /// <param name="characters">the full name of the characters in the scene.</param>
        public virtual void SendCancelEventTo(string interactionID, string utteranceID = "", List<string> characters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Legacy Send the CancelResponse Event to InworldServer to interrupt the character's speaking.
        /// </summary>
        /// <param name="characterID">the live session ID of the character to send</param>
        /// <param name="utteranceID">the current utterance ID that needs to be cancelled.</param>
        /// <param name="interactionID">the handle of the dialog context that needs to be cancelled.</param>
        public virtual void SendCancelEvent(string characterID, string interactionID, string utteranceID = "") => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// New Send the trigger to an InworldCharacter in the current scene.
        /// NOTE: 1. New method uses brain ID (aka character's full name) instead of live session ID
        ///       2. New method support broadcasting to multiple characters.
        /// </summary>
        /// <param name="triggerName">the name of the trigger to send.</param>
        /// <param name="parameters">the parameters and their values for the triggers.</param>
        /// <param name="characters">the full name of the characters in the scene.</param>
        public virtual void SendTriggerTo(string triggerName, Dictionary<string, string> parameters = null, List<string> characters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Legacy Send the trigger to an InworldCharacter in the current scene.
        /// </summary>
        /// <param name="charID">the live session ID of the character to send.</param>
        /// <param name="triggerName">the name of the trigger to send.</param>
        /// <param name="parameters">the parameters and their values for the triggers.</param>
        public virtual void SendTrigger(string charID, string triggerName, Dictionary<string, string> parameters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// New Send AUDIO_SESSION_START control events to server.
        /// NOTE: 1. New method uses brain ID (aka character's full name) instead of live session ID
        ///       2. New method support broadcasting to multiple characters.
        /// </summary>
        /// <param name="characters">the full name of the characters to send.</param>
        public virtual void StartAudioTo(List<string> characters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Legacy Send AUDIO_SESSION_START control events to server.
        /// Without sending this message, all the audio data would be discarded by server.
        /// However, if you send this event twice in a row, without sending `StopAudio()`, Inworld server will also through exceptions and terminate the session.
        /// </summary>
        /// <param name="charID">the live session ID of the character to send.</param>
        public virtual void StartAudio(string charID) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// New Send AUDIO_SESSION_END control events to server to.
        /// NOTE: 1. New method uses brain ID (aka character's full name) instead of live session ID
        ///       2. New method support broadcasting to multiple characters.
        /// </summary>
        /// <param name="characters">the full name of the character to send.</param>
        public virtual void StopAudioTo(List<string> characters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Legacy Send AUDIO_SESSION_END control events to server to.
        /// </summary>
        /// <param name="charID">the live session ID of the character to send.</param>
        public virtual void StopAudio(string charID) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// New Send the wav data to server to a specific character.
        /// Need to make sure that AUDIO_SESSION_START control event has been sent to server.
        /// NOTE: 1. New method uses brain ID (aka character's full name) instead of live session ID
        ///       2. New method support broadcasting to multiple characters.
        /// Only the base64 string of the wave data is supported by Inworld server.
        /// Additionally, the sample rate of the wave data has to be 16000, mono channel.
        /// </summary>
        /// <param name="base64">the base64 string of the wave data to send.</param>
        /// <param name="characters">the full name of the character to send.</param>
        public virtual void SendAudioTo(string base64, List<string> characters = null) => ErrorMessage = k_NotImplemented;
        /// <summary>
        /// Legacy Send the wav data to server to a specific character.
        /// Need to make sure that AUDIO_SESSION_START control event has been sent to server.
        ///
        /// Only the base64 string of the wave data is supported by Inworld server.
        /// Additionally, the sample rate of the wave data has to be 16000, mono channel.
        /// </summary>
        /// <param name="charID">the live session ID of the character to send.</param>
        /// <param name="base64">the base64 string of the wave data to send.</param>
        public virtual void SendAudio(string charID, string base64) => ErrorMessage = k_NotImplemented;
        
        /// <summary>
        /// Get the InworldCharacterData by characters' full name.
        /// </summary>
        /// <param name="characterFullNames">the request characters' Brain ID.</param>
        protected Dictionary<string, string> _GetCharacterDataByFullName(List<string> characterFullNames)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (characterFullNames == null || characterFullNames.Count == 0)
                return result;
            foreach (string brainID in characterFullNames)
            {
                if (m_LiveSessionData.TryGetValue(brainID, out InworldCharacterData value))
                    result[brainID] = value.agentId;
                else
                    result[brainID] = "";
            }
            return result;
        }
        protected virtual void RegisterLiveSession(LoadSceneResponse loadSceneResponse)
        {
            m_LiveSessionData.Clear();
            // YAN: Fetch all the characterData in the current session.
            foreach (InworldCharacterData agent in loadSceneResponse.agents.Where(agent => !string.IsNullOrEmpty(agent.agentId) && !string.IsNullOrEmpty(agent.brainName)))
            {
                agent.NormalizeBrainName();        
                m_LiveSessionData[agent.brainName] = agent;
            }
        }
        /// <summary>
        /// Change the current status of the Inworld client.
        /// </summary>
        /// <param name="status">the new status to change.</param>
        public void ChangeStatus(InworldConnectionStatus status) => OnStatusChanged?.Invoke(status);
        /// <summary>
        /// Copy the filling data from another Inworld client.
        /// </summary>
        /// <param name="rhs">the Inworld client's data to copy.</param>
        public void CopyFrom(InworldClient rhs)
        {
            Server = rhs.Server;
            APISecret = rhs.APISecret;
            APIKey = rhs.APIKey;
            CustomToken = rhs.CustomToken;
            InworldController.Client = this;
        }
        internal string APIKey
        {
            get => m_APIKey;
            set => m_APIKey = value;
        }
        internal string APISecret
        {
            get => m_APISecret;
            set => m_APISecret = value;
        }
        internal string SceneFullName
        {
            get => m_SceneFullName;
            set => m_SceneFullName = value;
        }
        internal string CustomToken
        {
            get => m_CustomToken;
            set => m_CustomToken = value;
        }
        protected virtual void OnEnable()
        {
            m_OutgoingCoroutine = OutgoingCoroutine();
            StartCoroutine(m_OutgoingCoroutine);
        }
        public virtual IEnumerator PrepareSession()
        {
            yield break;
        }
        protected virtual IEnumerator OutgoingCoroutine()
        {
            yield break;
        }
        protected string _GetSessionFullName(string sceneFullName)
        {
            string[] data = sceneFullName.Split('/');
            return data.Length != 4 ? "" : $"workspaces/{data[1]}/sessions/{m_Token.sessionId}";
        }
        protected string _GetCallbackReference(string sessionFullName, string interactionID, string correlationID)
        {
            return $"{sessionFullName}/interactions/{interactionID}/groups/{correlationID}";
        }
    }
}
