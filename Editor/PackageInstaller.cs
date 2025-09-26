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
    /// - Uses Client.Add("git+...") for Git HEAD installs.
    /// - Waits for Editor to finish compiling/updating after each operation (prevents "Running Backend" deadlocks).
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

        private static async Task WaitForEditorToSettle(string context, int timeoutMs = 240000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            await Task.Delay(100);
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                await Task.Delay(100);
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException(context + ": Editor did not finish import/compile within " + (timeoutMs / 1000) + "s.");
            }
        }

        public static async Task InstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException("package");
            EditorUtility.DisplayProgressBar("Installing", "Installing " + package.displayName + "…", 0.5f);
            try
            {
                // Unity registry (by name), e.g. Newtonsoft
                if (string.IsNullOrEmpty(package.gitUrl) &&
                    package.name.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                {
                    Request rName = Client.Add(package.name);
                    await WaitFor(rName, "Install " + package.name);
#if UNITY_2020_2_OR_NEWER
                    Client.Resolve();
#endif
                    await WaitForEditorToSettle("Install " + package.name);
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
                await WaitForEditorToSettle("Install " + package.name);
            }
            catch (Exception ex)
            {
                string msg = (ex.Message ?? "");
                if (msg.IndexOf("Expected a 'SemVer' compatible value", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    msg +=
                        "\n\nHint: A package's own package.json lists a Git URL in \"dependencies\". UPM requires a SemVer version there.\n" +
                        "Change it to a version (e.g., \"1.9.4\") and let NUPM pre-install Git deps in the project manifest.";
                }
                throw new Exception(msg, ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static async Task UninstallPackageAsync(PackageInfo package)
        {
            if (package == null) throw new ArgumentNullException("package");
            if (string.IsNullOrEmpty(package.name)) throw new ArgumentException("Package name is empty", "package");

            EditorUtility.DisplayProgressBar("Uninstalling", "Removing " + package.displayName + "…", 0.5f);
            try
            {
                Request remove = Client.Remove(package.name);
                await WaitFor(remove, "Uninstall " + package.name);
#if UNITY_2020_2_OR_NEWER
                Client.Resolve();
#endif
                await WaitForEditorToSettle("Uninstall " + package.name);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
