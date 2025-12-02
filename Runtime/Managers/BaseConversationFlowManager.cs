using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UG.Services.UGApiService;
using UG.Services.WebSocket;
using UG.Services;
using UG.Settings;
using UnityEngine;
using UG.Services.AudioStreamingService;
using Newtonsoft.Json.Linq;
using UG.Models.WebSocketRequestMessages;
using UG.Models.WebSocketResponseMessages;
using UG.Utils;
using Newtonsoft.Json;

namespace UG
{
    public class BaseConversationFlowManager
    {
        protected private WebSocketResponseMessage ParseResponse(string jsonLine)
        {
            try
            {
                var jObject = JObject.Parse(jsonLine);
                var kind = jObject["kind"]?.Value<string>();
                var type = jObject["type"]?.Value<string>();

                // Process interact stream messages
                if (type == "stream" && kind == "interact")
                {
                    var eventType = jObject["event"]?.Value<string>();
                    return eventType switch
                    {
                        "interaction_started" => jObject.ToObject<InteractionStartedEvent>(),
                        "text" => jObject.ToObject<TextEvent>(),
                        "text_complete" => jObject.ToObject<TextCompleteEvent>(),
                        "audio" => jObject.ToObject<AudioEvent>(),
                        "audio_complete" => jObject.ToObject<AudioCompleteEvent>(),
                        "interaction_complete" => jObject.ToObject<InteractionCompleteEvent>(),
                        "interaction_error" => jObject.ToObject<InteractionErrorEvent>(),
                        "data" => jObject.ToObject<DataEvent>(),
                        _ => jObject.ToObject<InteractResponse>() // fallback to base InteractResponse
                    };
                }

                // Process other stream messages - stream, non-interact
                if (type == "stream")
                {
                    // kind - close
                }

                // Parse by kind for non-interact messages
                return kind switch
                {
                    "authenticate" => jObject.ToObject<AuthenticateResponse>(),
                    "error" => jObject.ToObject<ErrorResponse>(),
                    "check_turn" => jObject.ToObject<CheckTurnResponse>(),
                    "transcribe" => jObject.ToObject<TranscribeResponse>(),
                    "add_audio" => jObject.ToObject<AddAudioResponse>(),
                    "clear_audio" => jObject.ToObject<ClearAudioResponse>(),
                    "set_configuration" => jObject.ToObject<SetConfigurationResponse>(),
                    "get_configuration" => jObject.ToObject<GetConfigurationResponse>(),
                    "interrupt" => jObject.ToObject<InterruptResponse>(),
                    _ => jObject.ToObject<WebSocketResponseMessage>()
                };
            }
            catch (Exception ex)
            {
                UGLog.LogError($"Failed to parse response: {ex.Message}");
                UGLog.LogError($"Raw line: {jsonLine}");
                return null;
            }
        }
    }
}