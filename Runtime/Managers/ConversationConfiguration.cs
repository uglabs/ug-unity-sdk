using System.Collections.Generic;

namespace UG
{
    public class ConversationConfiguration
    {
        #region Client-specific settings
        // Set to true if we want to allow interrupts (player can start speaking at any time)
        public bool IsAllowInterrupts { get; set; }
        // Timeout in seconds for how long we should wait for player to speak before triggering speech timeout message
        public int SpeechTimeoutSeconds { get; set; }
        #endregion

        public string Prompt { get; set; }
        public float Temperature { get; set; }
        public Dictionary<string, object> Utilities { get; set; } = new Dictionary<string, object>();
        public string LanguageCode { get; set; }
        public bool IsAudioOutput { get; set; }

        public ConversationConfiguration()
        {
            IsAllowInterrupts = false;
            SpeechTimeoutSeconds = 25;
            Prompt = "";
            Temperature = 0.5f;
        }
    }
}