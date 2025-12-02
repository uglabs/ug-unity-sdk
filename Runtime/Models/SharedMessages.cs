using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UG.Models
{
    // Shared models used by both request and response messages
    public class ConfigurationData
    {
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("temperature")]
        public float Temperature { get; set; }

        [JsonProperty("context")]
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();

        [JsonProperty("utilities")]
        public Dictionary<string, object> Utilities { get; set; } = new Dictionary<string, object>();
    }

    public class Reference
    {
        [JsonProperty("reference")]
        public string ReferenceValue { get; set; }
    }

    // Base class for utilities
    public abstract class Utility
    {
        [JsonProperty("type")]
        public abstract string Type { get; }
    }

    public class Classify : Utility
    {
        [JsonProperty("type")]
        public override string Type => "classify";

        [JsonProperty("classification_question")]
        public string ClassificationQuestion { get; set; }

        [JsonProperty("additional_context")]
        public string AdditionalContext { get; set; }

        [JsonProperty("answers")]
        public List<string> Answers { get; set; } = new List<string>();
    }

    public class Extract : Utility
    {
        [JsonProperty("type")]
        public override string Type => "extract";

        [JsonProperty("extract_prompt")]
        public string ExtractPrompt { get; set; }

        [JsonProperty("additional_context")]
        public string AdditionalContext { get; set; }
    }

    public class AudioConfig
    {
        [JsonProperty("sample_rate")]
        public int? SampleRate { get; set; }

        [JsonProperty("channels")]
        public int? Channels { get; set; }

        [JsonProperty("format")]
        public string Format { get; set; }
    }
}
