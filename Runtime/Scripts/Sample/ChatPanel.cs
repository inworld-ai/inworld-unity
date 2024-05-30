﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.Entities;
using Inworld.Packet;
using Inworld.UI;
using System;
using System.Linq;
using UnityEngine;


namespace Inworld.Sample
{
    [Serializable]
    public struct ChatOptions
    {
        public bool audio;
        public bool text;
        public bool emotion;
        public bool narrativeAction;
        public bool relation;
        public bool trigger;
        public bool longBubbleMode;
    }
    public class ChatPanel : BubblePanel
    {
        [SerializeField] protected ChatBubble m_BubbleLeft;
        [SerializeField] protected ChatBubble m_BubbleRight;
        [SerializeField] protected ChatOptions m_ChatOptions;
        public override bool IsUIReady => base.IsUIReady && m_BubbleLeft && m_BubbleRight;
        
        void OnEnable()
        {
            InworldController.Client.OnPacketSent += OnInteraction;
            InworldController.CharacterHandler.Event.onCharacterListJoined.AddListener(OnCharacterJoined);
            InworldController.CharacterHandler.Event.onCharacterListLeft.AddListener(OnCharacterLeft);
        }

        void OnDisable()
        {
            if (!InworldController.Instance)
                return;
            InworldController.Client.OnPacketSent -= OnInteraction;
            InworldController.CharacterHandler.Event.onCharacterListJoined.RemoveListener(OnCharacterJoined);
            InworldController.CharacterHandler.Event.onCharacterListLeft.RemoveListener(OnCharacterLeft);
        }

        protected virtual void OnCharacterJoined(InworldCharacter character)
        {
            // YAN: Clear existing event listener to avoid adding multiple times.
            character.Event.onPacketReceived.RemoveListener(OnInteraction); 
            character.Event.onPacketReceived.AddListener(OnInteraction);
        }

        protected virtual void OnCharacterLeft(InworldCharacter character)
        {
            character.Event.onPacketReceived.RemoveListener(OnInteraction); 
        }
        
