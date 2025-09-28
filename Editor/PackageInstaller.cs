#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace NUPM
{
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

            try
            {
                // Unity registry (by name), e.g. Newtonsoft
                if (string.IsNullOrEmpty(package.gitUrl) &&
                    package.name.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                {
                    var rName = Client.Add(package.name);
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

                var rGit = Client.Add(id);
                await WaitFor(rGit, "Install " + package.name);
#if UNITY_2020_2_OR_NEWER
                Client.Resolve();
#endif
            }
            catch (Exception ex)
            {
                string msg = ex.Message ?? "";
                if (msg.IndexOf("Expected a 'SemVer' compatible value", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("invalid dependencies", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    msg +=
                        "\n\nFix required in the package's own package.json:\n" +
                        "• Replace any dependency values that look like Git/URLs with a SemVer (e.g., \"^1.9.4\").\n" +
                        "  Example:\n" +
                        "  \"dependencies\": {\n" +
                        "    \"com.nekoindie.nekounity.lib\": \"^1.9.4\",\n" +
                        "    \"com.unity.nuget.newtonsoft-json\": \"3.2.1\"\n" +
                        "  }\n" +
                        "Git URLs are only allowed in the project's Packages/manifest.json.";
                }
                throw new Exception(msg, ex);
            }
        }

        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException("package");
            if (string.IsNullOrEmpty(package.name)) throw new ArgumentException("Package name is empty", "package");

            var remove = Client.Remove(package.name);
            await WaitFor(remove, "Uninstall " + package.name);
#if UNITY_2020_2_OR_NEWER
            Client.Resolve();
#endif
        }
    }
}
#endif
