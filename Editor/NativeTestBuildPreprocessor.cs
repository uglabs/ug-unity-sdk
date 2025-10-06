using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;

/// <summary>
/// For running SDK tests - we copy StreamingAssets folder with our test files to Assets/StreamingAssets of the project.
/// </summary>
#if UG_DISTRIBUTE
public class NativeTestBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // Only run for Android builds
        if (report.summary.platform == BuildTarget.Android)
        {
            CopyStreamingAssetsForBuild();
        }
    }

    private void CopyStreamingAssetsForBuild()
    {
        // Source: Package StreamingAssets
        string packageStreamingPath = "Packages/ug-unity-sdk/Tests/StreamingAssets";

        // Destination: Main project StreamingAssets
        string projectStreamingPath = "Assets/StreamingAssets";

        try
        {
            // Ensure the destination directory exists
            if (!Directory.Exists(projectStreamingPath))
            {
                Directory.CreateDirectory(projectStreamingPath);
                Debug.Log("[NativeTestBuildPreprocessor] Created Assets/StreamingAssets directory");
            }

            // Copy all files from package StreamingAssets to project StreamingAssets
            if (Directory.Exists(packageStreamingPath))
            {
                CopyDirectory(packageStreamingPath, projectStreamingPath);
                Debug.Log("[NativeTestBuildPreprocessor] Copied test files from package to Assets/StreamingAssets for build");

                // Refresh the asset database so Unity sees the new files
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.LogWarning("[NativeTestBuildPreprocessor] Package StreamingAssets directory not found: " + packageStreamingPath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[NativeTestBuildPreprocessor] Failed to copy StreamingAssets: " + e.Message);
        }
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // Create destination directory if it doesn't exist
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Copy all files
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true); // Overwrite if exists
            Debug.Log($"[NativeTestBuildPreprocessor] Copied for build: {fileName}");
        }

        // Recursively copy subdirectories
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(dir);
            string destSubDir = Path.Combine(destDir, dirName);
            CopyDirectory(dir, destSubDir);
        }
    }

    [MenuItem("UG Tests/Copy Native Test Files to StreamingAssets")]
    public static void ManualCopyStreamingAssets()
    {
        var processor = new NativeTestBuildPreprocessor();
        processor.CopyStreamingAssetsForBuild();
        Debug.Log("Manually copied native test files to StreamingAssets");
    }
}
#endif