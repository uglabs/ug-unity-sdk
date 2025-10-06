using System;
using UnityEngine;
using UG.Services;

namespace UG.Settings
{
    [CreateAssetMenu(fileName = "ServerSettings", menuName = "UG Labs/Create Settings file")]
    public class UGSDKSettings : ScriptableObject
    {
        [Header("Host URL (e.g. https://pug.uglabs.app)")]
        public string host;

        [Header("API Key")]
        public string apiKey;

        [Header("Team Name (Leave empty for default team)")]
        public string teamName;

        // Should eventually be removed/dev only - each user should have their own federated ID
        [Header("Federated ID (Temprorary/Dev only)")]
        public string federatedId;

        [Header("Log level - default is Error, switch to Debug to see more logs")]
        public UGLog.LogLevel logLevel = UGLog.LogLevel.Error;
    }
}