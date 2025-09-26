#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NUPM
{
    [Serializable]
    public class PackageManifest
    {
        public Dictionary<string, string> dependencies = new Dictionary<string, string>();
    }

    /// <summary>
    /// Unity 2021+ safe: uses synchronous File IO (more compatible across scripting backends),
    /// with tiny best-effort JSON parses (no JSON lib dependency).
    /// </summary>
    public static class PackageManifestHelper
    {
        private static readonly string ManifestPath = "Packages/manifest.json";
        private static readonly string LockPath = "Packages/packages-lock.json";

        public static System.Threading.Tasks.Task<PackageManifest> ReadManifestAsync()
        {
            // Synchronous read wrapped in Task.FromResult to keep call sites async-friendly and Unity 2021 safe.
            try
            {
                if (!File.Exists(ManifestPath))
                    return System.Threading.Tasks.Task.FromResult(new PackageManifest());

                string jsonContent = File.ReadAllText(ManifestPath);
                PackageManifest manifest = new PackageManifest();

                if (jsonContent.Contains("\"dependencies\""))
                {
                    int depStart = jsonContent.IndexOf("\"dependencies\"");
                    int braceStart = jsonContent.IndexOf('{', depStart);
                    int braceEnd = FindMatchingBrace(jsonContent, braceStart);
                    if (braceStart != -1 && braceEnd != -1)
                    {
                        string depSection = jsonContent.Substring(braceStart + 1, braceEnd - braceStart - 1);
                        ParseDependencies(depSection, manifest.dependencies);
                    }
                }
                return System.Threading.Tasks.Task.FromResult(manifest);
            }
            catch (Exception e)
            {
                Debug.LogError("[NUPM] Failed to read manifest - " + e.Message);
                return System.Threading.Tasks.Task.FromResult(new PackageManifest());
            }
        }

        public static async System.Threading.Tasks.Task<bool> IsPackageInManifestAsync(string packageName)
        {
            PackageManifest manifest = await ReadManifestAsync();
            return manifest.dependencies.ContainsKey(packageName);
        }

        // Version map (best-effort) — for completeness if you use it elsewhere
        public static Dictionary<string, string> TryReadPackagesLockVersionMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(LockPath)) return map;
                string json = File.ReadAllText(LockPath);
                int depsIdx = json.IndexOf("\"dependencies\"");
                if (depsIdx < 0) return map;
                int braceStart = json.IndexOf('{', depsIdx);
                int braceEnd = FindMatchingBrace(json, braceStart);
                if (braceStart < 0 || braceEnd < 0) return map;

                string chunk = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                string[] lines = chunk.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string current = null;
                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li].Trim();
                    if (line.StartsWith("\""))
                    {
                        int nameEnd = line.IndexOf('"', 1);
                        if (nameEnd > 1) current = line.Substring(1, nameEnd - 1);
                    }
                    if (current != null && line.IndexOf("\"version\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int s = line.IndexOf(':');
                        if (s > 0)
                        {
                            string v = line.Substring(s + 1).Trim().Trim(',').Trim('"');
                            map[current] = v;
                            current = null;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        // Git hash map from packages-lock.json (best-effort) — works in 2021+ / 6
        public static Dictionary<string, string> TryReadPackagesLockGitHashMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(LockPath)) return map;
                string json = File.ReadAllText(LockPath);
                int depsIdx = json.IndexOf("\"dependencies\"");
                if (depsIdx < 0) return map;
                int braceStart = json.IndexOf('{', depsIdx);
                int braceEnd = FindMatchingBrace(json, braceStart);
                if (braceStart < 0 || braceEnd < 0) return map;

                string chunk = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                string[] lines = chunk.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string current = null;
                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li].Trim();
                    if (line.StartsWith("\""))
                    {
                        int nameEnd = line.IndexOf('"', 1);
                        if (nameEnd > 1) current = line.Substring(1, nameEnd - 1);
                    }
                    if (current != null && line.IndexOf("\"hash\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int s = line.IndexOf(':');
                        if (s > 0)
                        {
                            string v = line.Substring(s + 1).Trim().Trim(',').Trim('"');
                            if (!string.IsNullOrEmpty(v)) map[current] = v;
                            current = null;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        private static void ParseDependencies(string depSection, Dictionary<string, string> dependencies)
        {
            string[] lines = depSection.Split(new char[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string cleaned = lines[i].Trim().Trim(',').Trim();
                if (string.IsNullOrEmpty(cleaned)) continue;
                int colonIndex = cleaned.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = cleaned.Substring(0, colonIndex).Trim().Trim('"');
                    string value = cleaned.Substring(colonIndex + 1).Trim().Trim('"');
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        dependencies[key] = value;
                }
            }
        }

        private static int FindMatchingBrace(string json, int startIndex)
        {
            if (startIndex < 0 || startIndex >= json.Length) return -1;
            int braceCount = 1;
            for (int i = startIndex + 1; i < json.Length; i++)
            {
                if (json[i] == '{') braceCount++;
                else if (json[i] == '}') braceCount--;
                if (braceCount == 0) return i;
            }
            return -1;
        }
    }
}
#endif
