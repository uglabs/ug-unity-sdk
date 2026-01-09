using System.Collections.Generic;
using UG;
using UG.Models;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Basic UGSDK conversation example
/// </summary>
public class UGSDKAdvancedUtilitiesExample : MonoBehaviour
{
    [SerializeField] private Text _captionsText;
    [SerializeField] private Text _recordingStateText;
    [SerializeField] private Text _utilityClassifyResultText;
    [SerializeField] private Button _startConversationButton, _pauseConversationButton, _stopConversationButton;

    public void Awake()
    {
        _startConversationButton.onClick.AddListener(OnStartClicked);
        _pauseConversationButton.onClick.AddListener(OnPauseClicked);
        _stopConversationButton.onClick.AddListener(OnStopClicked);

        _captionsText.text = "Waiting to start...";
    }

    public void Start()
    {
        UGSDK.Initialize();
        UGSDK.ConversationManager.OnConversationEvent += OnConversationEvent;

        UGSDK.ConversationManager.SetConfiguration(new ConversationConfiguration()
        {
            Prompt = "Be polite, respond in 1 sentence only",
            Temperature = 0.5f,
            Utilities = new Dictionary<string, object>() // <utility_name, utility_config>
            {
                ["cats"] = new UG.Models.Classify // Utility type
                {
                    ClassificationQuestion = "Did the user mention cats in the last message?", // Question to ask
                    Answers = new List<string> { "yes", "no" } // Available answers
                },
                ["finish_conversation"] = new UG.Models.Classify
                {
                    ClassificationQuestion = "Did the user express a desire to finish the conversation? Did the user say 'bye', 'goodbye', or 'end conversation'?",
                    Answers = new List<string> { "yes", "no" }
                }
            }
        });

        // Set which utilities to will run on user input
        UGSDK.ConversationManager.SetOnInputUtilities(new List<string> { "cats", "finish_conversation" });
        // Set which utilities will run on assistant output
        // UGSDK.ConversationManager.SetOnOutputUtilities(null);
    }

    private void OnConversationEvent(ConversationEvent conversationEvent)
    {
        Debug.Log("Conversation event: " + conversationEvent.Type);
        switch (conversationEvent.Type)
        {
            case ConversationEventType.TextReceived:
                _captionsText.text += (conversationEvent.Data as TextReceivedData).Text;
                break;
            case ConversationEventType.Error:
                Debug.LogError("Error: " + (conversationEvent.Data as ErrorData).Message);
                break;
            case ConversationEventType.Stopped:
                Debug.Log("Conversation stopped");
                _recordingStateText.text = "Stopped";
                break;
            case ConversationEventType.InteractionStarted:
                Debug.Log("Interaction started");
                break;
            case ConversationEventType.PlayingAudio:
                Debug.Log("Playing audio");
                break;
            case ConversationEventType.PlayingAudioComplete:
                Debug.Log("Playing audio complete");
                break;
            case ConversationEventType.AuthenticationSuccessful:
                Debug.Log("Authentication successful");
                break;
            // Microphone events
            case ConversationEventType.RecordingMicrophone:
                Debug.Log("Recording microphone");
                _recordingStateText.text = "Recording";
                break;
            case ConversationEventType.PlayerSpoke:
                Debug.Log("Player spoke");
                _captionsText.text = "";
                _recordingStateText.text = "Speaking";
                break;
            case ConversationEventType.MicrophoneSilenced:
                Debug.Log("Microphone silenced");
                _recordingStateText.text = "Silenced";
                break;
            case ConversationEventType.ConversationPaused:
                Debug.Log("Conversation paused");
                break;
            // To get data from utilities, you need to subscribe to the ConversationEventType.DataReceived event
            case ConversationEventType.DataReceived:
                // Get the result of the utility by name
                var dataMessage = (conversationEvent.Data as DataReceivedData).Data;
                dataMessage.TryGetValue("cats", out var cats);
                Debug.Log("Data result received: " + cats);
                _utilityClassifyResultText.text = "Classification result: " + cats;

                // Check if the user wants to finish the conversation
                dataMessage.TryGetValue("finish_conversation", out var finishConversation);
                Debug.Log("Utility finish_conversation result: " + finishConversation);
                if (finishConversation as string == "yes")
                {
                    UGSDK.ConversationManager.SetConversationComplete();
                }
                break;
            default:
                break;
        }
    }

    private void OnStartClicked()
    {
        _captionsText.text = "";
        UGSDK.ConversationManager.StartConversation();
    }

    private void OnPauseClicked()
    {
        UGSDK.ConversationManager.PauseConversation();
    }

    private void OnStopClicked()
    {
        _captionsText.text = "";
        UGSDK.ConversationManager.StopConversation();
        UGSDK.ConversationManager.ClearConversation();
    }
}
