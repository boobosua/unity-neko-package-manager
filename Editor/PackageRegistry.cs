#if UNITY_EDITOR
using System.Collections.Generic;

namespace NUPM
{
    public static class PackageRegistry
    {
        /// <summary>
        /// Gets all available Neko packages with their dependencies
        /// </summary>
        public static List<PackageInfo> GetAvailablePackages()
        {
            return new List<PackageInfo>
            {
                // Core library - no dependencies
                new(
                    "com.neko.lib",
                    "Neko Lib",
                    "1.0.0",
                    "Core library providing fundamental utilities, extensions, and base classes for all Neko packages.",
                    "https://github.com/boobosua/unity-nekolib.git"
                ),
                
                // Serialization system - depends on core lib and Newtonsoft JSON
                new(
                    "com.neko.serialize",
                    "Neko Serialize",
                    "1.0.0",
                    "Advanced serialization system with JSON support, custom serializers, and Unity-specific serialization utilities.",
                    "https://github.com/boobosua/unity-neko-serialize.git",
                    "com.neko.lib", "com.unity.nuget.newtonsoft-json"
                ),
                
                // Visual scripting and state management - depends on core lib
                new(
                    "com.neko.flow",
                    "Neko Flow",
                    "1.0.0",
                    "Visual scripting and state management system for Unity projects with node-based scripting and state machines.",
                    "https://github.com/boobosua/unity-neko-flow.git",
                    "com.neko.lib"
                ),
                
                // Event system - depends on core lib
                new(
                    "com.neko.signal",
                    "Neko Signal",
                    "1.0.0",
                    "Event-driven communication system with type-safe signals and observers for decoupled architecture.",
                    "https://github.com/boobosua/unity-neko-signal.git",
                    "com.neko.lib"
                )
            };
        }

        /// <summary>
        /// Gets a specific package by name
        /// </summary>
        public static PackageInfo GetPackageInfo(string packageName)
        {
            var packages = GetAvailablePackages();
            foreach (var package in packages)
            {
                if (package.name == packageName)
                    return package;
            }
            return null;
        }
    }
}
#endif