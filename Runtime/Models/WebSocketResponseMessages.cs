using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UG.Models.WebSocketResponseMessages
{
    // Wrapper class for discoverability easy access to all response message types
    // Todo: support json variant types for dict[str, Utility | Reference | None] 
    public static class ResponseMessages { }

    public class WebSocketResponseMessage
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("client_start_time")]
        public string ClientStartTime { get; set; }

        [JsonProperty("server_start_time")]
        public string ServerStartTime { get; set; }

        [JsonProperty("server_end_time")]
        public string ServerEndTime { get; set; }

        public WebSocketResponseMessage FromJson(string json)
        {
            return JsonConvert.DeserializeObject<WebSocketResponseMessage>(json);
        }
    }

    public class ErrorResponse : WebSocketResponseMessage
    {
        [JsonProperty("error")]
        public string Error { get; set; }

        public ErrorResponse()
        {
            Kind = "error";
        }
    }

    public class AuthenticateResponse : WebSocketResponseMessage
    {
        public AuthenticateResponse()
        {
            Kind = "authenticate";
        }
    }

    public class PingResponse : WebSocketResponseMessage
    {
        public PingResponse()
        {
            Kind = "ping";
        }
    }

    public class SetServiceProfileResponse : WebSocketResponseMessage
    {
        public SetServiceProfileResponse()
        {
            Kind = "set_service_profile";
        }
    }

    public class AddAudioResponse : WebSocketResponseMessage
    {
        public AddAudioResponse()
        {
            Kind = "add_audio";
        }
    }

    public class ClearAudioResponse : WebSocketResponseMessage
    {
        public ClearAudioResponse()
        {
            Kind = "clear_audio";
        }
    }

    public class CheckTurnResponse : WebSocketResponseMessage
    {
        [JsonProperty("is_user_still_speaking")]
        public bool IsUserStillSpeaking { get; set; }

        public CheckTurnResponse()
        {
            Kind = "check_turn";
        }
    }

    public class TranscribeResponse : WebSocketResponseMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        public TranscribeResponse()
        {
            Kind = "transcribe";
        }
    }

    public class AddKeywordsResponse : WebSocketResponseMessage
    {
        public AddKeywordsResponse()
        {
            Kind = "add_keywords";
        }
    }

    public class RemoveKeywordsResponse : WebSocketResponseMessage
    {
        public RemoveKeywordsResponse()
        {
            Kind = "remove_keywords";
        }
    }

    public class DetectKeywordsResponse : WebSocketResponseMessage
    {
        [JsonProperty("keywords")]
        public List<string> Keywords { get; set; } = new List<string>();

        public DetectKeywordsResponse()
        {
            Kind = "detect_keywords";
        }
    }

    public class AddSpeakerResponse : WebSocketResponseMessage
    {
        public AddSpeakerResponse()
        {
            Kind = "add_speaker";
        }
    }

    public class RemoveSpeakersResponse : WebSocketResponseMessage
    {
        public RemoveSpeakersResponse()
        {
            Kind = "remove_speakers";
        }
    }

    public class DetectSpeakersResponse : WebSocketResponseMessage
    {
        [JsonProperty("speakers")]
        public List<string> Speakers { get; set; } = new List<string>();

        public DetectSpeakersResponse()
        {
            Kind = "detect_speakers";
        }
    }

    public class SetConfigurationResponse : WebSocketResponseMessage
    {
        public SetConfigurationResponse()
        {
            Kind = "set_configuration";
        }
    }

    public class MergeConfigurationResponse : WebSocketResponseMessage
    {
        [JsonProperty("utilities")]
        public List<string> Utilities { get; set; } = new List<string>();

        public MergeConfigurationResponse()
        {
            Kind = "merge_configuration";
        }
    }

    public class GetConfigurationResponse : WebSocketResponseMessage
    {
        [JsonProperty("config")]
        public ConfigurationData Config { get; set; }

        public GetConfigurationResponse()
        {
            Kind = "get_configuration";
        }
    }

    public class RenderPromptResponse : WebSocketResponseMessage
    {
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        public RenderPromptResponse()
        {
            Kind = "render_prompt";
        }
    }

    public class InteractResponse : WebSocketResponseMessage
    {
        [JsonProperty("event")]
        public string Event { get; set; }

        public InteractResponse()
        {
            Kind = "interact";
        }
    }

    public class InteractionStartedEvent : InteractResponse
    {
        public InteractionStartedEvent()
        {
            Event = "interaction_started";
        }
    }

    public class TextEvent : InteractResponse
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        public TextEvent()
        {
            Event = "text";
        }
    }

    public class TextCompleteEvent : InteractResponse
    {
        public TextCompleteEvent()
        {
            Event = "text_complete";
        }
    }

    public class AudioEvent : InteractResponse
    {
        [JsonProperty("audio")]
        public string Audio { get; set; }

        public AudioEvent()
        {
            Event = "audio";
        }
    }

    public class AudioCompleteEvent : InteractResponse
    {
        public AudioCompleteEvent()
        {
            Event = "audio_complete";
        }
    }

    public class DataEvent : InteractResponse
    {
        [JsonProperty("data")]
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public DataEvent()
        {
            Event = "data";
        }
    }

    public class InteractionErrorEvent : InteractResponse
    {
        [JsonProperty("error")]
        public string Error { get; set; }

        public InteractionErrorEvent()
        {
            Event = "interaction_error";
        }
    }

    public class InteractionCompleteEvent : InteractResponse
    {
        public InteractionCompleteEvent()
        {
            Event = "interaction_complete";
        }
    }

    public class InterruptResponse : WebSocketResponseMessage
    {
        public InterruptResponse()
        {
            Kind = "interrupt";
        }
    }

    public class RunResponse : WebSocketResponseMessage
    {
        public RunResponse()
        {
            Kind = "run";
        }
    }

    public class GenerateImageResponse : WebSocketResponseMessage
    {
        [JsonProperty("image")]
        public string Image { get; set; }

        public GenerateImageResponse()
        {
            Kind = "generate_image";
        }
    }

}