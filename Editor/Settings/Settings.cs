using UG.Settings;

namespace UG.Editor.Settings
{
    using UnityEditor;
    using UnityEngine;
    using UG.Constants;
    using System.IO;

    public class SettingsWindow : EditorWindow
    {
        [MenuItem("Tools/UG Labs/Settings")]
        public static void ShowWindow()
        {
            GetWindow<SettingsWindow>("UG Labs Settings");
        }

        [MenuItem("Tools/UG Labs/UG Studio")]
        public static void ShowUGStudio()
        {
            Application.OpenURL("https://pug-playground.stg.uglabs.app/");
        }

        private void OnGUI()
        {
            OnGUISettings();
        }

        private void OnGUISettings()
        {
            if (File.Exists(Constants.SettingsPath))
            {
                GUILayout.Label($"Settings file at ({Constants.SettingsPath}):");

                if (GUILayout.Button("Open Settings"))
                {
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<UGSDKSettings>(Constants.SettingsPath));
                }
            }
            else
            {
                GUILayout.Label("Settings file is missing", new GUIStyle(GUI.skin.label) { normal = new GUIStyleState { textColor = Color.red } });
                if (GUILayout.Button("Create Settings"))
                {
                    CreateSettings();
                }
            }
        }

        private void CreateSettings()
        {
            string resourcesPath = "Assets/Resources";
            if (!Directory.Exists(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
            }

            UGSDKSettings settings = CreateInstance<UGSDKSettings>();
            AssetDatabase.CreateAsset(settings, Constants.SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = settings;
        }
    }
}