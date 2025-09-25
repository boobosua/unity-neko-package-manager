#if UNITY_EDITOR
using System.Collections.Generic;

namespace NUPM
{
    /// <summary>
    /// Private, code-only registry. Edit this list when you release/update your packages.
    /// This registry is metadata only; it does NOT auto-install anything.
    /// </summary>
    internal static class InternalRegistry
    {
        // (gitUrl, extraDependencyNames)
        // NOTE: Use Unity package names in deps (e.g. "com.neko.lib", "com.unity.nuget.newtonsoft-json")
        public static readonly List<(string gitUrl, string[] deps)> Packages = new()
        {
            // Core lib (no deps)
            ("https://github.com/boobosua/unity-nekolib.git", new string[] {}),

            // Serialize -> depends on core lib + Newtonsoft JSON (built-in)
            ("https://github.com/boobosua/unity-neko-serialize.git",
                new [] { "com.neko.lib", "com.unity.nuget.newtonsoft-json" }),

            // Signal -> depends on core lib
            ("https://github.com/boobosua/unity-neko-signal.git",
                new [] { "com.neko.lib" }),

            // Flow -> depends on core lib
            ("https://github.com/boobosua/unity-neko-flow.git",
                new [] { "com.neko.lib" }),
        };
    }
}
#endif
