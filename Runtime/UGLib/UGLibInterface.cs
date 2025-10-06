using UnityEngine;
using System.Runtime.InteropServices;
using System;

namespace UG.Services
{
    /// <summary>
    /// Simplified interface for UGLib native functions
    /// This can be used independently or replaced with the full UGLibWrapper
    /// </summary>
    public static class UGLibInterface
    {
        // Platform-specific library names
        private const string LIBRARY_NAME =
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            "libwebrtc-audio-processing-2";
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        "libwebrtc-audio-processing-2";
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        "libwebrtc-audio-processing-2";
#elif UNITY_ANDROID
        "libwebrtc-audio-processing-2";
#elif UNITY_IOS
        "__Internal";
#else
        "libwebrtc-audio-processing-2";
#endif

        /// <summary>
        /// Pushes audio samples to the native UGLib
        /// </summary>
        /// <param name="samples">Pointer to audio sample data</param>
        /// <param name="sampleCount">Number of samples in the buffer</param>
        /// <param name="channels">Number of audio channels</param>
        /// <returns>0 on success, non-zero on error</returns>
        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_push_audio_samples(IntPtr samples, int sampleCount, int channels);

        /// <summary>
        /// Pushes both audio output and microphone input samples for AEC processing
        /// </summary>
        /// <param name="audioOutSamples">Pointer to Unity audio output samples</param>
        /// <param name="micInSamples">Pointer to microphone input samples</param>
        /// <param name="sampleCount">Number of samples in each buffer</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="processedOutput">Pointer to output buffer for processed audio</param>
        /// <returns>0 on success, non-zero on error</returns>
        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_push_audioout_micin_samples(IntPtr audioOutSamples, IntPtr micInSamples, int sampleCount, int sampleRate, IntPtr processedOutput);

