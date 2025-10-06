# Audio Recording Service

This folder contains the audio recording service implementation for UG Unity SDK, including support for multiple recording backends and audio enhancement processing.

## Overview

The audio recording service provides a unified interface (`IAudioRecordingService`) for capturing audio from various sources, with support for:
- Basic microphone recording
- AEC (Acoustic Echo Cancellation) recording
- Audio enhancement processing pipeline
- Multiple enhancement algorithms

## Core Components

### `IAudioRecordingService`
The main interface that all audio recording services implement:
```csharp
public interface IAudioRecordingService
{
    void Init(bool isRequestMicPermissionOnInit);
    void StartRecording();
    void StopRecording();
    event Action<float[]> OnSamplesRecorded;
    void Dispose();
}
```

### `AudioRecordingService`
Basic microphone recording service with permission handling.

### `AudioRecordingServiceAEC`
Recording service with Acoustic Echo Cancellation support.

### `AudioEnhancementWrapper`
A wrapper service that applies audio enhancement algorithms to the recording pipeline.

## Basic Usage

### Simple Recording
```csharp
using UG.Services.UserInput.AudioRecordingService;

// Create and initialize the service
var audioService = new AudioRecordingService();
audioService.Init(true); // Request microphone permission

// Subscribe to recorded samples
audioService.OnSamplesRecorded += (samples) => {
    Debug.Log($"Recorded {samples.Length} samples");
};

// Start/stop recording
audioService.StartRecording();
// ... recording happens ...
audioService.StopRecording();

// Clean up
audioService.Dispose();
```

### AEC Recording
```csharp
var aecService = new AudioRecordingServiceAEC();
aecService.Init(true);

// Use the same interface
aecService.OnSamplesRecorded += (samples) => {
    Debug.Log($"AEC recorded {samples.Length} samples");
};

aecService.StartRecording();
// ... recording with echo cancellation ...
aecService.StopRecording();
aecService.Dispose();
```

## Audio Enhancement Pipeline

The `AudioEnhancementWrapper` allows you to process audio through multiple enhancement algorithms in sequence.

### Creating Enhancement Algorithms

All enhancement algorithms implement the `IAudioEnhancement` interface:

```csharp
public interface IAudioEnhancement
{
    float[] ProcessSamples(float[] inputSamples, int sampleRate = 16000);
    string Name { get; }
    bool IsEnabled { get; set; }
}
```

### Example: Noise Gate Enhancement

```csharp
using SimpleNoiseReduction;

public class NoiseGateEnhancement : IAudioEnhancement
{
    private float _thresholdDb = -30.0f;
    private float _reductionDb = -20.0f;
    private float _attackMs = 5.0f;
    private float _decayMs = 50.0f;
    
    public string Name => "Noise Gate";
    public bool IsEnabled { get; set; } = true;
    
    public float[] ProcessSamples(float[] inputSamples, int sampleRate = 16000)
    {
        if (!IsEnabled || inputSamples == null || inputSamples.Length == 0)
            return inputSamples;
            
        try
        {
            // Use the existing AdvancedNoiseGate implementation
            return AdvancedNoiseGate.ProcessNoiseGate(
                inputSamples, 
                _thresholdDb, 
                _reductionDb, 
                _attackMs, 
                0.0f, // holdMs
                _decayMs, 
                0.0f, // frequencyThresholdKhz
                sampleRate
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in NoiseGateEnhancement: {e.Message}");
            return inputSamples; // Fallback to original
        }
    }
    
    // Factory methods for common presets
    public static NoiseGateEnhancement CreateConservative()
    {
        return new NoiseGateEnhancement(-50.0f, -10.0f, 25.0f, 150.0f);
    }
    
    public static NoiseGateEnhancement CreateAggressive()
    {
        return new NoiseGateEnhancement(-20.0f, -40.0f, 2.0f, 20.0f);
    }
}
```

### Using the Enhancement Wrapper

```csharp
// Create your base audio recording service
var audioService = new AudioRecordingService();

// Create enhancement algorithms
var noiseGate = new NoiseGateEnhancement(-30.0f, -20.0f, 5.0f, 50.0f);
var conservativeNoiseGate = NoiseGateEnhancement.CreateConservative();

// Create the enhancement wrapper with enhancements
var enhancements = new List<IAudioEnhancement> { noiseGate, conservativeNoiseGate };
var enhancedAudioService = new AudioEnhancementWrapper(audioService, enhancements);

// Or add enhancements later
enhancedAudioService.AddEnhancement(aggressiveNoiseGate);

// Subscribe to both raw and enhanced events
enhancedAudioService.OnSamplesRecorded += (rawSamples) => {
    // Raw samples from the source service
    Debug.Log($"Raw samples: {rawSamples.Length}");
};

enhancedAudioService.OnEnhancedSamplesRecorded += (enhancedSamples) => {
    // Enhanced samples after all processing
    Debug.Log($"Enhanced samples: {enhancedSamples.Length}");
};

// Use it like any other IAudioRecordingService
enhancedAudioService.Init(true);
enhancedAudioService.StartRecording();
// ... recording with enhancements applied ...
enhancedAudioService.StopRecording();
enhancedAudioService.Dispose();
```

