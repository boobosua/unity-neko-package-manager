#if UNITY_EDITOR
using System.Collections.Generic;

namespace NUPM
{
    /// <summary>
    /// Private, code-only registry for your packages.
    /// Dependencies can be either:
    ///  - Git URLs for your custom packages, or
    ///  - Package names for install-by-name (e.g., com.unity.nuget.newtonsoft-json)
    /// </summary>
    internal static class InternalRegistry
    {
        // (gitUrl, deps) where deps are either git URLs OR package names.
        public static readonly List<(string gitUrl, string[] deps)> Packages = new()
        {
            // Core lib (no deps)
            ("https://github.com/boobosua/unity-nekolib.git",
                new string[] {}),

            // Serialize -> depends on core lib (by Git URL) + Newtonsoft JSON (by name)
            ("https://github.com/boobosua/unity-neko-serialize.git",
                new[]
                {
                    "https://github.com/boobosua/unity-nekolib.git",
                    "com.unity.nuget.newtonsoft-json"
                }),

            // Signal -> depends on core lib
            ("https://github.com/boobosua/unity-neko-signal.git",
                new[]
                {
                    "https://github.com/boobosua/unity-nekolib.git",
                }),

            // Flow -> depends on core lib
            ("https://github.com/boobosua/unity-neko-flow.git",
                new[]
                {
                    "https://github.com/boobosua/unity-nekolib.git",
                }),
        };
    }
}
#endif
