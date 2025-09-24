#if UNITY_EDITOR
using System.Collections.Generic;

namespace NUPM
{
    /// <summary>
    /// Internal, private registry for all custom Neko packages.
    /// Edit this list only when releasing a new version of your package.
    /// </summary>
    internal static class InternalRegistry
    {
        // Tuple: (gitUrl, dependencyNames)
        public static readonly List<(string gitUrl, string[] deps)> Packages = new()
        {
            // Core library - no dependencies
            ("https://github.com/boobosua/unity-nekolib.git", new string[] { }),

            // Serialization - depends on core + Newtonsoft JSON
            ("https://github.com/boobosua/unity-neko-serialize.git", new [] { "com.neko.lib", "com.unity.nuget.newtonsoft-json" }),

            // Flow - depends on core
            ("https://github.com/boobosua/unity-neko-flow.git", new [] { "com.neko.lib" }),

            // Signal - depends on core
            ("https://github.com/boobosua/unity-neko-signal.git", new [] { "com.neko.lib" }),
        };
    }
}
#endif
