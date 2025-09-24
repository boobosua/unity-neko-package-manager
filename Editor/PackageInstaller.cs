#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace NUPM
{
    public static class PackageInstaller
    {
        public static async Task InstallPackageAsync(PackageInfo package)
        {
            Debug.Log($"[NUPM] Installing {package.name}...");
            AddRequest request;
            if (!string.IsNullOrEmpty(package.name) && package.name.StartsWith("com.unity."))
            {
                request = Client.Add(package.name);
            }
            else
            {
                request = Client.Add(package.gitUrl);
            }

            float t = 0f;
            while (!request.IsCompleted)
            {
                t = Mathf.Repeat(t + 0.05f, 1f);
                EditorUtility.DisplayProgressBar("Installing", $"Installing {package.displayName}…", t);
                await Task.Delay(100);
            }
            EditorUtility.ClearProgressBar();

            if (request.Status == StatusCode.Failure)
                throw new Exception($"Failed to install {package.name}: {request.Error.message}");

            Debug.Log($"[NUPM] Successfully installed {package.name}");
        }

        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            Debug.Log($"[NUPM] Uninstalling {package.name}...");
            var request = Client.Remove(package.name);

            float t = 0f;
            while (!request.IsCompleted)
            {
                t = Mathf.Repeat(t + 0.05f, 1f);
                EditorUtility.DisplayProgressBar("Uninstalling", $"Removing {package.displayName}…", t);
                await Task.Delay(100);
            }
            EditorUtility.ClearProgressBar();

            if (request.Status == StatusCode.Failure)
                throw new Exception($"Failed to uninstall {package.name}: {request.Error.message}");

            Debug.Log($"[NUPM] Successfully uninstalled {package.name}");
        }
    }
}
#endif
