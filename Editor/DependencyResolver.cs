#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NUPM
{
    public class DependencyResolver
    {
        /// <summary>
        /// Resolve a dependency name to a PackageInfo (e.g., via fetched catalog)
        /// </summary>
        public delegate bool TryResolve(string depName, out PackageInfo pkg);

        /// <summary>
        /// Resolves dependencies and returns them in installation order (deps → root).
        /// </summary>
        public List<PackageInfo> ResolveDependencies(PackageInfo rootPackage, TryResolve resolver)
        {
            var resolved = new List<PackageInfo>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            ResolveRecursive(rootPackage, resolver, resolved, visited, visiting);
            return resolved;
        }

        private void ResolveRecursive(
            PackageInfo package,
            TryResolve resolver,
            List<PackageInfo> resolved,
            HashSet<string> visited,
            HashSet<string> visiting)
        {
            if (visited.Contains(package.name)) return;

            if (visiting.Contains(package.name))
                throw new InvalidOperationException($"Circular dependency detected involving {package.name}");

            visiting.Add(package.name);

            if (package.dependencies != null)
            {
                foreach (var depName in package.dependencies)
                {
                    if (string.IsNullOrEmpty(depName)) continue;
                    if (depName.StartsWith("com.unity.")) continue; // Unity built-ins

                    if (resolver(depName, out var depPackage))
                        ResolveRecursive(depPackage, resolver, resolved, visited, visiting);
                    else
                        Debug.LogWarning($"[NUPM] Dependency '{depName}' not found in known sources; will skip auto-install.");
                }
            }

            visiting.Remove(package.name);
            visited.Add(package.name);
            resolved.Add(package);
        }
    }
}
#endif
