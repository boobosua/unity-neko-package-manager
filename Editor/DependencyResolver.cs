#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NUPM
{
    /// <summary>
    /// Pure resolver: returns install order (dependencies first, then root).
    /// Does not hardcode any packages or install anything.
    /// </summary>
    internal sealed class DependencyResolver
    {
        public List<PackageInfo> ResolveDependencies(PackageInfo rootPackage, List<PackageInfo> availablePackages)
        {
            if (rootPackage == null) throw new ArgumentNullException(nameof(rootPackage));
            var resolved = new List<PackageInfo>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            // name -> PackageInfo lookup
            var map = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in availablePackages) map[p.name] = p;

            Visit(rootPackage, map, resolved, visited, visiting);
            return resolved;
        }

        private void Visit(
            PackageInfo p,
            Dictionary<string, PackageInfo> map,
            List<PackageInfo> resolved,
            HashSet<string> visited,
            HashSet<string> visiting)
        {
            if (visited.Contains(p.name)) return;
            if (visiting.Contains(p.name))
                throw new InvalidOperationException($"Circular dependency detected at '{p.name}'");

            visiting.Add(p.name);

            if (p.dependencies != null)
            {
                foreach (var depName in p.dependencies)
                {
                    // Unity built-ins are handled by UPM directly; keep them in order but no lookup error if not in registry
                    if (depName.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (map.TryGetValue(depName, out var dep))
                        Visit(dep, map, resolved, visited, visiting);
                    else
                        Debug.LogWarning($"[NUPM] Dependency '{depName}' not found in registry");
                }
            }

            visiting.Remove(p.name);
            visited.Add(p.name);
            resolved.Add(p);
        }
    }
}
#endif
