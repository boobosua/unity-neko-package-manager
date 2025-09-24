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
    /// Fetches Unity package metadata (package.json) directly from a GitHub URL.
    /// Supports optional "#path=..." fragment for subfolder UPM layout.
    /// </summary>
    public static class GitMetadataFetcher
    {
        // Matches https://github.com/{owner}/{repo}.git[#path=...]
        private static readonly Regex GithubRepoRx =
            new Regex(@"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/#\.]+)(?:\.git)?(?<frag>.*)$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<PackageInfo> FetchAsync(string gitUrl)
        {
            if (string.IsNullOrEmpty(gitUrl))
                throw new ArgumentException("gitUrl is null or empty");

            if (!GithubRepoRx.IsMatch(gitUrl))
                throw new NotSupportedException($"Unsupported git URL: {gitUrl}");

            var m = GithubRepoRx.Match(gitUrl);
            var owner = m.Groups["owner"].Value;
            var repo = m.Groups["repo"].Value;
            var frag = m.Groups["frag"].Value; // may contain #path=...

            var path = ExtractPathFragment(frag);
            var candidatePaths = new List<string>();
            if (!string.IsNullOrEmpty(path))
                candidatePaths.Add($"{path.TrimEnd('/')}/package.json");
            else
                candidatePaths.Add("package.json");

            var branches = new[] { "main", "master" };

            foreach (var branch in branches)
            {
                foreach (var relPath in candidatePaths)
                {
                    var raw = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{relPath}";
                    string json = await TryDownloadText(raw);
                    if (!string.IsNullOrEmpty(json))
                        return ParsePackageJson(json, gitUrl);
                }
            }

            throw new Exception($"package.json not found at expected locations for {gitUrl}");
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
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success) return null;
#else
            if (req.isNetworkError || req.isHttpError) return null;
#endif
            return req.downloadHandler.text;
        }

        private static PackageInfo ParsePackageJson(string json, string gitUrl)
        {
            string name = ExtractString(json, "\"name\"");
            string displayName = ExtractString(json, "\"displayName\"");
            string version = ExtractString(json, "\"version\"");
            string description = ExtractString(json, "\"description\"");
            var deps = ExtractDependencies(json);

            if (string.IsNullOrEmpty(displayName)) displayName = name ?? "";

            return new PackageInfo(
                name ?? "",
                displayName ?? "",
                string.IsNullOrEmpty(version) ? "0.0.0" : version,
                description ?? "",
                gitUrl,
                deps.ToArray()
            );
        }

        private static string ExtractString(string json, string key)
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
                var depName = m.Groups["name"].Value.Trim();
                if (!string.IsNullOrEmpty(depName)) list.Add(depName);
            }
            return list;
        }
    }
}
#endif
