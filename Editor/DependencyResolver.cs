#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NUPM
{
    public class DependencyResolver
    {
        /// <summary>
        /// Resolves dependencies and returns them in installation order
        /// </summary>
        public List<PackageInfo> ResolveDependencies(PackageInfo rootPackage, List<PackageInfo> availablePackages)
        {
            var resolved = new List<PackageInfo>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            // Create lookup for faster access
            var packageLookup = new Dictionary<string, PackageInfo>();
            foreach (var pkg in availablePackages)
            {
                packageLookup[pkg.name] = pkg;
            }

            ResolveDependenciesRecursive(rootPackage, packageLookup, resolved, visited, visiting);
            return resolved;
        }

        private void ResolveDependenciesRecursive(PackageInfo package, Dictionary<string, PackageInfo> packageLookup,
            List<PackageInfo> resolved, HashSet<string> visited, HashSet<string> visiting)
        {
            if (visited.Contains(package.name)) return;

            if (visiting.Contains(package.name))
            {
                throw new InvalidOperationException($"Circular dependency detected involving {package.name}");
            }

            visiting.Add(package.name);

            // Resolve dependencies first
            if (package.dependencies != null)
            {
                foreach (var depName in package.dependencies)
                {
                    // Skip Unity built-in packages
                    if (depName.StartsWith("com.unity.")) continue;

                    if (packageLookup.TryGetValue(depName, out PackageInfo depPackage))
                    {
                        ResolveDependenciesRecursive(depPackage, packageLookup, resolved, visited, visiting);
                    }
                    else
                    {
                        Debug.LogWarning($"[NUPM] Dependency '{depName}' not found");
                    }
                }
            }

            visiting.Remove(package.name);
            visited.Add(package.name);
            resolved.Add(package);
        }
    }
}
#endif