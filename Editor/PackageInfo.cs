#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace NUPM
{
    [Serializable]
    public class PackageInfo
    {
        public string name;           // e.g. com.nekoindie.nekounity.lib
        public string displayName;    // e.g. NekoLib
        public string version;
        public string description;
        public string gitUrl;         // empty => install by name (Unity registry)
        public List<string> dependencies;

        // Remote HEAD commit SHA (for Git packages) to surface Update even if SemVer didn't bump.
        public string latestCommitSha;

        public PackageInfo()
        {
            name = "";
            displayName = "";
            version = "1.0.0";
            description = "";
            gitUrl = "";
            dependencies = new List<string>();
            latestCommitSha = null;
        }

        public PackageInfo(string name, string displayName, string version, string description, string gitUrl, params string[] deps)
        {
            this.name = name ?? "";
            this.displayName = displayName ?? "";
            this.version = version ?? "1.0.0";
            this.description = description ?? "";
            this.gitUrl = gitUrl ?? "";
            this.dependencies = new List<string>();
            if (deps != null)
            {
                foreach (var d in deps)
                    if (!string.IsNullOrEmpty(d)) this.dependencies.Add(d);
            }
            this.latestCommitSha = null;
        }

        public override string ToString() { return displayName + " (" + name + ") v" + version; }
    }
}
#endif
