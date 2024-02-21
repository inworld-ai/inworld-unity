﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/
using Inworld.Packet;
using Inworld.Entities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityWebSocket;

namespace Inworld
{
    public class InworldWebSocketClient : InworldClient
    {
        [Tooltip("This is for the legacy previous data")] [SerializeField] protected PreviousDialog m_PreviousDialog;
        protected WebSocket m_Socket;
        protected LoadSceneResponse m_CurrentSceneData;
        protected const string k_DisconnectMsg = "The remote party closed the WebSocket connection without completing the close handshake.";
        public override void GetAccessToken() => StartCoroutine(_GetAccessToken(m_PublicWorkspace));
        public override void StartSession() => StartCoroutine(_StartSession());
        public override void Disconnect() => StartCoroutine(_DisconnectAsync());
        public override LoadSceneResponse GetLiveSessionInfo() => m_CurrentSceneData;

        public override void SendCapabilities()
        {
            string jsonToSend = JsonUtility.ToJson(InworldAI.Capabilities.ToPacket);
            InworldAI.Log($"Sending Capabilities: {InworldAI.Capabilities}");
            m_Socket.SendAsync(jsonToSend);
        }
        public override void SendSessionConfig()
        {
            string gameSessionID = $"{InworldAI.User.Name}:{m_Token.sessionId}:{_GetSessionGUID()}";
            string jsonToSend = JsonUtility.ToJson(m_Token.ToPacket(gameSessionID));
            InworldAI.Log($"Sending Session Info: {gameSessionID}");
            m_Socket.SendAsync(jsonToSend);
        }
        public override void SendClientConfig()
        {
            string jsonToSend = JsonUtility.ToJson(InworldAI.UnitySDK.ToPacket);
            InworldAI.Log($"Sending Client Info: {InworldAI.UnitySDK}");
            m_Socket.SendAsync(jsonToSend);
        }
        public override void SendUserConfig()
        {
            string jsonToSend = JsonUtility.ToJson(InworldAI.User.Request.ToPacket);
            InworldAI.Log($"Sending User Config: {InworldAI.User.Request}");
            m_Socket.SendAsync(jsonToSend);
        }
        // Load History Priority: User's Data (Session State > History dialog) > Session State in Runtime Memory 
        // We'll fetch the data in runtime memory only when user did NOT put any data in Continuation.
        //
        // So if you'd like to keep updating the dialog history, please call `GetHistoryAsync` when terminating session,
        // And save the session state somewhere in your own storage.
        public override void SendHistory()
        {
            if (!m_Continuation.IsValid)
            {
                if (string.IsNullOrEmpty(SessionHistory))
                    return;
                m_Continuation.continuationType = ContinuationType.CONTINUATION_TYPE_EXTERNALLY_SAVED_STATE;
                m_Continuation.externallySavedState = SessionHistory;
            }
            string jsonToSend = JsonUtility.ToJson(m_Continuation.ToPacket);
            InworldAI.Log($"Send previous history... {jsonToSend}");
            m_Socket.SendAsync(jsonToSend);
        }
        public override void LoadScene(string sceneFullName)
        {
            string jsonToSend = JsonUtility.ToJson(new LoadScenePacket(sceneFullName));
            m_Socket.SendAsync(jsonToSend);
        }

