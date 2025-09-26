#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace NUPM
{
    /// <summary>
    /// Grabs installed packages and (best-effort) git hash from packages-lock.json.
    /// Unity 2021+ / Unity 6 safe.
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
            public string gitHash; // from packages-lock.json if available
        }

        public static async Task<Dictionary<string, Installed>> SnapshotAsync()
        {
            Dictionary<string, Installed> dict =
                new Dictionary<string, Installed>(System.StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> hashMap = PackageManifestHelper.TryReadPackagesLockGitHashMap();

            ListRequest req = Client.List(true);
            while (!req.IsCompleted) await Task.Delay(50);

            if (req.Status == StatusCode.Success && req.Result != null)
            {
                foreach (UnityEditor.PackageManager.PackageInfo p in req.Result)
                {
                    Installed inst = new Installed();
                    inst.name = p.name;
                    inst.displayName = string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName;
                    inst.version = p.version;
                    inst.source = p.source.ToString();
                    inst.gitUrl = ExtractGitUrlFromPackageId(p.packageId);
                    inst.gitHash = null;

                    if (hashMap != null && hashMap.TryGetValue(p.name, out string h))
                        inst.gitHash = h;

                    dict[p.name] = inst;
                }
            }
            return dict;
        }

        private static string ExtractGitUrlFromPackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            int idx = packageId.IndexOf("git+");
            return idx >= 0 ? packageId.Substring(idx + 4) : null;
        }
    }
}
#endif
