using System;
using System.Collections.Generic;

namespace UG
{
    public enum ConversationEventType
    {
        Processing, // Processing the conversation
        InteractionStarted, // Initiated a new interaction
        InteractionComplete, // Interaction completed - no more data to receive
        TextReceived, // Received text from the conversation
        AudioReceived, // Received audio from the conversation
        DataReceived, // Received utility data from the conversation
        Error, // Error occurred in the conversation
        RecordingMicrophone, // Microphone is recording - waiting for player to speak
        MicrophoneSilenced, // Microphone is silenced - player is done speaking
        PlayerSpoke, // Player speech detected - microphone is recording
        LongTimeoutTriggered, // No speech was detected for a long time
        PlayingAudio, // Playing audio from the conversation
        PlayingAudioComplete, // Audio playback complete
        AuthenticationSuccessful, // Authentication successful at the start of the conversation
        Stopped,
        ConversationPaused,
        VoiceCaptureNoSpeechTimeout,
        VoiceCaptureSpeechTooLong,
    }

    // Base class for all event data
    public abstract class ConversationEventData
    {
        public string ComponentId;
        public bool IsStandalone;
    }

    public class LoadingData : ConversationEventData
    {
        public LoadingData() { }
    }

    public class PlayingAudioData : ConversationEventData
    {
        public PlayingAudioData() { }
    }

    public class ConversationStartedData : ConversationEventData
    {
        public string ConversationId { get; }
        public ConversationStartedData(string conversationId) => ConversationId = conversationId;
    }

    public class InteractionStartedData : ConversationEventData
    {
        public Dictionary<string, string> Headers { get; }
        public string ConversationId;
        public InteractionStartedData(Dictionary<string, string> headers, string componentId, string conversationId)
        {
            Headers = headers;
            ComponentId = componentId;
            ConversationId = conversationId;
        }
    }

    public class TextReceivedData : ConversationEventData
    {
        public string Text { get; }
        public TextReceivedData(string text) => Text = text;
    }

    public class AudioReceivedData : ConversationEventData
    {
        public byte[] AudioData { get; }
        public AudioReceivedData(byte[] audioData) => AudioData = audioData;
    }

    public class DataReceivedData : ConversationEventData
    {
        public Dictionary<string, object> Data { get; }
        public DataReceivedData(Dictionary<string, object> data) => Data = data;
    }

    public class ErrorData : ConversationEventData
    {
        public string Message { get; }
        public enum ErrorType
        {
            Unknown,
            NetworkError, // Not recoverable
            ResponseError, // Recoverable
        }
        public ErrorType Type { get; }
        public ErrorData(string message, ErrorType type)
        {
            Message = message;
            Type = type;
        }
        public ErrorData(string message) => Message = message;
    }

    public class RecordingMicrophoneData : ConversationEventData
    {
        public bool IsRecording { get; }
        public RecordingMicrophoneData(bool isRecording) => IsRecording = isRecording;
    }

    public class StringValueData : ConversationEventData
    {
        public string SourceName { get; }
        public string Value { get; }
        public StringValueData(string sourceName, string value)
        {
            SourceName = sourceName;
            Value = value;
        }
    }

    public class BoolValueData : ConversationEventData
    {
        public string SourceName { get; }
        public bool Value { get; }
        public BoolValueData(string sourceName, bool value)
        {
            SourceName = sourceName;
            Value = value;
        }
    }

    public class ImageReceivedData : ConversationEventData
    {
        public string SourceName { get; }
        public string ImageContent { get; }
        public ImageReceivedData(string sourceName, string imageContent)
        {
            SourceName = sourceName;
            ImageContent = imageContent;
        }
    }

    public class ConversationEvent
    {
        public ConversationEventType Type { get; }
        public ConversationEventData Data { get; }

        public ConversationEvent(ConversationEventType type, string componentId = "",
            ConversationEventData data = null, bool isStandalone = false)
        {
            Type = type;
            Data = data;
            if (Data != null)
            {
                Data.ComponentId = componentId;
                Data.IsStandalone = isStandalone;
            }
        }
    }
}