        public override void SendText(string characterID, string textToSend, List<string> characters = null)
        {
            if (string.IsNullOrEmpty(textToSend) || string.IsNullOrEmpty(characterID) && characters == null)
                return;
            InworldPacket packet = new TextPacket
            {
                timestamp = InworldDateTime.UtcNow,
                type = "TEXT",
                packetId = new PacketId(),
                routing = characters == null ? new Routing(characterID) : new Routing("", characters),
                text = new TextEvent(textToSend)
            };
            string jsonToSend = JsonUtility.ToJson(packet);
            Dispatch(packet);
            m_Socket.SendAsync(jsonToSend);
        }
        public override void SendCancelEvent(string characterID, string interactionID, List<string> characters = null)
        {
            if (string.IsNullOrEmpty(characterID) && characters == null)
                return;
            MutationPacket cancelPacket = new MutationPacket
            {
                timestamp = InworldDateTime.UtcNow,
                type = "CANCEL_RESPONSE",
                packetId = new PacketId(),
                routing = characters == null ? new Routing(characterID) : new Routing("", characters),
                mutation = new MutationEvent
                {
                    cancelResponses = new CancelResponse
                    {
                        interactionId = interactionID
                    }
                }
            };
            m_Socket.SendAsync(JsonUtility.ToJson(cancelPacket));
        }
        public override void SendTrigger(string charID, string triggerName, Dictionary<string, string> parameters, List<string> characters = null)
        {

            if (string.IsNullOrEmpty(charID))
                charID = "WORLD";
            
            InworldPacket packet = new CustomPacket
            {
                timestamp = InworldDateTime.UtcNow,
                type = "CUSTOM",
                packetId = new PacketId(),
                routing = characters == null ? new Routing(charID) : new Routing("", characters),
                custom = new CustomEvent(triggerName, parameters)
            };
            string jsonToSend = JsonUtility.ToJson(packet);
            InworldAI.Log($"Send Trigger {triggerName}");
            m_Socket.SendAsync(jsonToSend);
        }
        public override void StartAudio(string charID, List<string> characters = null)
        {
            if (string.IsNullOrEmpty(charID) && characters == null)
                return;

            InworldPacket packet = new ControlPacket
            {
                timestamp = InworldDateTime.UtcNow,
                type = "TEXT",
                packetId = new PacketId(),
                routing = characters == null ? new Routing(charID) : new Routing("", characters),
                control = new ControlEvent
                {
                    action = "AUDIO_SESSION_START"
                }
            };
            string jsonToSend = JsonUtility.ToJson(packet);
            m_Socket.SendAsync(jsonToSend);
        }
        public override void StopAudio(string charID, List<string> characters = null)
        {
            if (string.IsNullOrEmpty(charID) && characters == null)
                return;
            InworldPacket packet = new ControlPacket
            {
                timestamp = InworldDateTime.UtcNow,
                type = "TEXT",
                packetId = new PacketId(),
                routing = characters == null ? new Routing(charID) : new Routing("", characters),
                control = new ControlEvent
                {
                    action = "AUDIO_SESSION_END"
                }
            };
            string jsonToSend = JsonUtility.ToJson(packet);
            m_Socket.SendAsync(jsonToSend);
        }
        public override void SendAudio(string charID, string base64, List<string> characters = null)
        {
            if (string.IsNullOrEmpty(charID) && characters == null)
                return;
            InworldPacket packet = new AudioPacket
            {
                timestamp = InworldDateTime.UtcNow,
                type = "AUDIO",
                packetId = new PacketId(),
                routing = characters == null ? new Routing(charID) : new Routing("", characters),
                dataChunk = new DataChunk
                {
                    type = "AUDIO",
                    chunk = base64
                }
            };
            string jsonToSend = JsonUtility.ToJson(packet);
            Dispatch(packet);
            m_Socket.SendAsync(jsonToSend);
        }
        protected IEnumerator _GetAccessToken(string workspaceFullName = "")
        {
            Status = InworldConnectionStatus.Initializing;
            string responseJson = m_CustomToken;
            if (string.IsNullOrEmpty(responseJson))
            {
                if (string.IsNullOrEmpty(m_APIKey))
                {
                    Error = "Please fill API Key!";
                    yield break;
                }
                if (string.IsNullOrEmpty(m_APISecret))
                {
                    Error = "Please fill API Secret!";
                    yield break;
                }
                string header = InworldAuth.GetHeader(m_ServerConfig.runtime, m_APIKey, m_APISecret);
                UnityWebRequest uwr = new UnityWebRequest(m_ServerConfig.TokenServer, "POST");
                Status = InworldConnectionStatus.Initializing;

                uwr.SetRequestHeader("Authorization", header);
                uwr.SetRequestHeader("Content-Type", "application/json");

                AccessTokenRequest req = new AccessTokenRequest
                {
                    api_key = m_APIKey,
                    resource_id = workspaceFullName
                };
                string json = JsonUtility.ToJson(req);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                uwr.uploadHandler = new UploadHandlerRaw(bodyRaw);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    Error = $"Error Get Token: {uwr.error}";
                }
                uwr.uploadHandler.Dispose();
                responseJson = uwr.downloadHandler.text;
            }
            m_Token = JsonUtility.FromJson<Token>(responseJson);
            if (!IsTokenValid)
            {
                Error = "Get Token Failed";
                yield break;
            }
            Status = InworldConnectionStatus.Initialized;
        }
        public override void GetHistoryAsync(string sceneFullName) => StartCoroutine(_GetHistoryAsync(sceneFullName));
        
