#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace NUPM
{
    /// <summary>
    /// Unity 2021+ / Unity 6 safe installer.
    /// NOTE: We do not chain installs here; sequencing is handled by NUPMInstallQueue.
    /// </summary>
    internal static class PackageInstaller
    {
        private static async Task WaitFor(Request request, string opName, int timeoutMs = 120000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!request.IsCompleted)
            {
                await Task.Delay(50);
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException(opName + " timed out after " + (timeoutMs / 1000) + "s.");
            }
            if (request.Status == StatusCode.Failure)
                throw new Exception(opName + " failed: " + (request.Error != null ? request.Error.message : "unknown"));
        }

        public static async Task InstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException("package");
            // Registry (by name), e.g. Newtonsoft
            if (string.IsNullOrEmpty(package.gitUrl) &&
                package.name.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
            {
                Request rName = Client.Add(package.name);
                await WaitFor(rName, "Install " + package.name);
#if UNITY_2020_2_OR_NEWER
                Client.Resolve();
#endif
                return;
            }

            // Git install (latest HEAD unless URL pins tag/sha)
            if (string.IsNullOrEmpty(package.gitUrl))
                throw new ArgumentException("No gitUrl for " + package.name);

            string id = package.gitUrl.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
                ? package.gitUrl
                : "git+" + package.gitUrl;

            Request rGit = Client.Add(id);
            await WaitFor(rGit, "Install " + package.name);
#if UNITY_2020_2_OR_NEWER
            Client.Resolve();
#endif
        }

        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException("package");
            if (string.IsNullOrEmpty(package.name)) throw new ArgumentException("Package name is empty", "package");

            Request remove = Client.Remove(package.name);
            await WaitFor(remove, "Uninstall " + package.name);
#if UNITY_2020_2_OR_NEWER
            Client.Resolve();
#endif
        }
    }
}
#endif
