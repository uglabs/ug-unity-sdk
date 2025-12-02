using System.IO;
using UG.Services;
using UnityEngine;

namespace UG.Utils
{
    // Save audio data to a file when debug flag is set
    public static class ConversationAudioDebug
    {
        private static int _sessionResponseCount = 0;
        private static string _dataPath;
        private static bool _isInitialized;

        public static void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            _dataPath = Application.persistentDataPath;
            _isInitialized = true;
            UGLog.Log("[ConversationAudioDebug] Initialized with data path: " + _dataPath);
        }

        public static void NewResponse()
        {
            _sessionResponseCount++;
        }

        public static void SaveAudioData(byte[] audioData, string conversationId, string extension = ".ogg")
        {
#if UNITY_EDITOR && DEBUG_SAVE_AUDIO
            if (!_isInitialized)
            {
                Initialize();
            }

            string filePath = $"{_dataPath}/audio_file_new{conversationId}_{_sessionResponseCount}{extension}";

            UGLog.Log($"Saving audio to file: {filePath}");

            // Append the bytes to the file
            if (!File.Exists(filePath))
            {
                File.WriteAllBytes(filePath, audioData);
            }
            else
            {
                // Manual append using FileStream
                using (FileStream fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write))
                {
                    fileStream.Write(audioData, 0, audioData.Length);
                    fileStream.Flush();
                }
            }
#endif
        }
    }
}
