using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UG.Models;

namespace UG.Models.WebSocketRequestMessages
{
    // Wrapper class for discoverability easy access to all message types
    // Create messages with uid, clientStartTime
    // Todo: support json variant types for dict[str, Utility | Reference | None] 
    public static class Messages
    {
        public static AuthenticateRequest CreateAuthenticateMessage(string accessToken = null)
        {
            return new AuthenticateRequest(uid: GetUid(),
                clientStartTime: GetClientStartTime(),
                accessToken: accessToken);
        }

        public static InteractRequest CreateInteractMessage(string text,
            List<string> speakers,
            Dictionary<string, object> context,
            List<string> onInput = null,
            List<string> onOutput = null,
            bool audioOutput = false,
            string languageCode = "en")
        {
            return new InteractRequest(uid: GetUid(),
                clientStartTime: GetClientStartTime(),
                text: text,
                speakers: speakers,
                context: context,
                onInput: onInput,
                onOutput: onOutput,
                audioOutput: audioOutput,
                languageCode: languageCode);
        }
        
        
        public static GetConfigurationRequest CreateGetConfigurationMessage()
        {
            return new GetConfigurationRequest(uid: GetUid(), clientStartTime: GetClientStartTime());
        }

        public static SetConfigurationRequest CreateSetConfigurationMessage(string prompt, float temperature, Dictionary<string, object> utilities = null)
        {
            return new SetConfigurationRequest(uid: GetUid(),
                clientStartTime: GetClientStartTime(),
                config: new ConfigurationData { Prompt = prompt, Temperature = temperature, Utilities = utilities ?? new Dictionary<string, object>() });
        }

        public static AddAudioRequest CreateAddAudioMessage(string audio, AudioConfig config = null)
        {
            return new AddAudioRequest(uid: GetUid(),
                clientStartTime: GetClientStartTime(),
                audio: audio,
                config: config);
        }

        public static ClearAudioRequest CreateClearAudioMessage()
        {
            return new ClearAudioRequest(uid: GetUid(), clientStartTime: GetClientStartTime());
        }

        public static CheckTurnRequest CreateCheckTurnMessage()
        {
            return new CheckTurnRequest(uid: GetUid(), clientStartTime: GetClientStartTime());
        }

        public static TranscribeRequest CreateTranscribeMessage(string languageCode = "en")
        {
            return new TranscribeRequest(uid: GetUid(),
                clientStartTime: GetClientStartTime(),
                languageCode: languageCode);
        }

        public static InterruptRequest CreateInterruptMessage(string targetUid, int? atCharacter = null)
        {
            return new InterruptRequest(uid: GetUid(),
                clientStartTime: GetClientStartTime(),
                targetUid: targetUid,
                atCharacter: atCharacter);
        }

        public static string GetUid()
        {
            return Guid.NewGuid().ToString();
        }

        public static string GetClientStartTime()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
        }
    }

    public class WebSocketRequestMessage
    {
        [JsonProperty("type")]
        public string Type = "request";

        [JsonProperty("uid")]
        public string Uid { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("client_start_time")]
        public string ClientStartTime { get; set; }

        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };
        public string ToJson() => JsonConvert.SerializeObject(this, settings);
    }

    public class InteractRequest : WebSocketRequestMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("speakers")]
        public List<string> Speakers;

        [JsonProperty("context")]
        public Dictionary<string, object> Context { get; set; }

        [JsonProperty("on_input")]
        public List<string> OnInput;

        [JsonProperty("on_output")]
        public List<string> OnOutput;

        [JsonProperty("audio_output")]
        public bool AudioOutput;
        
        [JsonProperty("language_code")]
        public string LanguageCode { get; set; }

        public InteractRequest(string uid, string clientStartTime,
            string text,
            List<string> speakers,
            Dictionary<string, object> context,
            List<string> onInput,
            List<string> onOutput,
            bool audioOutput,
            string languageCode)
        {
            Uid = uid;
            Type = "stream";
            Kind = "interact";
            ClientStartTime = clientStartTime;
            Text = text;
            Speakers = speakers;
            Context = context;
            OnInput = onInput;
            OnOutput = onOutput;
            AudioOutput = audioOutput;
            LanguageCode = languageCode;
        }
    }


    public class AddAudioRequest : WebSocketRequestMessage
    {
        [JsonProperty("audio")]
        public string Audio { get; set; }

        [JsonProperty("config")]
        public AudioConfig Config { get; set; }

        public AddAudioRequest(string uid, string clientStartTime, string audio, AudioConfig config = null)
        {
            Uid = uid;
            Kind = "add_audio";
            ClientStartTime = clientStartTime;
            Audio = audio;
            Config = config;
        }
    }

    public class GetConfigurationRequest : WebSocketRequestMessage
    {
        public GetConfigurationRequest(string uid, string clientStartTime)
        {
            Uid = uid;
            Kind = "get_configuration";
            ClientStartTime = clientStartTime;
        }
    }

    public class SetConfigurationRequest : WebSocketRequestMessage
    {
        [JsonProperty("config")]
        public ConfigurationData Config { get; set; }

        public SetConfigurationRequest(string uid, string clientStartTime, ConfigurationData config)
        {
            Uid = uid;
            Kind = "set_configuration";
            ClientStartTime = clientStartTime;
            Config = config;
        }
    }

    public class AuthenticateRequest : WebSocketRequestMessage
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        public AuthenticateRequest(string uid, string clientStartTime, string accessToken)
        {
            Uid = uid;
            Kind = "authenticate";
            ClientStartTime = clientStartTime;
            AccessToken = accessToken;
        }
    }

    public class ClearAudioRequest : WebSocketRequestMessage
    {
        public ClearAudioRequest(string uid, string clientStartTime)
        {
            Uid = uid;
            Kind = "clear_audio";
            ClientStartTime = clientStartTime;
        }
    }

    public class CheckTurnRequest : WebSocketRequestMessage
    {
        public CheckTurnRequest(string uid, string clientStartTime)
        {
            Uid = uid;
            Kind = "check_turn";
            ClientStartTime = clientStartTime;
        }
    }

    public class TranscribeRequest : WebSocketRequestMessage
    {
        [JsonProperty("language_code")]
        public string LanguageCode { get; set; }

        public TranscribeRequest(string uid, string clientStartTime, string languageCode = "en")
        {
            Uid = uid;
            Kind = "transcribe";
            ClientStartTime = clientStartTime;
            LanguageCode = languageCode;
        }
    }

    public class InterruptRequest : WebSocketRequestMessage
    {
        [JsonProperty("target_uid")]
        public string TargetUid { get; set; }

        [JsonProperty("at_character")]
        public int? AtCharacter { get; set; }

        public InterruptRequest(string uid, string clientStartTime, string targetUid, int? atCharacter = null)
        {
            Uid = uid;
            Kind = "interrupt";
            ClientStartTime = clientStartTime;
            TargetUid = targetUid;
            AtCharacter = atCharacter;
        }
    }


    public class AudioConfig
    {
        [JsonProperty("sampling_rate")]
        public int? SampleRate { get; set; }

        [JsonProperty("mime_type")]
        public string MimeType { get; set; }
    }
}