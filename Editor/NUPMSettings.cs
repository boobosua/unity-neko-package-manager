#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace NUPM
{
    /// <summary>
    /// Project-scoped, versionable settings for NUPM.
    /// </summary>
    public static class NUPMSettings
    {
        private static readonly string SettingsDir = "ProjectSettings/NUPM";
        private static readonly string SourcesPath = Path.Combine(SettingsDir, "sources.txt");

        public static List<string> LoadSources()
        {
            EnsureStorage();
            if (!File.Exists(SourcesPath)) return new List<string>();
            return File.ReadAllLines(SourcesPath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Distinct()
                    .ToList();
        }

        public static void SaveSources(IEnumerable<string> urls)
        {
            EnsureStorage();
            File.WriteAllLines(SourcesPath, urls
                .Select(u => u.Trim())
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct()
                .ToArray());
            AssetDatabase.Refresh();
        }

        public static void AddSource(string gitUrl)
        {
            var list = LoadSources();
            if (!list.Contains(gitUrl)) list.Add(gitUrl);
            SaveSources(list);
        }

        public static void RemoveSource(string gitUrl)
        {
            var list = LoadSources();
            if (list.Remove(gitUrl))
                SaveSources(list);
        }

        private static void EnsureStorage()
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }
        }
    }
}
#endif
