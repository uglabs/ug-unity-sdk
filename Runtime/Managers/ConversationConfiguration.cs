using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UG
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum VoiceProvider
    {
        [EnumMember(Value = "elevenlabs")]
        ElevenLabs,
        
        [EnumMember(Value = "deepdub")]
        DeepDub
    }
    
    public class VoiceProfile
    {
        // Provider selection - determines which TTS service handles the request
        // If not set, binding order decides (ElevenLabs first by default)
        [JsonProperty("provider")]
        public VoiceProvider Provider { get; set; } = VoiceProvider.ElevenLabs;
        
        // Voice identifier - used by BOTH providers
        // For ElevenLabs: the ElevenLabs voice ID
        // For Deepdub: maps to the voice_prompt_id
        [JsonProperty("voice_id")]
        public string VoiceId { get; set; }
        
        #region ElevenLabs-specific parameters
        [JsonProperty("speed")]
        public float Speed { get; set; }
        
        [JsonProperty("stability")]
        public float Stability { get; set; }
        
        [JsonProperty("similarity_boost")]
        public float SimilarityBoost { get; set; }
        
        [JsonProperty("elevenlabs_model")]
        public string ElevenlabsModel { get; set; }
        #endregion
        
        #region Deepdub-specific parameters
        [JsonProperty("deepdub_model")]
        public string DeepDubModel { get; set; }
        
        [JsonProperty("deepdub_tempo")]
        public float? DeepDubTempo { get; set; }
        
        [JsonProperty("deepdub_variance")]
        public float? DeepDubVariance { get; set; }
        
        [JsonProperty("deepdub_locale")]
        public string DeepDubLocale { get; set; }
        
        // Deepdub accent control (blend between base and target accents)
        [JsonProperty("deepdub_accent_base_locale")]
        public string DeepDubAccentBaseLocale { get; set; }
        
        [JsonProperty("deepdub_accent_locale")]
        public string DeepDubAccentLocale { get; set; }
        
        [JsonProperty("deepdub_accent_ratio")]
        public float? DeepDubAccentRatio { get; set; }
        
        // Deepdub audio post-processing
        [JsonProperty("deepdub_clean_audio")]
        public bool? DeepDubCleanAudio { get; set; }
        #endregion
    }

    public class ConversationConfiguration
    {
        #region Client settings
        // Set to true if we want to allow interrupts (player can start speaking at any time)
        [JsonIgnore]
        public bool IsAllowInterrupts { get; set; }
        // Timeout in seconds for how long we should wait for player to speak before triggering speech timeout message
        [JsonIgnore]
        public int VoiceCaptureMaxSilenceSeconds { get; set; }
        // When there is no audio recorded for SpeechTimeoutSeconds, we send an automatic message to prompt user to speak - e.g. "are you still there?",,
        // This can happen when the microphone is turned off/user not speaking 
        [JsonIgnore]
        public int VoiceCaptureMaxSilentRetry { get; set; } = 3;
        #endregion

        #region Conversation settings
        [JsonProperty("app_name")]
        public string AppName { get; set; }
        
        [JsonProperty("app_description")]
        public string AppDescription { get; set; }
        
        [JsonProperty("version")]
        public string Version { get; set; }
        
        [JsonProperty("format_version")]
        public string FormatVersion { get; set; }

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("temperature")]
        public float Temperature { get; set; }
        
        [JsonProperty("utilities")]
        public Dictionary<string, object> Utilities { get; set; } = new Dictionary<string, object>();
        
        [JsonProperty("context")]
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
        
        [JsonProperty("language_code")]
        public string LanguageCode { get; set; }

        [JsonProperty("audio_output")]
        public bool IsAudioOutput { get; set; }

        [JsonProperty("on_input")]
        public List<string> OnInputUtilities { get; set; } = new List<string>();
        
        [JsonProperty("on_output")]
        public List<string> OnOutputUtilities { get; set; } = new List<string>();
        
        [JsonProperty("safety_policy")]
        public string SafetyPolicy { get; set; }
        
        [JsonProperty("voice_profile")]
        public VoiceProfile VoiceProfile { get; set; }
        
        [JsonProperty("debug")]
        public bool Debug { get; set; } = false;

        public ConversationConfiguration()
        {
            IsAllowInterrupts = false;
            VoiceCaptureMaxSilenceSeconds = 25;
            Prompt = "";
            Temperature = 0.5f;
        }

        public string ToJson()
        {
            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };
            return JsonConvert.SerializeObject(this, settings);
        }

        public static ConversationConfiguration FromJson(string json)
        {
            return JsonConvert.DeserializeObject<ConversationConfiguration>(json);
        }
        #endregion
    }
}