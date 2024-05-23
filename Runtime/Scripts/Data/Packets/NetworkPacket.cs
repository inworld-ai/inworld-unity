﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;


namespace Inworld.Packet
{

    public class NetworkPacketResponse
    {
        [JsonConverter(typeof(PacketDeserializer))]
        public InworldPacket result;
        public InworldError error;
    }

    public class InworldError
    {
        public int code;
        public string message;
        public List<InworldErrorData> details;
        
        public InworldError(string data)
        {
            code = -1;
            message = data;
            details = new List<InworldErrorData>
            {
                new InworldErrorData
                {
                    errorType = ErrorType.CLIENT_ERROR,
                    reconnectType = ReconnectionType.UNDEFINED,
                    reconnectTime = "",
                    maxRetries = 0
                }
            };
        }
        [JsonIgnore]
        public bool IsValid => !string.IsNullOrEmpty(message);
        [JsonIgnore]
        public ReconnectionType RetryType  => details[0]?.reconnectType ?? ReconnectionType.UNDEFINED;
        [JsonIgnore]
        public ErrorType ErrorType => details[0]?.errorType ?? ErrorType.UNDEFINED;

    }
    [Serializable]
    public class InworldErrorData
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public ErrorType errorType;
        [JsonConverter(typeof(StringEnumConverter))]
        public ReconnectionType reconnectType;
        public string reconnectTime;
        public int maxRetries;
    }
}
