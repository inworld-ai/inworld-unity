﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.Packet;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Inworld.Entities
{
    [Serializable]
    public class UserRequest
    {
        public string name;
        public string id;
        public UserSetting userSettings;
        
        [JsonIgnore]
        public UserConfigPacket ToPacket => new UserConfigPacket
        {
            timestamp = InworldDateTime.UtcNow,
            type = PacketType.SESSION_CONTROL,
            packetId = new PacketId(),
            routing = new Routing("WORLD"),
            sessionControl = new UserConfigEvent
            {
                userConfiguration = this
            }
        };
        public override string ToString()
        {
            string result = $"{name}: {id}";
            return userSettings?.playerProfile?.fields?.Count > 0
                ? userSettings.playerProfile.fields.Aggregate(result, (current, field) => current + $" {field.fieldId}: {field.fieldValue}")
                : result;
        }
    }

    [Serializable]
    public class UserSetting
    {
        [HideInInspector] public bool viewTranscriptConsent;
        public PlayerProfile playerProfile;

        public UserSetting(List<PlayerProfileField> rhs)
        {
            viewTranscriptConsent = true;
            playerProfile = new PlayerProfile
            {
                fields = rhs
            };
        }
    }
    [Serializable]
    public class PlayerProfile
    {
        public List<PlayerProfileField> fields;
    }
    [Serializable]
    public class PlayerProfileField
    {
        public string fieldId;
        public string fieldValue;
    }
    [Serializable]
    public class UserConfigPacket : InworldPacket
    {
        public UserConfigEvent sessionControl;
    }
}
