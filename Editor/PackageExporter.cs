using System.IO;
using UG.Services;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace UG.Editor
{
    /// <summary>
    /// Used to create .tar.gz package for distribution
    /// </summary>
    public class PackagePacker
    {
#if UG_DISTRIBUTE
        private static string _packagePath = "Packages/io.uglabs.unity.sdk";
        private static string _destinationFolder = "Assets";
        private static string _testsFolder = "Packages/io.uglabs.unity.sdk/Tests";
        private static string _tempTestsFolder = "Packages/io.uglabs.unity.sdk-tests-backup";

        [MenuItem("Tools/UG Labs/Create UGSDK package")]
        private static void PackPackage()
        {
            if (!Directory.Exists(_packagePath))
            {
                UGLog.LogError($"[PackageExporter] Package directory {_packagePath} does not exist.");
                return;
            }

            // Move Tests folder CONTENTS outside the package (leave the folder itself)
            bool testsMoved = false;
            if (Directory.Exists(_testsFolder) && HasContents(_testsFolder))
            {
                // Create backup folder
                if (Directory.Exists(_tempTestsFolder))
                {
                    Directory.Delete(_tempTestsFolder, true);
                }
                Directory.CreateDirectory(_tempTestsFolder);

                // Move all contents from Tests to backup
                MoveDirectoryContents(_testsFolder, _tempTestsFolder);
                testsMoved = true;
                UGLog.Log("[PackageExporter] Moved Tests folder contents outside package");
                
                AssetDatabase.Refresh();
            }

            try
            {
                PackRequest request = Client.Pack(_packagePath, _destinationFolder);

                while (!request.IsCompleted)
                {
                    System.Threading.Thread.Sleep(100);
                    EditorUtility.DisplayProgressBar("Packing in progress", "Please wait...", 0f);
                }

                EditorUtility.ClearProgressBar();

                if (request.Status == StatusCode.Success)
                {
                    string packageFilePath = request.Result.tarballPath;
                    UGLog.Log($"[PackageExporter] Package packed successfully: {packageFilePath}");
                }
                else
                {
                    UGLog.LogError($"[PackageExporter] Failed to pack package: {request.Error.message}");
                }
            }
            finally
            {
                // Restore Tests folder contents
                if (testsMoved && Directory.Exists(_tempTestsFolder))
                {
                    MoveDirectoryContents(_tempTestsFolder, _testsFolder);
                    Directory.Delete(_tempTestsFolder, true);
                    UGLog.Log("[PackageExporter] Restored Tests folder contents");
                    AssetDatabase.Refresh();
                }
            }
        }

        private static bool HasContents(string folderPath)
        {
            return Directory.GetFiles(folderPath).Length > 0 || Directory.GetDirectories(folderPath).Length > 0;
        }

        private static void MoveDirectoryContents(string sourceDir, string destDir)
        {
            // Move all files
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);
                File.Move(file, destFile);
            }

            // Move all subdirectories
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                string destSubDir = Path.Combine(destDir, dirName);
                Directory.Move(dir, destSubDir);
            }
        }
#endif
    }
}