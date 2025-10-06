using System;
using UnityEngine;

namespace UG.Services
{
    public static class UGLog
    {
        public enum LogLevel
        {
            Error,
            Warning,
            Debug
        }
        private static LogLevel _logLevel;

        public static void SetLogLevel(LogLevel logLevel)
        {
            _logLevel = logLevel;
        }

        public static void Log(string message)
        {
            if (IsShouldLog(LogLevel.Debug))
            {
                Debug.Log(message);
            }
        }

        public static void LogWarning(string message)
        {
            if (IsShouldLog(LogLevel.Warning))
            {
                Debug.LogWarning(message);
            }
        }

        public static void LogError(Exception e)
        {
            if (IsShouldLog(LogLevel.Error))
            {
                Debug.LogError(e);
            }
        }

        public static void LogError(string message)
        {
            if (IsShouldLog(LogLevel.Error))
            {
                Debug.LogError(message);
            }
        }

        private static bool IsShouldLog(LogLevel logLevel)
        {
            return logLevel <= _logLevel;
        }
    }
}