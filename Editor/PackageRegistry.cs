#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace NUPM
{
    /// <summary>
    /// Dynamic registry that pulls package info from the internal registry.
    /// </summary>
    public static class PackageRegistry
    {
        private static readonly Dictionary<string, PackageInfo> _byName = new();
        private static readonly Dictionary<string, PackageInfo> _byGit = new();

        public static async Task<List<PackageInfo>> RefreshAsync()
        {
            _byName.Clear();
            _byGit.Clear();

            var list = new List<PackageInfo>();
            foreach (var (gitUrl, deps) in InternalRegistry.Packages)
            {
                try
                {
                    var info = await GitMetadataFetcher.FetchAsync(gitUrl);
                    if (info == null || string.IsNullOrEmpty(info.name)) continue;

                    if (deps != null && deps.Length > 0)
                    {
                        foreach (var d in deps)
                            if (!info.dependencies.Contains(d))
                                info.dependencies.Add(d);
                    }

                    list.Add(info);
                    _byName[info.name] = info;
                    _byGit[gitUrl] = info;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NUPM] Failed to fetch metadata from {gitUrl}: {e.Message}");
                }
            }

            return list;
        }

        public static bool TryGetByName(string name, out PackageInfo pkg) => _byName.TryGetValue(name, out pkg);
        public static bool TryGetByGit(string git, out PackageInfo pkg) => _byGit.TryGetValue(git, out pkg);
        public static IEnumerable<PackageInfo> AllCached() => _byName.Values;
    }
}
#endif
