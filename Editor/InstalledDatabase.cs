#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace NUPM
{
    /// <summary>
    /// Snapshot of currently installed packages (direct + indirect) from UPM.
    /// </summary>
    internal static class InstalledDatabase
    {
        internal class Installed
        {
            public string name;
            public string displayName;
            public string version;
            public string source;
            public string gitUrl;
        }

        public static async Task<Dictionary<string, Installed>> SnapshotAsync()
        {
            var dict = new Dictionary<string, Installed>();
            ListRequest req = Client.List(true);
            while (!req.IsCompleted) await Task.Delay(50);

            if (req.Status == StatusCode.Success && req.Result != null)
            {
                foreach (var p in req.Result)
                {
                    dict[p.name] = new Installed
                    {
                        name = p.name,
                        displayName = string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName,
                        version = p.version,
                        source = p.source.ToString(),
                        gitUrl = ExtractGitUrlFromPackageId(p.packageId)
                    };
                }
            }
            return dict;
        }

        private static string ExtractGitUrlFromPackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            var idx = packageId.IndexOf("git+");
            return idx >= 0 ? packageId.Substring(idx + 4) : null;
        }
    }
}
#endif