        protected virtual void OnInteraction(InworldPacket incomingPacket)
        {
            switch (incomingPacket)
            {
                case ActionPacket actionPacket:
                    HandleAction(actionPacket);
                    break;
                case TextPacket textPacket:
                    HandleText(textPacket);
                    break;
                case EmotionPacket emotionPacket:
                    HandleEmotion(emotionPacket);
                    break;
                case CustomPacket customPacket:
                    HandleTrigger(customPacket);
                    break;
                case AudioPacket audioPacket: 
                    HandleAudio(audioPacket);
                    break;
                case ControlPacket controlEvent:
                    HandleControl(controlEvent);
                    break;
                case MutationPacket mutationPacket:
                    RemoveBubbles(mutationPacket);
                    break;
                default:
                    InworldAI.LogWarning($"Received unknown {incomingPacket.type}");
                    break;
            }
        }
        protected virtual void RemoveBubbles(MutationPacket mutationPacket)
        {
            CancelResponse response = new CancelResponse();
            if (mutationPacket?.mutation is RegenerateResponseEvent regenEvt)
                response.interactionId = regenEvt.regenerateResponse.interactionId;
            if (mutationPacket?.mutation is CancelResponseEvent cancelEvt)
                response = cancelEvt.cancelResponses;
            if (string.IsNullOrEmpty(response.interactionId))
                return;
            if (m_ChatOptions.longBubbleMode)
                RemoveBubble(response.interactionId);
            else
                response.utteranceId.ForEach(RemoveBubble);
        }
        protected virtual void HandleAudio(AudioPacket audioPacket)
        {
            // Already Played.
        }
        protected virtual void HandleControl(ControlPacket controlEvent)
        {
            // Not process in the global chat panel.
        }
        protected virtual void HandleRelation(CustomPacket relationPacket)
        {
            if (!m_ChatOptions.relation || !IsUIReady)
                return;
            if (!InworldController.Client.LiveSessionData.TryGetValue(relationPacket.routing.source.name, out InworldCharacterData charData))
                return;
            string key = m_ChatOptions.longBubbleMode ? relationPacket.packetId.interactionId : relationPacket.packetId.utteranceId;
            string charName = charData.givenName ?? "Character";
            Texture2D thumbnail = charData.thumbnail ? charData.thumbnail : InworldAI.DefaultThumbnail;
            string content = relationPacket.custom.parameters.Aggregate(" ", (current, param) => current + $"{param.name}: {param.value} ");
            InsertBubbleWithPacketInfo(key, relationPacket.packetId, m_BubbleLeft, charName, m_ChatOptions.longBubbleMode, content, thumbnail);
        }
        protected virtual void HandleTrigger(CustomPacket customPacket)
        {
            if (customPacket.Message == InworldMessage.RelationUpdate)
                HandleRelation(customPacket);
            if (!m_ChatOptions.trigger || customPacket.custom == null || !IsUIReady)
                return;
            if (!InworldController.Client.LiveSessionData.TryGetValue(customPacket.routing.source.name, out InworldCharacterData charData))
                return;
            string key = m_ChatOptions.longBubbleMode ? customPacket.packetId.interactionId : customPacket.packetId.utteranceId;
            string charName = charData.givenName ?? "Character";
            Texture2D thumbnail = charData.thumbnail ? charData.thumbnail : InworldAI.DefaultThumbnail;
            if (string.IsNullOrEmpty(customPacket.TriggerName))
                return;
            string content = $"(Received: {customPacket.Trigger})";
            InsertBubbleWithPacketInfo(key, customPacket.packetId, m_BubbleLeft, charName, m_ChatOptions.longBubbleMode, content, thumbnail);
        }
        protected virtual void HandleEmotion(EmotionPacket emotionPacket)
        {
            // Not process in the global chat panel.
        }
        protected virtual void HandleText(TextPacket textPacket)
        {
            if (!m_ChatOptions.text || textPacket.text == null || string.IsNullOrWhiteSpace(textPacket.text.text) || !IsUIReady)
                return;
            string key = "";
            switch (textPacket.Source)
            {
                case SourceType.AGENT:
                {
                    InworldCharacterData charData = InworldController.Client.GetCharacterDataByID(textPacket.routing.source.name);
                    if (charData != null)
                    {
                        key = m_ChatOptions.longBubbleMode ? textPacket.packetId.interactionId : textPacket.packetId.utteranceId;
                        string charName = charData.givenName ?? "Character";
                        Texture2D thumbnail = charData.thumbnail ? charData.thumbnail : InworldAI.DefaultThumbnail;
                        string content = textPacket.text.text;
                        InsertBubbleWithPacketInfo(key, textPacket.packetId, m_BubbleLeft, charName, m_ChatOptions.longBubbleMode, content, thumbnail);
                    }
                    break;
                }
                case SourceType.PLAYER:
                    // YAN: Player Input does not apply longBubbleMode.
                    //      And Key is always utteranceID.
                    key = textPacket.packetId.utteranceId;
                    InsertBubbleWithPacketInfo(key, textPacket.packetId, m_BubbleRight, InworldAI.User.Name, false, textPacket.text.text, InworldAI.DefaultThumbnail);
                    break;
            }
        }
        protected virtual void HandleAction(ActionPacket actionPacket)
        {
            if (!m_ChatOptions.narrativeAction || actionPacket.action == null || actionPacket.action.narratedAction == null || string.IsNullOrWhiteSpace(actionPacket.action.narratedAction.content) || !IsUIReady)
                return;

            switch (actionPacket.routing.source.type)
            {
                case SourceType.AGENT:
                    InworldCharacterData charData = InworldController.Client.GetCharacterDataByID(actionPacket.routing.source.name);
                    if (charData == null)
                        return;
                    string key = m_ChatOptions.longBubbleMode ? actionPacket.packetId.interactionId : actionPacket.packetId.utteranceId;
                    string charName = charData.givenName ?? "Character";
                    Texture2D thumbnail = charData.thumbnail ? charData.thumbnail : InworldAI.DefaultThumbnail;
                    string content = $"<i><color=#AAAAAA>{actionPacket.action.narratedAction.content}</color></i>";
                    InsertBubbleWithPacketInfo(key, actionPacket.packetId, m_BubbleLeft, charName, m_ChatOptions.longBubbleMode, content, thumbnail);
                    break;
                case SourceType.PLAYER:
                    // YAN: Player Input does not apply longBubbleMode.
                    //      And Key is always utteranceID.
                    key = actionPacket.packetId.utteranceId;
                    content = $"<i><color=#AAAAAA>{actionPacket.action.narratedAction.content}</color></i>";
                    InsertBubbleWithPacketInfo(key, actionPacket.packetId, m_BubbleRight, InworldAI.User.Name, false, content, InworldAI.DefaultThumbnail);
                    break;
            }
        }
    }
}
