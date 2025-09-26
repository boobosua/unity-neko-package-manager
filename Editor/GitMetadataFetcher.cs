#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace NUPM
{
    /// <summary>
    /// Fetches package.json metadata from a GitHub repo URL (supports optional '#path=...').
    /// Also fetches the latest HEAD commit sha from main/master.
    /// </summary>
    internal static class GitMetadataFetcher
    {
        private static readonly Regex GithubRepoRx =
            new Regex(@"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/#\.]+)(?:\.git)?(?<frag>.*)$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<PackageInfo> FetchAsync(string gitUrl)
        {
            if (string.IsNullOrEmpty(gitUrl))
                throw new ArgumentException("gitUrl is null or empty");

            var m = GithubRepoRx.Match(gitUrl);
            if (!m.Success)
                throw new NotSupportedException($"Unsupported git URL: {gitUrl}");

            var owner = m.Groups["owner"].Value;
            var repo = m.Groups["repo"].Value;
            var frag = m.Groups["frag"].Value; // may contain '#path=...'

            var path = ExtractPathFragment(frag);
            var candidates = new List<string> { string.IsNullOrEmpty(path) ? "package.json" : $"{path.TrimEnd('/')}/package.json" };
            var branches = new[] { "main", "master" };

            string latestSha = null;

            foreach (var br in branches)
            {
                // Try to get latest HEAD commit for this branch (best-effort)
                if (latestSha == null)
                    latestSha = await TryFetchGithubHeadSha(owner, repo, br);

                foreach (var rel in candidates)
                {
                    var raw = $"https://raw.githubusercontent.com/{owner}/{repo}/{br}/{rel}";
                    var json = await TryDownloadText(raw);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var pi = ParsePackageJson(json, gitUrl);
                        pi.latestCommitSha = latestSha;
                        return pi;
                    }
                }
            }

            throw new Exception($"package.json not found for {gitUrl} (try appending '#path=Packages/<folder>')");
        }

        private static string ExtractPathFragment(string frag)
        {
            if (string.IsNullOrEmpty(frag)) return null;
            var idx = frag.IndexOf("#path=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            return frag.Substring(idx + 6).TrimStart('/');
        }

        private static async Task<string> TryDownloadText(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 12;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success) return null;
#else
                if (req.isHttpError || req.isNetworkError) return null;
#endif
                return req.downloadHandler.text;
            }
        }

        // NEW: lightweight GitHub HEAD SHA fetch (no auth; keep usage light)
        private static async Task<string> TryFetchGithubHeadSha(string owner, string repo, string branch)
        {
            try
            {
                string url = $"https://api.github.com/repos/{owner}/{repo}/commits?sha={branch}&per_page=1";
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = 8;
                    req.SetRequestHeader("User-Agent", "NUPM"); // GitHub requires a UA
                    var op = req.SendWebRequest();
                    while (!op.isDone) await Task.Yield();

#if UNITY_2020_2_OR_NEWER
                    if (req.result != UnityWebRequest.Result.Success) return null;
#else
                    if (req.isHttpError || req.isNetworkError) return null;
#endif
                    var json = req.downloadHandler.text;
                    var m = Regex.Match(json, "\"sha\"\\s*:\\s*\"([0-9a-fA-F]{40})\"");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { /* best-effort */ }
            return null;
        }

        private static PackageInfo ParsePackageJson(string json, string gitUrl)
        {
            string name = Extract(json, "\"name\"");
            string displayName = Extract(json, "\"displayName\"");
            string version = Extract(json, "\"version\"");
            string description = Extract(json, "\"description\"");
            var deps = ExtractDependencies(json);

            if (string.IsNullOrEmpty(displayName)) displayName = name ?? "";
            return new PackageInfo
            {
                name = name ?? "",
                displayName = displayName,
                version = string.IsNullOrEmpty(version) ? "0.0.0" : version,
                description = description ?? "",
                gitUrl = gitUrl,
                dependencies = deps,
                latestCommitSha = null // set by caller when known
            };
        }

        private static string Extract(string json, string key)
        {
            var rx = new Regex(key + @"\s*:\s*""(?<v>[^""]+)""", RegexOptions.IgnoreCase);
            var m = rx.Match(json);
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static List<string> ExtractDependencies(string json)
        {
            var list = new List<string>();
            var depsStart = json.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
            if (depsStart < 0) return list;

            var brace = json.IndexOf('{', depsStart);
            if (brace < 0) return list;

            int depth = 1;
            int i = brace + 1;
            for (; i < json.Length && depth > 0; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;
            }
            if (depth != 0) return list;

            var block = json.Substring(brace + 1, i - brace - 2);
            var rx = new Regex(@"""(?<name>[^""]+)""\s*:\s*""(?<ver>[^""]+)""");
            foreach (Match m in rx.Matches(block))
            {
                var dep = m.Groups["name"].Value.Trim();
                if (!string.IsNullOrEmpty(dep))
                    list.Add(dep);
            }
            return list;
        }
    }
}
#endif
