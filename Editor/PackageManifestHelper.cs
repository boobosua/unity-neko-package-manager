#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

        /// <summary>
        /// Reads the Unity package manifest
        /// </summary>
        public static async Task<PackageManifest> ReadManifestAsync()
        {
            try
            {
                if (!File.Exists(ManifestPath))
                {
                    return new PackageManifest();
                }

                string jsonContent = await File.ReadAllTextAsync(ManifestPath);

                // Simple JSON parsing - just extract dependencies
                var manifest = new PackageManifest();
                if (jsonContent.Contains("\"dependencies\""))
                {
                    // Find dependencies section
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

        /// <summary>
        /// Checks if a package is in the manifest
        /// </summary>
        public static async Task<bool> IsPackageInManifestAsync(string packageName)
        {
            var manifest = await ReadManifestAsync();
            return manifest.dependencies.ContainsKey(packageName);
        }

        /// <summary>
        /// Simple dependency parsing
        /// </summary>
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
                    {
                        dependencies[key] = value;
                    }
                }
            }
        }

        /// <summary>
        /// Find matching closing brace
        /// </summary>
        private static int FindMatchingBrace(string json, int startIndex)
        {
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