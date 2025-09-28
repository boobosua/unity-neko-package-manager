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
    /// Reads package.json (and HEAD sha) from GitHub gitUrl.
    /// Supports optional '#path=Packages/XXX' fragment.
    /// Unity 2021+ / Unity 6 safe.
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

            Match m = GithubRepoRx.Match(gitUrl);
            if (!m.Success)
                throw new NotSupportedException("Unsupported git URL: " + gitUrl);

            string owner = m.Groups["owner"].Value;
            string repo = m.Groups["repo"].Value;
            string frag = m.Groups["frag"].Value;

            string path = ExtractPathFragment(frag);
            List<string> candidates = new List<string>();
            candidates.Add(string.IsNullOrEmpty(path) ? "package.json" : path.TrimEnd('/') + "/package.json");

            string[] branches = new string[] { "main", "master" };
            string latestSha = null;

            for (int bi = 0; bi < branches.Length; bi++)
            {
                string br = branches[bi];

                if (latestSha == null)
                    latestSha = await TryFetchGithubHeadSha(owner, repo, br);

                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    string rel = candidates[ci];
                    string raw = "https://raw.githubusercontent.com/" + owner + "/" + repo + "/" + br + "/" + rel;
                    string json = await TryDownloadText(raw);
                    if (!string.IsNullOrEmpty(json))
                    {
                        PackageInfo pi = ParsePackageJson(json, gitUrl);
                        pi.latestCommitSha = latestSha;
                        return pi;
                    }
                }
            }

            throw new Exception("package.json not found for " + gitUrl + " (try appending '#path=Packages/<folder>')");
        }

        private static string ExtractPathFragment(string frag)
        {
            if (string.IsNullOrEmpty(frag)) return null;
            int idx = frag.IndexOf("#path=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            return frag.Substring(idx + 6).TrimStart('/');
        }

        private static async Task<string> TryDownloadText(string url)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 12;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success) return null;
                return req.downloadHandler.text;
            }
        }

        // Lightweight HEAD commit fetch (no auth) — Unity 2021+ safe.
        private static async Task<string> TryFetchGithubHeadSha(string owner, string repo, string branch)
        {
            try
            {
                string url = "https://api.github.com/repos/" + owner + "/" + repo + "/commits?sha=" + branch + "&per_page=1";
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.timeout = 8;
                    req.SetRequestHeader("User-Agent", "NUPM");
                    var op = req.SendWebRequest();
                    while (!op.isDone) await Task.Yield();

                    if (req.result != UnityWebRequest.Result.Success) return null;
                    string json = req.downloadHandler.text;
                    Match m = Regex.Match(json, "\"sha\"\\s*:\\s*\"([0-9a-fA-F]{40})\"");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        private static PackageInfo ParsePackageJson(string json, string gitUrl)
        {
            string name = Extract(json, "\"name\"");
            string displayName = Extract(json, "\"displayName\"");
            string version = Extract(json, "\"version\"");
            string description = Extract(json, "\"description\"");
            List<string> deps = ExtractDependencies(json);

            if (string.IsNullOrEmpty(displayName)) displayName = name ?? "";
            PackageInfo pi = new PackageInfo();
            pi.name = name ?? "";
            pi.displayName = displayName;
            pi.version = string.IsNullOrEmpty(version) ? "0.0.0" : version;
            pi.description = description ?? "";
            pi.gitUrl = gitUrl;
            pi.dependencies = deps;
            pi.latestCommitSha = null; // assigned by caller above
            return pi;
        }

        private static string Extract(string json, string key)
        {
            Regex rx = new Regex(key + "\\s*:\\s*\"(?<v>[^\"]+)\"", RegexOptions.IgnoreCase);
            Match m = rx.Match(json);
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static List<string> ExtractDependencies(string json)
        {
            List<string> list = new List<string>();
            int depsStart = json.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
            if (depsStart < 0) return list;

            int brace = json.IndexOf('{', depsStart);
            if (brace < 0) return list;

            int depth = 1;
            int i = brace + 1;
            for (; i < json.Length && depth > 0; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;
            }
            if (depth != 0) return list;

            string block = json.Substring(brace + 1, i - brace - 2);
            Regex rx = new Regex("\"(?<name>[^\"]+)\"\\s*:\\s*\"(?<ver>[^\"]+)\"");
            MatchCollection ms = rx.Matches(block);
            foreach (Match m in ms)
            {
                string dep = m.Groups["name"].Value.Trim();
                if (!string.IsNullOrEmpty(dep))
                    list.Add(dep);
            }
            return list;
        }
    }
}
#endif