### Managing Enhancements

```csharp
// Add/remove enhancements dynamically
enhancedAudioService.AddEnhancement(newEchoCancellation);
enhancedAudioService.RemoveEnhancement(noiseGate);

// Enable/disable specific enhancements
noiseGate.IsEnabled = false; // Temporarily disable

// Get all enhancements
var allEnhancements = enhancedAudioService.GetEnhancements();
foreach (var enhancement in allEnhancements)
{
    Debug.Log($"Enhancement: {enhancement.Name}, Enabled: {enhancement.IsEnabled}");
}

// Set sample rate
enhancedAudioService.SetSampleRate(48000);
```

## Advanced Usage Patterns

### Pipeline Composition
```csharp
// Create a complex audio processing pipeline
var audioService = new AudioRecordingServiceAEC(); // Start with AEC

var enhancements = new List<IAudioEnhancement>
{
    new NoiseGateEnhancement(-25.0f, -30.0f, 3.0f, 30.0f),     // Noise gate
    new SpectralSubtractionEnhancement(),                        // Spectral subtraction
    new WienerFilterEnhancement(),                               // Wiener filtering
    new VoiceActivityDetectionEnhancement()                      // VAD
};

var enhancedService = new AudioEnhancementWrapper(audioService, enhancements);
```

### Custom Enhancement Implementation
```csharp
public class CustomEchoCancellation : IAudioEnhancement
{
    public string Name => "Custom Echo Cancellation";
    public bool IsEnabled { get; set; } = true;
    
    public float[] ProcessSamples(float[] inputSamples, int sampleRate = 16000)
    {
        // Your custom echo cancellation algorithm here
        var processedSamples = new float[inputSamples.Length];
        
        // Apply your algorithm...
        for (int i = 0; i < inputSamples.Length; i++)
        {
            // Process sample i
            processedSamples[i] = inputSamples[i]; // Placeholder
        }
        
        return processedSamples;
    }
}
```

## Testing

### Unit Tests
The service includes comprehensive unit tests in `AudioRecordingServiceTest.cs` that test both basic recording and playback functionality.

### Integration Testing
Test the enhancement pipeline with real audio:
```csharp
[Test]
public void TestNoiseGateEnhancement()
{
    var audioService = new AudioRecordingService();
    var noiseGate = new NoiseGateEnhancement(-30.0f, -20.0f, 5.0f, 50.0f);
    var enhancedService = new AudioEnhancementWrapper(audioService, new List<IAudioEnhancement> { noiseGate });
    
    // Test recording and enhancement
    enhancedService.Init(true);
    enhancedService.StartRecording();
    
    // Wait for samples and verify enhancement
    // ... test logic ...
    
    enhancedService.StopRecording();
    enhancedService.Dispose();
}
```

## Best Practices

1. **Always Dispose**: Call `Dispose()` when done with services
2. **Error Handling**: Wrap enhancement processing in try-catch blocks
3. **Sample Rate**: Ensure all enhancements use the same sample rate
4. **Performance**: Keep enhancement algorithms efficient for real-time processing
5. **Testing**: Test each enhancement individually before combining them

## Troubleshooting

### Common Issues

1. **No Audio Output**: Check if `AudioListener` is present in the scene
2. **Permission Denied**: Ensure microphone permissions are granted
3. **High Latency**: Review enhancement algorithm performance
4. **Memory Leaks**: Always dispose of services and unsubscribe from events

### Debug Logging
Enable debug logging to troubleshoot issues:
```csharp
// Subscribe to both events to see the difference
enhancedService.OnSamplesRecorded += (samples) => {
    Debug.Log($"Raw: {samples.Length} samples, RMS: {CalculateRMS(samples):F3}");
};

enhancedService.OnEnhancedSamplesRecorded += (samples) => {
    Debug.Log($"Enhanced: {samples.Length} samples, RMS: {CalculateRMS(samples):F3}");
};
```

## Dependencies

- **Unity Engine**: Core audio functionality
- **FftSharp**: For frequency-domain processing (used in noise gate)
- **.NET**: For basic audio processing and data structures

## Future Enhancements

- Real-time parameter adjustment
- Preset management system
- Audio visualization tools
- Machine learning-based enhancement
- Multi-channel audio support
- GPU acceleration for heavy processing
