#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NUPM
{
    public class NUPMWindow : EditorWindow
    {
        private enum Tab { Browse, Installed }

        private Tab _tab = Tab.Browse;
        private Vector2 _scroll;
        private string _search = "";

        private List<PackageInfo> _catalog = new();
        private Dictionary<string, InstalledDatabase.Installed> _installed;

        private bool _init;
        private bool _loading;

        [MenuItem("Window/NUPM")]
        public static void ShowWindow()
        {
            var w = GetWindow<NUPMWindow>("NUPM");
            w.minSize = new Vector2(720, 480);
        }

        private void OnEnable()
        {
            _ = InitializeAsync();
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
        }

        private void OnRegisteredPackages(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            _ = RefreshAsync();
        }

        private async Task InitializeAsync()
        {
            if (_init) return;
            _loading = true; Repaint();
            await RefreshAsync();
            _init = true;
            _loading = false; Repaint();
        }

        private void OnFocus() => _ = RefreshAsync();

        private async Task RefreshAsync()
        {
            _installed = await InstalledDatabase.SnapshotAsync();
            _catalog = await PackageRegistry.RefreshAsync();
            Repaint();
        }

        private void OnGUI()
        {
            if (!_init || _loading)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Loading NUPM…", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }

            DrawToolbar();
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_tab == Tab.Browse) DrawBrowse();
            else DrawInstalled();
            GUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Toggle(_tab == Tab.Browse, "Browse", EditorStyles.toolbarButton)) _tab = Tab.Browse;
                if (GUILayout.Toggle(_tab == Tab.Installed, "Installed", EditorStyles.toolbarButton)) _tab = Tab.Installed;

                GUILayout.Space(8);
                GUILayout.Label("Search:", GUILayout.Width(48));
                _search = GUILayout.TextField(_search, GUILayout.MinWidth(160));

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    _ = RefreshAsync();
            }
        }

        private IEnumerable<PackageInfo> Filter(IEnumerable<PackageInfo> src)
        {
            if (string.IsNullOrEmpty(_search)) return src;
            var s = _search.ToLowerInvariant();
            return src.Where(p =>
                (p.name?.ToLowerInvariant().Contains(s) ?? false) ||
                (p.displayName?.ToLowerInvariant().Contains(s) ?? false) ||
                (p.description?.ToLowerInvariant().Contains(s) ?? false));
        }

        private void DrawBrowse()
        {
            var list = Filter(_catalog).ToList();
            if (list.Count == 0)
            {
                EditorGUILayout.HelpBox("No packages in your registry.", MessageType.Info);
                return;
            }

            foreach (var pkg in list)
                DrawPackageCard(pkg, showUpdateBadge: true);
        }

        private void DrawInstalled()
        {
            var regNames = new HashSet<string>(_catalog.Select(c => c.name));
            var installed = _installed?.Values?.Where(i => regNames.Contains(i.name)).ToList()
                           ?? new List<InstalledDatabase.Installed>();

            if (installed.Count == 0)
            {
                EditorGUILayout.HelpBox("No custom packages installed.", MessageType.Info);
                return;
            }

            var rows = new List<(InstalledDatabase.Installed inst, PackageInfo remote, bool hasUpdate)>();
            foreach (var i in installed)
            {
                PackageInfo remote = null;
                if (PackageRegistry.TryGetByName(i.name, out var r)) remote = r;
                bool hasUpdate = remote != null && IsNewer(remote.version, i.version);
                rows.Add((i, remote, hasUpdate));
            }

            foreach (var row in rows.OrderByDescending(r => r.hasUpdate).ThenBy(r => r.inst.displayName))
            {
                if (!string.IsNullOrEmpty(_search))
                {
                    var s = _search.ToLowerInvariant();
                    if (!(row.inst.name.ToLowerInvariant().Contains(s) || row.inst.displayName.ToLowerInvariant().Contains(s)))
                        continue;
                }
                DrawInstalledCard(row.inst, row.remote, row.hasUpdate);
            }
        }

        private void DrawPackageCard(PackageInfo pkg, bool showUpdateBadge)
        {
            bool isInstalled = _installed != null && _installed.ContainsKey(pkg.name);

            // ✅ FIX: explicitly initialize 'inst' to avoid CS0165
            InstalledDatabase.Installed inst = null;
            if (_installed != null)
                _installed.TryGetValue(pkg.name, out inst);

            bool hasUpdate = showUpdateBadge && inst != null && IsNewer(pkg.version, inst.version);

            using (new GUILayout.VerticalScope("box"))
            {
                using (new GUILayout.HorizontalScope())
                {
                    var title = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                    GUILayout.Label(pkg.displayName, title);
                    GUILayout.FlexibleSpace();
                    if (hasUpdate) GUILayout.Label("Update available", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                }

                GUILayout.Label($"Name: {pkg.name}", EditorStyles.miniLabel);
                GUILayout.Label($"Version: {pkg.version}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(pkg.description))
                    GUILayout.Label(pkg.description, EditorStyles.wordWrappedMiniLabel);
                if (pkg.dependencies != null && pkg.dependencies.Count > 0)
                    GUILayout.Label($"Dependencies: {string.Join(", ", pkg.dependencies)}", EditorStyles.miniLabel);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (!isInstalled)
                    {
                        if (GUILayout.Button("Install", GUILayout.Width(90)))
                            _ = InstallWithDependenciesAsync(pkg);
                    }
                    else
                    {
                        if (hasUpdate)
                        {
                            if (GUILayout.Button("Update", GUILayout.Width(90)))
                                _ = UpdatePackageAsync(pkg);
                        }
                        if (GUILayout.Button("Uninstall", GUILayout.Width(90)))
                            _ = UninstallPackageAsync(new PackageInfo { name = pkg.name, displayName = pkg.displayName });
                    }
                }
            }
        }

        private void DrawInstalledCard(InstalledDatabase.Installed inst, PackageInfo remote, bool hasUpdate)
        {
            using (new GUILayout.VerticalScope("box"))
            {
                using (new GUILayout.HorizontalScope())
                {
                    var title = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                    GUILayout.Label(inst.displayName, title);
                    GUILayout.FlexibleSpace();
                    if (hasUpdate) GUILayout.Label("Update available", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                }

                GUILayout.Label($"Name: {inst.name}", EditorStyles.miniLabel);
                GUILayout.Label($"Installed: {inst.version} [{inst.source}]", EditorStyles.miniLabel);
                if (remote != null) GUILayout.Label($"Latest:    {remote.version}", EditorStyles.miniLabel);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (hasUpdate && remote != null)
                    {
                        if (GUILayout.Button("Update", GUILayout.Width(90)))
                            _ = UpdatePackageAsync(remote);
                    }
                    if (GUILayout.Button("Uninstall", GUILayout.Width(90)))
                        _ = UninstallPackageAsync(new PackageInfo { name = inst.name, displayName = inst.displayName });
                }
            }
        }

        private static bool IsNewer(string remote, string installed)
        {
            Version rv = SafeVer(remote);
            Version iv = SafeVer(installed);
            return rv > iv;
        }

        private static Version SafeVer(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Version(0, 0, 0);
            try { return new Version(s.Split('+', '-', ' ')[0]); }
            catch { return new Version(0, 0, 0); }
        }

        // --------------------- Install / Update / Uninstall ---------------------

        private async Task InstallWithDependenciesAsync(PackageInfo root)
        {
            try
            {
                var resolver = new DependencyResolver();
                var order = resolver.ResolveDependencies(root, _catalog);

                var toInstall = new List<PackageInfo>();
                foreach (var p in order)
                {
                    bool already = _installed != null && _installed.ContainsKey(p.name);
                    if (!already) toInstall.Add(p);
                }

                if (toInstall.Count > 0)
                {
                    var depOnly = toInstall.Where(p => p.name != root.name).ToList();
                    if (depOnly.Count > 0)
                    {
                        var names = string.Join(", ", depOnly.Select(d => d.displayName));
                        bool proceed = EditorUtility.DisplayDialog(
                            "Install Dependencies",
                            $"\"{root.displayName}\" requires:\n{names}\n\nInstall dependencies first?",
                            "Install All",
                            "Cancel");
                        if (!proceed) return;
                    }

                    for (int i = 0; i < toInstall.Count; i++)
                    {
                        var p = toInstall[i];
                        EditorUtility.DisplayProgressBar("Installing",
                            $"Installing {p.displayName} ({i + 1}/{toInstall.Count})…",
                            (float)(i + 1) / toInstall.Count);

                        await PackageInstaller.InstallPackageAsync(p);
                    }
                    EditorUtility.ClearProgressBar();
                }

                await RefreshAsync();
                EditorUtility.DisplayDialog("Success", $"{root.displayName} installed successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Installation failed:\n{e.Message}", "OK");
                Debug.LogError($"[NUPM] Install error: {e}");
            }
        }

        private async Task UpdatePackageAsync(PackageInfo latest)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Updating", $"Updating {latest.displayName}…", 0.5f);
                await PackageInstaller.InstallPackageAsync(latest);
                EditorUtility.ClearProgressBar();
                await RefreshAsync();
                EditorUtility.DisplayDialog("Success", $"{latest.displayName} updated!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Update failed:\n{e.Message}", "OK");
                Debug.LogError($"[NUPM] Update error: {e}");
            }
        }

        private async Task UninstallPackageAsync(PackageInfo package)
        {
            try
            {
                var installedKeys = _installed != null ? _installed.Keys.ToList() : new List<string>();
                var installedCustom = new HashSet<string>(installedKeys, StringComparer.OrdinalIgnoreCase);

                var dependents = _catalog
                    .Where(p => p.dependencies != null && p.dependencies.Contains(package.name))
                    .Where(p => installedCustom.Contains(p.name))
                    .ToList();

                if (dependents.Count > 0)
                {
                    var list = string.Join(", ", dependents.Select(d => d.displayName));
                    bool alsoUninstall = EditorUtility.DisplayDialog(
                        "Package is required",
                        $"{package.displayName} is required by:\n{list}\n\nUninstall dependents first?",
                        "Uninstall Dependents",
                        "Cancel");
                    if (!alsoUninstall) return;

                    for (int i = 0; i < dependents.Count; i++)
                    {
                        var dep = dependents[i];
                        EditorUtility.DisplayProgressBar("Uninstalling dependents",
                            $"Removing {dep.displayName} ({i + 1}/{dependents.Count})…",
                            (float)(i + 1) / (dependents.Count + 1));

                        await PackageInstaller.UninstallPackageAsync(new PackageInfo { name = dep.name, displayName = dep.displayName });
                    }
                    EditorUtility.ClearProgressBar();
                }

                bool proceed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    $"Uninstall {package.displayName}?",
                    "Uninstall", "Cancel");
                if (!proceed) return;

                await PackageInstaller.UninstallPackageAsync(package);
                await RefreshAsync();
                EditorUtility.DisplayDialog("Success", $"{package.displayName} uninstalled successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Uninstallation failed:\n{e.Message}", "OK");
                Debug.LogError($"[NUPM] Uninstall error: {e}");
            }
        }
    }
}
#endif
