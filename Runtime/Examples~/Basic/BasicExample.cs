using UG;
using UG.Models;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Basic UGSDK conversation example
/// </summary>
public class UGSDKBasicExample : MonoBehaviour
{
    [SerializeField] private Text _captionsText;
    [SerializeField] private Text _recordingStateText;
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
            Temperature = 0.5f
        });
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
