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

    public static class PackageManifestHelper
    {
        private static readonly string ManifestPath = "Packages/manifest.json";
        private static readonly string LockPath = "Packages/packages-lock.json";

        public static async System.Threading.Tasks.Task<PackageManifest> ReadManifestAsync()
        {
            try
            {
                if (!File.Exists(ManifestPath))
                    return new PackageManifest();

                string jsonContent = await File.ReadAllTextAsync(ManifestPath);
                var manifest = new PackageManifest();

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
                return manifest;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NUPM] Failed to read manifest - {e.Message}");
                return new PackageManifest();
            }
        }

        public static async System.Threading.Tasks.Task<bool> IsPackageInManifestAsync(string packageName)
        {
            var manifest = await ReadManifestAsync();
            return manifest.dependencies.ContainsKey(packageName);
        }

        // Existing: version map (best-effort)
        public static Dictionary<string, string> TryReadPackagesLockVersionMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(LockPath)) return map;
                var json = File.ReadAllText(LockPath);
                var depsIdx = json.IndexOf("\"dependencies\"");
                if (depsIdx < 0) return map;
                var braceStart = json.IndexOf('{', depsIdx);
                var braceEnd = FindMatchingBrace(json, braceStart);
                if (braceStart < 0 || braceEnd < 0) return map;

                var chunk = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                var lines = chunk.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string current = null;
                foreach (var l in lines)
                {
                    var line = l.Trim();
                    if (line.StartsWith("\""))
                    {
                        var nameEnd = line.IndexOf('"', 1);
                        if (nameEnd > 1)
                            current = line.Substring(1, nameEnd - 1);
                    }
                    if (current != null && line.Contains("\"version\""))
                    {
                        var s = line.IndexOf(':');
                        if (s > 0)
                        {
                            var v = line.Substring(s + 1).Trim().Trim(',').Trim('"');
                            map[current] = v;
                            current = null;
                        }
                    }
                }
            }
            catch { /* best-effort */ }

            return map;
        }

        // NEW: best-effort git hash map from packages-lock.json
        public static Dictionary<string, string> TryReadPackagesLockGitHashMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(LockPath)) return map;
                var json = File.ReadAllText(LockPath);
                var depsIdx = json.IndexOf("\"dependencies\"");
                if (depsIdx < 0) return map;
                var braceStart = json.IndexOf('{', depsIdx);
                var braceEnd = FindMatchingBrace(json, braceStart);
                if (braceStart < 0 || braceEnd < 0) return map;

                var chunk = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                var lines = chunk.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string current = null;
                foreach (var l in lines)
                {
                    var line = l.Trim();
                    if (line.StartsWith("\""))
                    {
                        var nameEnd = line.IndexOf('"', 1);
                        if (nameEnd > 1)
                            current = line.Substring(1, nameEnd - 1);
                    }
                    if (current != null && line.IndexOf("\"hash\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var s = line.IndexOf(':');
                        if (s > 0)
                        {
                            var v = line.Substring(s + 1).Trim().Trim(',').Trim('"');
                            if (!string.IsNullOrEmpty(v))
                                map[current] = v;
                            current = null;
                        }
                    }
                }
            }
            catch { /* best-effort */ }

            return map;
        }

        private static void ParseDependencies(string depSection, Dictionary<string, string> dependencies)
        {
            var lines = depSection.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var cleaned = line.Trim().Trim(',').Trim();
                if (string.IsNullOrEmpty(cleaned)) continue;
                var colonIndex = cleaned.IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = cleaned.Substring(0, colonIndex).Trim().Trim('"');
                    var value = cleaned.Substring(colonIndex + 1).Trim().Trim('"');
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
