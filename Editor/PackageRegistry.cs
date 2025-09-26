#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace NUPM
{
    /// <summary>
    /// Builds the live catalog from InternalRegistry (Git URLs) + package.json metadata.
    /// NEW: understands dependency entries that are Git URLs or package names.
    ///  - For Git URL deps: fetch metadata, add them to catalog, and add their *names* to dependency lists.
    ///  - For name deps (e.g., com.unity...): kept as-is for install-by-name.
    /// </summary>
    internal static class PackageRegistry
    {
        private static readonly Dictionary<string, PackageInfo> _byName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, PackageInfo> _byGit = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex GitUrlRx = new(
            @"^https?://github\.com/[^/]+/[^/#]+(?:\.git)?(?:#.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<List<PackageInfo>> RefreshAsync()
        {
            _byName.Clear();
            _byGit.Clear();

            var list = new List<PackageInfo>();

            // Load every top-level package
            foreach (var (gitUrl, extraDeps) in InternalRegistry.Packages)
            {
                var root = await EnsurePackageLoadedAsync(gitUrl, list);
                if (root == null) continue;

                // Merge custom deps; convert any Git URL deps into named deps after fetching
                if (extraDeps != null && extraDeps.Length > 0)
                {
                    root.dependencies ??= new List<string>();

                    foreach (var d in extraDeps)
                    {
                        if (IsGitUrl(d))
                        {
                            var depInfo = await EnsurePackageLoadedAsync(d, list);
                            if (depInfo != null)
                            {
                                // store by NAME in root deps (resolver is name-based)
                                if (!root.dependencies.Contains(depInfo.name, StringComparer.OrdinalIgnoreCase))
                                    root.dependencies.Add(depInfo.name);
                            }
                        }
                        else
                        {
                            // install-by-name (e.g. com.unity.nuget.newtonsoft-json)
                            if (!root.dependencies.Contains(d, StringComparer.OrdinalIgnoreCase))
                                root.dependencies.Add(d);
                        }
                    }
                }
            }

            return list
                .GroupBy(p => p.name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsGitUrl(string s) => !string.IsNullOrEmpty(s) && GitUrlRx.IsMatch(s);

        /// <summary>
        /// Fetch from cache or network; add to list & indexes.
        /// </summary>
        private static async Task<PackageInfo> EnsurePackageLoadedAsync(string gitUrl, List<PackageInfo> sink)
        {
            if (string.IsNullOrEmpty(gitUrl)) return null;

            if (_byGit.TryGetValue(gitUrl, out var cached))
                return cached;

            try
            {
                var info = await GitMetadataFetcher.FetchAsync(gitUrl);
                if (info == null || string.IsNullOrEmpty(info.name)) return null;

                // Dedup by name; prefer first occurrence
                if (!_byName.ContainsKey(info.name))
                {
                    _byName[info.name] = info;
                    _byGit[gitUrl] = info;
                    sink.Add(info);
                }
                else
                {
                    // still index by git for future lookups
                    _byGit[gitUrl] = _byName[info.name];
                }

                return _byName[info.name];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NUPM] Failed to fetch package.json from {gitUrl}: {e.Message}");
                return null;
            }
        }

        public static bool TryGetByName(string name, out PackageInfo pkg) => _byName.TryGetValue(name, out pkg);
        public static IEnumerable<PackageInfo> AllCached() => _byName.Values;
    }
}
#endif
