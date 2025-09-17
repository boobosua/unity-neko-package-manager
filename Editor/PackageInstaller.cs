#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace NUPM
{
    public static class PackageInstaller
    {
        /// <summary>
        /// Installs a package
        /// </summary>
        public static async Task InstallPackageAsync(PackageInfo package)
        {
            Debug.Log($"[NUPM] Installing {package.name}...");

            AddRequest request;

            // Handle Unity built-in packages vs Git packages
            if (package.name.StartsWith("com.unity."))
            {
                request = Client.Add(package.name);
            }
            else
            {
                request = Client.Add(package.gitUrl);
            }

            // Wait for completion
            while (!request.IsCompleted)
            {
                await Task.Delay(100);
            }

            if (request.Status == StatusCode.Failure)
            {
                throw new Exception($"Failed to install {package.name}: {request.Error.message}");
            }

            Debug.Log($"[NUPM] Successfully installed {package.name}");
        }

        /// <summary>
        /// Uninstalls a package
        /// </summary>
        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            Debug.Log($"[NUPM] Uninstalling {package.name}...");

            var request = Client.Remove(package.name);

            while (!request.IsCompleted)
            {
                await Task.Delay(100);
            }

            if (request.Status == StatusCode.Failure)
            {
                throw new Exception($"Failed to uninstall {package.name}: {request.Error.message}");
            }

            Debug.Log($"[NUPM] Successfully uninstalled {package.name}");
        }
    }
}
#endif