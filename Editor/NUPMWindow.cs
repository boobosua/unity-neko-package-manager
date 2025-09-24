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
            var w = GetWindow<NUPMWindow>("NUPM - Neko Unity Package Manager");
            w.minSize = new Vector2(760, 480);
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
            _loading = true;
            Repaint();

            await RefreshAsync();

            _init = true;
            _loading = false;
            Repaint();
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

            DrawHeader();
            DrawToolbar();
            DrawBody();
        }

        private void DrawHeader()
        {
            GUILayout.Space(6);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
                GUILayout.Label("NUPM — Neko Unity Package Manager", style);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(6);
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

        private void DrawBody()
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            switch (_tab)
            {
                case Tab.Browse:
                    DrawBrowse();
                    break;
                case Tab.Installed:
                    DrawInstalled();
                    break;
            }

            GUILayout.EndScrollView();
        }

        private IEnumerable<PackageInfo> FilterBySearch(IEnumerable<PackageInfo> src)
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
            var list = FilterBySearch(_catalog).ToList();
            if (list.Count == 0)
            {
                EditorGUILayout.HelpBox("No custom packages found in the registry.", MessageType.Info);
                return;
            }

            foreach (var pkg in list)
                DrawPackageRow(pkg, showUpdateBadge: true);
        }

        private void DrawInstalled()
        {
            var installedInfos = _installed?.Values?.ToList() ?? new List<InstalledDatabase.Installed>();

            // Only show packages that exist in the registry
            var registryNames = new HashSet<string>(_catalog.Select(c => c.name));
            installedInfos = installedInfos.Where(i => registryNames.Contains(i.name)).ToList();

            if (installedInfos.Count == 0)
            {
                EditorGUILayout.HelpBox("No custom packages installed.", MessageType.Info);
                return;
            }

            var rows = new List<(InstalledDatabase.Installed inst, PackageInfo remote, bool hasUpdate)>();
            foreach (var inst in installedInfos)
            {
                PackageInfo remote = null;
                if (PackageRegistry.TryGetByName(inst.name, out var foundByName))
                    remote = foundByName;

                bool hasUpdate = remote != null && IsNewer(remote.version, inst.version);
                rows.Add((inst, remote, hasUpdate));
            }

            foreach (var row in rows.OrderByDescending(r => r.hasUpdate).ThenBy(r => r.inst.name))
            {
                if (!string.IsNullOrEmpty(_search))
                {
                    var s = _search.ToLowerInvariant();
                    if (!(row.inst.name.ToLowerInvariant().Contains(s) ||
                          row.inst.displayName.ToLowerInvariant().Contains(s)))
                        continue;
                }

                DrawInstalledRow(row.inst, row.remote, row.hasUpdate);
            }
        }

        // ----------- Styles -----------

        private GUIStyle GetTitleStyle()
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.4f, 0.85f, 1f)   // bright cyan for dark skin
                    : new Color(0.05f, 0.25f, 0.55f) // deep blue for light skin
                }
            };
        }

        private GUIStyle GetMetaStyle()
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                normal = { textColor = EditorStyles.label.normal.textColor }
            };
        }

        private GUIStyle GetDescriptionStyle()
        {
            return new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = EditorStyles.label.normal.textColor },
                margin = new RectOffset(0, 0, 0, 0),   // reset indent
                padding = new RectOffset(0, 0, 0, 0)   // reset indent
            };
        }

        private GUIStyle GetDependencyStyle()
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Italic,
                normal = { textColor = EditorStyles.label.normal.textColor }
            };
        }

        // ----------- Drawing -----------

        private void DrawPackageRow(PackageInfo pkg, bool showUpdateBadge)
        {
            bool isInstalled = _installed != null && _installed.ContainsKey(pkg.name);

            InstalledDatabase.Installed inst = null;
            if (_installed != null)
                _installed.TryGetValue(pkg.name, out inst);

            bool hasUpdate = showUpdateBadge && inst != null && IsNewer(pkg.version, inst.version);

            using (new GUILayout.VerticalScope("box"))
            {
                // Title
                var titleStyle = GetTitleStyle();
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(pkg.displayName, titleStyle);
                    GUILayout.FlexibleSpace();
                    if (hasUpdate)
                        GUILayout.Label("Update available", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                }

                // Metadata
                GUILayout.Label($"Name: {pkg.name}", GetMetaStyle());
                GUILayout.Label($"Version: {pkg.version}", GetMetaStyle());
                if (!string.IsNullOrEmpty(pkg.description))
                    GUILayout.Label(pkg.description, GetDescriptionStyle());
                if (pkg.dependencies != null && pkg.dependencies.Count > 0)
                    GUILayout.Label($"Dependencies: {string.Join(", ", pkg.dependencies)}", GetDependencyStyle());

                GUILayout.Space(4);

                // Buttons
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

        private void DrawInstalledRow(InstalledDatabase.Installed inst, PackageInfo remote, bool hasUpdate)
        {
            using (new GUILayout.VerticalScope("box"))
            {
                // Title
                var titleStyle = GetTitleStyle();
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(inst.displayName, titleStyle);
                    GUILayout.FlexibleSpace();
                    if (hasUpdate)
                        GUILayout.Label("Update available", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                }

                // Metadata
                GUILayout.Label($"Name: {inst.name}", GetMetaStyle());
                GUILayout.Label($"Installed: {inst.version} [{inst.source}]", GetMetaStyle());
                if (remote != null)
                    GUILayout.Label($"Latest:    {remote.version}", GetMetaStyle());

                GUILayout.Space(4);

                // Buttons
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

        // ----------- Utils -----------

        private static bool IsNewer(string remote, string installed)
        {
            System.Version rv = SafeVer(remote);
            System.Version iv = SafeVer(installed);
            return rv > iv;
        }
        private static System.Version SafeVer(string s)
        {
            if (string.IsNullOrEmpty(s)) return new System.Version(0, 0, 0);
            try
            {
                var cut = s.Split('+', '-', ' ');
                return new System.Version(cut[0]);
            }
            catch { return new System.Version(0, 0, 0); }
        }

        // ----------- Install / Update / Uninstall -----------

        private async Task InstallWithDependenciesAsync(PackageInfo root)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Preparing", $"Resolving {root.displayName}…", 0.2f);

                var resolver = new DependencyResolver();
                var installOrder = resolver.ResolveDependencies(
                    root,
                    (string depName, out PackageInfo found) => PackageRegistry.TryGetByName(depName, out found)
                );

                var missing = installOrder
                    .Where(p => p.name != root.name && (_installed == null || !_installed.ContainsKey(p.name)))
                    .ToList();

                EditorUtility.ClearProgressBar();

                if (missing.Count > 0)
                {
                    var depNames = string.Join(", ", missing.Select(d => d.displayName));
                    bool proceed = EditorUtility.DisplayDialog(
                        "Install Dependencies",
                        $"\"{root.displayName}\" requires:\n{depNames}\n\nInstall dependencies first?",
                        "Install All",
                        "Cancel"
                    );
                    if (!proceed) return;

                    for (int i = 0; i < missing.Count; i++)
                    {
                        var dep = missing[i];
                        EditorUtility.DisplayProgressBar("Installing dependencies",
                            $"Installing {dep.displayName} ({i + 1}/{missing.Count})…",
                            (float)(i + 1) / (missing.Count + 1));
                        await PackageInstaller.InstallPackageAsync(dep);
                    }
                    EditorUtility.ClearProgressBar();
                }

                EditorUtility.DisplayProgressBar("Installing", $"Installing {root.displayName}…", 0.9f);
                await PackageInstaller.InstallPackageAsync(root);
                EditorUtility.ClearProgressBar();

                await RefreshAsync();
                EditorUtility.DisplayDialog("Success", $"{root.displayName} installed successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Installation failed:\n{e.Message}", "OK");
                Debug.LogError($"NUPM: {e}");
            }
        }

        private async Task UpdatePackageAsync(PackageInfo latest)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Updating", $"Updating {latest.displayName} to {latest.version}…", 0.5f);
                await PackageInstaller.InstallPackageAsync(latest);
                EditorUtility.ClearProgressBar();
                await RefreshAsync();
                EditorUtility.DisplayDialog("Success", $"{latest.displayName} updated!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Update failed:\n{e.Message}", "OK");
                Debug.LogError($"NUPM: {e}");
            }
        }

        private async Task UninstallPackageAsync(PackageInfo package)
        {
            try
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    $"Uninstall {package.displayName}?",
                    "Uninstall",
                    "Cancel"
                );
                if (!proceed) return;

                await PackageInstaller.UninstallPackageAsync(package);
                await RefreshAsync();
                EditorUtility.DisplayDialog("Success", $"{package.displayName} uninstalled successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Uninstallation failed:\n{e.Message}", "OK");
                Debug.LogError($"NUPM: {e}");
            }
        }
    }
}
#endif
