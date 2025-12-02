namespace UG
{
    /// <summary>
    /// Contains conversation command strings that can be customized externally.
    /// These commands are sent to the conversation system to control conversation flow.
    /// </summary>
    public class ConversationCommands
    {
        // Default command constants
        public const string DEFAULT_START_COMMAND = "[start conversation]";
        public const string DEFAULT_PAUSE_RESUME_COMMAND = "[resume after short pause]";
        public const string DEFAULT_LONG_PAUSE_RESUME_COMMAND = "[resume after long break]";
        public const string DEFAULT_ERROR_RESUME_COMMAND = "[resume after error]";

        public string StartCommand { get; set; } = DEFAULT_START_COMMAND;
        public string PauseResumeCommand { get; set; } = DEFAULT_PAUSE_RESUME_COMMAND;
        public string LongPauseResumeCommand { get; set; } = DEFAULT_LONG_PAUSE_RESUME_COMMAND;
        public string ErrorResumeCommand { get; set; } = DEFAULT_ERROR_RESUME_COMMAND;

        /// <summary>
        /// Sets all command values at once
        /// </summary>
        /// <param name="startCommand">Command to start conversation</param>
        /// <param name="pauseResumeCommand">Command to resume conversation after a short pause</param>
        /// <param name="longPauseResumeCommand">Command to resume after a long break</param>
        /// <param name="errorResumeCommand">Command to resume after error</param>
        public void Set(string startCommand, string pauseResumeCommand, string longPauseResumeCommand, string errorResumeCommand)
        {
            StartCommand = startCommand ?? DEFAULT_START_COMMAND;
            PauseResumeCommand = pauseResumeCommand ?? DEFAULT_PAUSE_RESUME_COMMAND;
            LongPauseResumeCommand = longPauseResumeCommand ?? DEFAULT_LONG_PAUSE_RESUME_COMMAND;
            ErrorResumeCommand = errorResumeCommand ?? DEFAULT_ERROR_RESUME_COMMAND;
        }

        /// <summary>
        /// Resets all command values to their defaults
        /// </summary>
        public void SetDefault()
        {
            StartCommand = DEFAULT_START_COMMAND;
            PauseResumeCommand = DEFAULT_PAUSE_RESUME_COMMAND;
            LongPauseResumeCommand = DEFAULT_LONG_PAUSE_RESUME_COMMAND;
            ErrorResumeCommand = DEFAULT_ERROR_RESUME_COMMAND;
        }
    }
}
