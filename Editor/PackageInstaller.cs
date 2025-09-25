#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace NUPM
{
    internal static class PackageInstaller
    {
        private static async Task WaitFor(Request request, string opName, int timeoutMs = 120000)
        {
            var sw = Stopwatch.StartNew();
            while (!request.IsCompleted)
            {
                await Task.Delay(50);
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"{opName} timed out after {timeoutMs / 1000}s.");
            }
            if (request.Status == StatusCode.Failure)
                throw new Exception($"{opName} failed: {request.Error?.message}");
        }

        public static async Task InstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            EditorUtility.DisplayProgressBar("Installing", $"Installing {package.displayName}…", 0.5f);
            try
            {
                Request req;
                if (string.IsNullOrEmpty(package.gitUrl) && package.name.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                {
                    req = Client.Add(package.name);
                }
                else
                {
                    if (string.IsNullOrEmpty(package.gitUrl))
                        throw new ArgumentException($"No gitUrl for {package.name}");
                    var id = package.gitUrl.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
                        ? package.gitUrl : "git+" + package.gitUrl;
                    req = Client.Add(id);
                }

                await WaitFor(req, $"Install {package.name}");

#if UNITY_2020_2_OR_NEWER
                Client.Resolve(); // no assignment, just call
#endif
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (string.IsNullOrEmpty(package.name)) throw new ArgumentException("Package name is empty", nameof(package));

            EditorUtility.DisplayProgressBar("Uninstalling", $"Removing {package.displayName}…", 0.5f);
            try
            {
                var remove = Client.Remove(package.name);
                await WaitFor(remove, $"Uninstall {package.name}");

#if UNITY_2020_2_OR_NEWER
                Client.Resolve();
#endif
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
#endif