        /// <summary>
        /// Pushes audio samples to the native library
        /// </summary>
        /// <param name="samples">Pointer to audio sample data</param>
        /// <param name="sampleCount">Number of samples</param>
        /// <param name="channels">Number of audio channels</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int PushAudioSamples(IntPtr samples, int sampleCount, int channels)
        {
            try
            {
                return uglib_push_audio_samples(samples, sampleCount, channels);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error pushing audio samples: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Pushes both Unity audio output and microphone input to UGLib for AEC processing
        /// </summary>
        /// <param name="audioOutSamples">Pointer to Unity audio output samples</param>
        /// <param name="micInSamples">Pointer to microphone input samples</param>
        /// <param name="sampleCount">Number of samples in each buffer</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="processedOutput">Pointer to output buffer for processed audio</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int PushAudioOutMicInSamples(IntPtr audioOutSamples, IntPtr micInSamples, int sampleCount, int sampleRate, IntPtr processedOutput)
        {
            try
            {
                return uglib_push_audioout_micin_samples(audioOutSamples, micInSamples, sampleCount, sampleRate, processedOutput);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error pushing AEC audio samples: {e.Message}");
                return -1;
            }
        }

#if UNITY_IOS
    // iOS uses __Internal for static libraries
    [DllImport("__Internal")]
    private static extern int uglib_init_webrtc();

    [DllImport("__Internal")]
    private static extern int uglib_set_stream_delay_ms(int delay_ms);

    // Configuration functions for WebRTC Audio Processing
    [DllImport("__Internal")]
    private static extern int uglib_set_echo_cancellation(int enabled, int mobile_mode);

    [DllImport("__Internal")]
    private static extern int uglib_set_gain_controller1(int enabled, int mode);

    [DllImport("__Internal")]
    private static extern int uglib_set_gain_controller2(int enabled);

    [DllImport("__Internal")]
    private static extern int uglib_set_noise_suppression(int enabled, int level);

    [DllImport("__Internal")]
    private static extern int uglib_set_high_pass_filter(int enabled);

    [DllImport("__Internal")]
    private static extern int uglib_set_pre_gain_factor(float pre_gain_factor);

    [DllImport("__Internal")]
    private static extern int uglib_set_post_gain_factor(float post_gain_factor);

    [DllImport("__Internal")]
    private static extern int uglib_set_analog_level_minimum(int analog_level_minimum);

    [DllImport("__Internal")]
    private static extern int uglib_get_test_minor_version();

    [DllImport("__Internal")]
    private static extern void uglib_cleanup();

#else

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_init_webrtc();

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_stream_delay_ms(int delay_ms);

        // Configuration functions for WebRTC Audio Processing
        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_echo_cancellation(int enabled, int mobile_mode);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_gain_controller1(int enabled, int mode);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_gain_controller2(int enabled);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_noise_suppression(int enabled, int level);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_high_pass_filter(int enabled);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_pre_gain_factor(float pre_gain_factor);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_post_gain_factor(float post_gain_factor);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_set_analog_level_minimum(int analog_level_minimum);

        [DllImport(LIBRARY_NAME)]
        private static extern int uglib_get_test_minor_version();

        [DllImport(LIBRARY_NAME)]
        private static extern void uglib_cleanup();

#endif

        public static int InitWebRTC()
        {
            try
            {
                return uglib_init_webrtc();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error initializing WebRTC: {e.Message}");
                return -1;
            }
        }

        public static int ConfigureWebRTC(int echo_cancellation, int gain_control, int noise_suppression, int high_pass_filter)
        {
            try
            {
                // Configure WebRTC using individual setter functions since uglib_configure_webrtc is not available
                int result = 0;

                // Set echo cancellation
                result |= SetEchoCancellation(echo_cancellation != 0, true); // Use mobile mode for iOS

                // Set gain control
                if (gain_control != 0)
                {
                    result |= SetGainController1(true, 1); // Adaptive digital mode
                    result |= SetGainController2(true);
                }
                else
                {
                    result |= SetGainController1(false, 0);
                    result |= SetGainController2(false);
                }

                // Set noise suppression
                if (noise_suppression != 0)
                {
                    result |= SetNoiseSuppression(true, 2); // High level
                }
                else
                {
                    result |= SetNoiseSuppression(false, 0);
                }

                // Set high pass filter
                result |= SetHighPassFilter(high_pass_filter != 0);

                return result;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error configuring WebRTC: {e.Message}");
                return -1;
            }
        }

        public static int GetTestMinorVersion()
        {
            return uglib_get_test_minor_version();
        }

        /// <summary>
        /// Sets the stream delay for echo cancellation
        /// </summary>
        /// <param name="delayMs">Delay in milliseconds between speaker output and microphone input</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetStreamDelayMs(int delayMs)
        {
            try
            {
                return uglib_set_stream_delay_ms(delayMs);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting stream delay: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets echo cancellation configuration
        /// </summary>
        /// <param name="enabled">Enable echo cancellation</param>
        /// <param name="mobileMode">Use mobile mode for echo cancellation</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetEchoCancellation(bool enabled, bool mobileMode)
        {
            try
            {
                return uglib_set_echo_cancellation(enabled ? 1 : 0, mobileMode ? 1 : 0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting echo cancellation: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets gain controller 1 configuration
        /// </summary>
        /// <param name="enabled">Enable gain controller 1</param>
        /// <param name="mode">Gain controller 1 mode (0=AdaptiveAnalog, 1=AdaptiveDigital, 2=FixedDigital)</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetGainController1(bool enabled, int mode)
        {
            try
            {
                return uglib_set_gain_controller1(enabled ? 1 : 0, mode);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting gain controller 1: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets gain controller 2 configuration
        /// </summary>
        /// <param name="enabled">Enable gain controller 2</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetGainController2(bool enabled)
        {
            try
            {
                return uglib_set_gain_controller2(enabled ? 1 : 0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting gain controller 2: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets noise suppression configuration
        /// </summary>
        /// <param name="enabled">Enable noise suppression</param>
        /// <param name="level">Noise suppression level (0=Low, 1=Moderate, 2=High, 3=VeryHigh)</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetNoiseSuppression(bool enabled, int level)
        {
            try
            {
                return uglib_set_noise_suppression(enabled ? 1 : 0, level);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting noise suppression: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets high pass filter configuration
        /// </summary>
        /// <param name="enabled">Enable high pass filter</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetHighPassFilter(bool enabled)
        {
            try
            {
                return uglib_set_high_pass_filter(enabled ? 1 : 0);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting high pass filter: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets pre-gain factor for capture level adjustment
        /// </summary>
        /// <param name="preGainFactor">Pre-processing gain factor</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetPreGainFactor(float preGainFactor)
        {
            try
            {
                return uglib_set_pre_gain_factor(preGainFactor);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting pre-gain factor: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets post-gain factor for capture level adjustment
        /// </summary>
        /// <param name="postGainFactor">Post-processing gain factor</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetPostGainFactor(float postGainFactor)
        {
            try
            {
                return uglib_set_post_gain_factor(postGainFactor);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting post-gain factor: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets analog level minimum for gain controller
        /// </summary>
        /// <param name="analogLevelMinimum">Minimum analog level (0-255)</param>
        /// <returns>0 on success, non-zero on error</returns>
        public static int SetAnalogLevelMinimum(int analogLevelMinimum)
        {
            try
            {
                return uglib_set_analog_level_minimum(analogLevelMinimum);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error setting analog level minimum: {e.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Cleans up and destroys the WebRTC Audio Processing instance
        /// Call this when shutting down or stopping audio processing
        /// </summary>
        public static void CleanupWebRTC()
        {
            try
            {
                uglib_cleanup();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UGLibInterface] Error cleaning up WebRTC: {e.Message}");
            }
        }
    }
}