        protected IEnumerator _GetHistoryAsync(string sceneFullName)
        {
            string sessionFullName = _GetSessionFullName(sceneFullName);
            UnityWebRequest uwr = new UnityWebRequest(m_ServerConfig.LoadSessionURL(sessionFullName), "GET");
            uwr.SetRequestHeader("Grpc-Metadata-session-id", m_Token.sessionId);
            uwr.SetRequestHeader("Authorization", $"Bearer {m_Token.token}");
            uwr.SetRequestHeader("Content-Type", "application/json");
            uwr.downloadHandler = new DownloadHandlerBuffer();
            yield return uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Error = $"Error loading scene {m_Token.sessionId}: {uwr.error} {uwr.downloadHandler.text}";
                uwr.uploadHandler.Dispose();
                yield break;
            }
            string responseJson = uwr.downloadHandler.text;
            PreviousSessionResponse response = JsonUtility.FromJson<PreviousSessionResponse>(responseJson);
            SessionHistory = response.state;
            InworldAI.Log($"Get Previous Content Encrypted: {SessionHistory}");
        }
        string _GetSessionFullName(string sceneFullName)
        {
            string[] data = sceneFullName.Split('/');
            return data.Length != 4 ? "" : $"workspaces/{data[1]}/sessions/{m_Token.sessionId}";
        }
        string _GetSessionGUID()
        {
            if (!string.IsNullOrEmpty(m_Continuation?.externallySavedState) && m_Continuation?.externallySavedState.Length >= 8)
                return m_Continuation.externallySavedState[..8];
            if (!string.IsNullOrEmpty(SessionHistory) && SessionHistory.Length >= 8)
                return SessionHistory[..8];
            return Guid.NewGuid().ToString()[..8];
        }
        protected IEnumerator _StartSession()
        {
            if (Status == InworldConnectionStatus.Connected)
                yield break;
            string url = m_ServerConfig.SessionURL(m_Token.sessionId);
            if (!IsTokenValid)
                yield break;
            string[] param = {m_Token.type, m_Token.token};
            m_Socket = WebSocketManager.GetWebSocket(url);
            if (m_Socket == null)
                m_Socket = new WebSocket(url, param);
            m_Socket.OnOpen += OnSocketOpen;
            m_Socket.OnMessage += OnMessageReceived;
            m_Socket.OnClose += OnSocketClosed;
            m_Socket.OnError += OnSocketError;
            Status = InworldConnectionStatus.Connecting;
            m_Socket.ConnectAsync();
        }
        protected void _RegisterLiveSession(SessionResponseEvent sessionResponse)
        {
            m_CurrentSceneData = new LoadSceneResponse();
            if (sessionResponse.loadedScene?.agents?.Count > 0)
                m_CurrentSceneData.agents.AddRange(sessionResponse.loadedScene.agents);
            if (sessionResponse.loadedCharacters?.agents?.Count > 0)
                m_CurrentSceneData.agents.AddRange(sessionResponse.loadedCharacters.agents);
            if (InworldController.CharacterHandler)
                InworldController.CharacterHandler.RegisterLiveSession();
        }
        protected IEnumerator _DisconnectAsync()
        {
            yield return new WaitForEndOfFrame();
            m_Socket?.CloseAsync();
            yield return new WaitForEndOfFrame();
        }
        void OnSocketOpen(object sender, OpenEventArgs e)
        {
            InworldAI.Log($"Connect {m_Token.sessionId}");
            Status = InworldConnectionStatus.Connected;
        }
        
        void OnMessageReceived(object sender, MessageEventArgs e)
        {
            NetworkPacketResponse response = JsonUtility.FromJson<NetworkPacketResponse>(e.Data);
            if (response == null || response.result == null)
            {
                InworldAI.LogError($"Error Processing packets {e.Data}");
                return;
            }
            InworldNetworkPacket packetReceived = response.result;
            if (packetReceived.Type == PacketType.SESSION_RESPONSE)
            {
                if (packetReceived.Packet is SessionResponsePacket sessionResponse)
                    _RegisterLiveSession(sessionResponse.sessionControlResponse);
                return;
            }
            if (packetReceived.Type == PacketType.CONTROL && packetReceived.control.action == "WARNING")
            {
                InworldAI.LogWarning(packetReceived.control.description);
                return;
            }
            if (packetReceived.Type == PacketType.UNKNOWN)
            {
                if (e.Data.Contains("error"))
                {
                    if (e.Data.Contains("inactivity")) 
                        Status = InworldConnectionStatus.LostConnect;
                    else
                        Error = e.Data;
                }
                else
                {
                    InworldAI.LogWarning($"Received Unknown {e.Data}");
                }
            }
            Dispatch(packetReceived.Packet);
        }
        void OnSocketClosed(object sender, CloseEventArgs e)
        {
            InworldAI.Log($"Closed: StatusCode: {e.StatusCode}, Reason: {e.Reason}");
            Status = e.StatusCode == CloseStatusCode.Normal ? InworldConnectionStatus.Idle : InworldConnectionStatus.LostConnect;
        }

        void OnSocketError(object sender, ErrorEventArgs e)
        {
            if (e.Message != k_DisconnectMsg)
                Error = e.Message;
        }
        
        void OnDestroy()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            var webSocketManager = FindObjectOfType<WebSocketManager>();
            if(webSocketManager)
                Destroy(webSocketManager.gameObject);
#endif
        }
    }
}
