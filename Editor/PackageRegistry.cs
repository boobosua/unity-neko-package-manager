#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace NUPM
{
    /// <summary>
    /// Builds the live catalog from InternalRegistry (Git URLs) + package.json metadata.
    /// </summary>
    internal static class PackageRegistry
    {
        private static readonly Dictionary<string, PackageInfo> _byName = new();
        private static readonly Dictionary<string, PackageInfo> _byGit = new();

        public static async Task<List<PackageInfo>> RefreshAsync()
        {
            _byName.Clear();
            _byGit.Clear();

            var list = new List<PackageInfo>();
            foreach (var (gitUrl, extraDeps) in InternalRegistry.Packages)
            {
                try
                {
                    var info = await GitMetadataFetcher.FetchAsync(gitUrl);
                    if (info == null || string.IsNullOrEmpty(info.name)) continue;

                    // Merge extra dependencies (from your private registry) into the read metadata
                    if (extraDeps != null && extraDeps.Length > 0)
                    {
                        info.dependencies ??= new List<string>();
                        foreach (var d in extraDeps)
                            if (!info.dependencies.Contains(d))
                                info.dependencies.Add(d);
                    }

                    list.Add(info);
                    _byName[info.name] = info;
                    _byGit[gitUrl] = info;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NUPM] Failed to fetch package.json from {gitUrl}: {e.Message}");
                }
            }

            return list.OrderBy(p => p.displayName).ToList();
        }

        public static bool TryGetByName(string name, out PackageInfo pkg) => _byName.TryGetValue(name, out pkg);
        public static IEnumerable<PackageInfo> AllCached() => _byName.Values;
    }
}
#endif
