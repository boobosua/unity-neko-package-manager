#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace NUPM
{
    internal static class PackageInstaller
    {
        private static async Task WaitFor(Request request, string opName, int timeoutMs, int pollMs)
        {
            var sw = Stopwatch.StartNew();
            int poll = Mathf.Clamp(pollMs, 20, 500); // sanity clamp

            while (!request.IsCompleted)
            {
                await Task.Delay(poll);
                if (timeoutMs > 0 && sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"{opName} timed out after {(timeoutMs / 1000)}s.");
            }
            if (request.Status == StatusCode.Failure)
                throw new Exception($"{opName} failed: {request.Error?.message ?? "unknown"}");
        }

        public static async Task InstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var s = NUPMSettings.Instance;
            int pollMs = s != null ? s.requestPollIntervalMs : 80;
            int timeoutMs = (s != null ? s.installTimeoutSeconds : 300) * 1000;

            // Unity registry (by name), e.g., Newtonsoft
            if (string.IsNullOrEmpty(package.gitUrl) &&
                package.name.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
            {
                var rName = Client.Add(package.name);
                await WaitFor(rName, $"Install {package.name}", timeoutMs, pollMs);
                return;
            }

            // Git install (latest HEAD unless URL pins tag/sha)
            if (string.IsNullOrEmpty(package.gitUrl))
                throw new ArgumentException($"No gitUrl for {package.name}");

            string id = package.gitUrl.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
                ? package.gitUrl
                : "git+" + package.gitUrl;

            var rGit = Client.Add(id);
            await WaitFor(rGit, $"Install {package.name}", timeoutMs, pollMs);
        }

        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (string.IsNullOrEmpty(package.name)) throw new ArgumentException("Package name is empty", nameof(package));

            var s = NUPMSettings.Instance;
            int pollMs = s != null ? s.requestPollIntervalMs : 80;
            int timeoutMs = (s != null ? s.uninstallTimeoutSeconds : 300) * 1000;

            var remove = Client.Remove(package.name);
            await WaitFor(remove, $"Uninstall {package.name}", timeoutMs, pollMs);
        }
    }
}
#endif
