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

        private sealed class InstalledRow
        {
            public InstalledDatabase.Installed Inst;
            public PackageInfo Remote;
            public bool HasUpdate;
        }

        private Tab _tab = Tab.Browse;
        private Vector2 _scroll;
        private string _search = string.Empty;

        private List<PackageInfo> _catalog = new List<PackageInfo>();
        private Dictionary<string, InstalledDatabase.Installed> _installed =
            new Dictionary<string, InstalledDatabase.Installed>(StringComparer.OrdinalIgnoreCase);

        private bool _init;             // first init done
        private bool _loading;          // init in progress
        private bool _scheduledInit;    // avoid double delayCall

        private Task _refreshTask;      // single-flight refresh
        private bool _delayedRefreshArmed;
        private double _delayedRefreshUntil;

        private bool _bootstrapRetryHooked;
        private int _bootstrapRetriesLeft = 5;
        private double _nextRetryAt;

        private const int RefreshTimeoutMs = 10000;
        private bool _lastRefreshTimedOut;

        [MenuItem("Window/NUPM")]
        public static void ShowWindow()
        {
            NUPMWindow w = GetWindow<NUPMWindow>("NUPM");
            w.minSize = new Vector2(720f, 480f);
        }

        private void OnEnable()
        {
            if (!_scheduledInit)
            {
                _scheduledInit = true;
                EditorApplication.delayCall += OnDelayCallInit;
            }

            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            EditorApplication.update -= OnEditorUpdate;
            _bootstrapRetryHooked = false;
        }

        private async void OnDelayCallInit()
        {
            if (this == null) return;
            await InitializeAsync();
            TryBeginBootstrapRetries();
        }

        private async Task InitializeAsync()
        {
            if (_init || _loading) return;
            _loading = true; Repaint();

            await RefreshAsyncCoalesced();

            _init = true;
            _loading = false; Repaint();
        }

        private void OnRegisteredPackages(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            RequestRefresh(0.2);
        }

        private void OnFocus()
        {
            RequestRefresh(0.2);
            TryBeginBootstrapRetries();
        }

        private void OnEditorUpdate()
        {
            if (_delayedRefreshArmed && EditorApplication.timeSinceStartup >= _delayedRefreshUntil)
            {
                _delayedRefreshArmed = false;
                _ = RefreshAsyncCoalesced();
            }

            if (_bootstrapRetryHooked && EditorApplication.timeSinceStartup >= _nextRetryAt)
            {
                if (_catalog != null && _catalog.Count > 0)
                {
                    _bootstrapRetryHooked = false;
                }
                else if (_bootstrapRetriesLeft > 0)
                {
                    _bootstrapRetriesLeft--;
                    _nextRetryAt = EditorApplication.timeSinceStartup + 0.6;
                    _ = RefreshAsyncCoalesced();
                }
                else
                {
                    _bootstrapRetryHooked = false;
                }
            }
        }

        private void RequestRefresh(double delaySeconds)
        {
            if (delaySeconds <= 0.0)
            {
                _ = RefreshAsyncCoalesced();
                return;
            }
            _delayedRefreshArmed = true;
            _delayedRefreshUntil = EditorApplication.timeSinceStartup + delaySeconds;
        }

        private Task RefreshAsyncCoalesced()
        {
            if (_refreshTask != null && !_refreshTask.IsCompleted)
                return _refreshTask;

            _refreshTask = RefreshAsyncInternal();
            return _refreshTask;
        }

        private async Task RefreshAsyncInternal()
        {
            try
            {
                Task<Dictionary<string, InstalledDatabase.Installed>> installedTask = InstalledDatabase.SnapshotAsync();
                Task<List<PackageInfo>> catalogTask = PackageRegistry.RefreshAsync();

                Task all = Task.WhenAll(installedTask, catalogTask);
                Task completed = await Task.WhenAny(all, Task.Delay(RefreshTimeoutMs));
                if (completed != all)
                {
                    _lastRefreshTimedOut = true;
                    Debug.LogWarning("[NUPM] Refresh timed out after 10s (network/UPM may be unavailable).");
                    return;
                }

                _installed = installedTask.Result ?? new Dictionary<string, InstalledDatabase.Installed>(StringComparer.OrdinalIgnoreCase);
                _catalog = catalogTask.Result ?? new List<PackageInfo>();
                _lastRefreshTimedOut = false;
            }
            catch (Exception e)
            {
                _lastRefreshTimedOut = false;
                Debug.LogWarning("[NUPM] Refresh failed: " + e.Message);
            }
            finally
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (!_init)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(_loading ? "Loading NUPM…" : "Initializing…", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }

            DrawToolbar();
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_tab == Tab.Browse) DrawBrowse();
            else DrawInstalled();
            GUILayout.EndScrollView();

            TryBeginBootstrapRetries();
        }

        private void TryBeginBootstrapRetries()
        {
            if (_catalog != null && _catalog.Count > 0) { _bootstrapRetryHooked = false; return; }
            if (_bootstrapRetryHooked) return;

            _bootstrapRetryHooked = true;
            _bootstrapRetriesLeft = 5;
            _nextRetryAt = EditorApplication.timeSinceStartup + 0.6;
        }

        private void DrawToolbar()
        {
            using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Toggle(_tab == Tab.Browse, "Browse", EditorStyles.toolbarButton)) _tab = Tab.Browse;
                if (GUILayout.Toggle(_tab == Tab.Installed, "Installed", EditorStyles.toolbarButton)) _tab = Tab.Installed;

                GUILayout.Space(8);
                GUILayout.Label("Search:", GUILayout.Width(48));
                _search = GUILayout.TextField(_search ?? string.Empty, GUILayout.MinWidth(160));

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    RequestRefresh(0.0);
            }
        }

        private IEnumerable<PackageInfo> Filter(IEnumerable<PackageInfo> src)
        {
            if (src == null) return Enumerable.Empty<PackageInfo>();
            if (string.IsNullOrEmpty(_search)) return src;
            string s = _search.ToLowerInvariant();
            return src.Where(p =>
                ((p.name != null) && p.name.ToLowerInvariant().Contains(s)) ||
                ((p.displayName != null) && p.displayName.ToLowerInvariant().Contains(s)) ||
                ((p.description != null) && p.description.ToLowerInvariant().Contains(s)));
        }

        private void DrawBrowse()
        {
            List<PackageInfo> list = new List<PackageInfo>(Filter(_catalog));
            if (list.Count == 0)
            {
                string msg = _lastRefreshTimedOut
                    ? "Network/UPM may be slow or offline. Retrying automatically…"
                    : "No packages in your registry (still initializing…)";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                return;
            }

            foreach (PackageInfo pkg in list)
                DrawPackageCard(pkg, true);
        }

        private void DrawInstalled()
        {
            HashSet<string> regNames = new HashSet<string>(_catalog.Select(c => c.name), StringComparer.OrdinalIgnoreCase);
            List<InstalledDatabase.Installed> installedList =
                (_installed != null && _installed.Values != null)
                ? _installed.Values.Where(i => i != null && regNames.Contains(i.name)).ToList()
                : new List<InstalledDatabase.Installed>();

            if (installedList.Count == 0)
            {
                EditorGUILayout.HelpBox("No custom packages installed.", MessageType.Info);
                return;
            }

            List<InstalledRow> rows = new List<InstalledRow>();
            for (int idx = 0; idx < installedList.Count; idx++)
            {
                InstalledDatabase.Installed i = installedList[idx];
                PackageInfo remote = null;
                if (i != null && i.name != null && PackageRegistry.TryGetByName(i.name, out remote)) { }

                bool hasUpdate = HasUpdate(remote, i);
                rows.Add(new InstalledRow { Inst = i, Remote = remote, HasUpdate = hasUpdate });
            }

            rows.Sort((a, b) =>
            {
                int byUpdate = b.HasUpdate.CompareTo(a.HasUpdate);
                if (byUpdate != 0) return byUpdate;
                string an = (a.Inst != null && a.Inst.displayName != null) ? a.Inst.displayName : string.Empty;
                string bn = (b.Inst != null && b.Inst.displayName != null) ? b.Inst.displayName : string.Empty;
                return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
            });

            string s = string.IsNullOrEmpty(_search) ? null : _search.ToLowerInvariant();
            foreach (InstalledRow row in rows)
            {
                if (s != null)
                {
                    string n = (row.Inst != null && row.Inst.name != null) ? row.Inst.name.ToLowerInvariant() : string.Empty;
                    string dn = (row.Inst != null && row.Inst.displayName != null) ? row.Inst.displayName.ToLowerInvariant() : string.Empty;
                    if (!(n.Contains(s) || dn.Contains(s))) continue;
                }
                DrawInstalledCard(row.Inst, row.Remote, row.HasUpdate);
            }
        }

        private void DrawPackageCard(PackageInfo pkg, bool showUpdateBadge)
        {
            bool isInstalled = (_installed != null) && _installed.ContainsKey(pkg.name);
            InstalledDatabase.Installed inst;
            if (_installed == null || !_installed.TryGetValue(pkg.name, out inst)) inst = null;

            bool hasUpdate = showUpdateBadge && HasUpdate(pkg, inst);

            using (new GUILayout.VerticalScope("box"))
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUIStyle title = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                    GUILayout.Label(pkg.displayName, title);
                    GUILayout.FlexibleSpace();
                    if (hasUpdate) GUILayout.Label("Update available", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                }

                GUILayout.Label("Name: " + pkg.name, EditorStyles.miniLabel);
                GUILayout.Label("Version: " + pkg.version, EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(pkg.description))
                    GUILayout.Label(pkg.description, EditorStyles.wordWrappedMiniLabel);
                if (pkg.dependencies != null && pkg.dependencies.Count > 0)
                    GUILayout.Label("Dependencies: " + string.Join(", ", pkg.dependencies.ToArray()), EditorStyles.miniLabel);

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
                    GUIStyle title = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                    GUILayout.Label(inst.displayName, title);
                    GUILayout.FlexibleSpace();
                    if (hasUpdate) GUILayout.Label("Update available", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                }

                GUILayout.Label("Name: " + inst.name, EditorStyles.miniLabel);
                GUILayout.Label("Installed: " + inst.version + " [" + inst.source + "]", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(inst.gitHash))
                    GUILayout.Label("Commit:   " + inst.gitHash, EditorStyles.miniLabel);
                if (remote != null) GUILayout.Label("Latest:    " + remote.version, EditorStyles.miniLabel);
                if (remote != null && !string.IsNullOrEmpty(remote.latestCommitSha))
                    GUILayout.Label("Latest SHA: " + remote.latestCommitSha, EditorStyles.miniLabel);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (hasUpdate && remote != null)
                    {
                        if (GUILayout.Button("Update", GUILayout.Width(90)))
                            _ = UpdatePackageAsync(remote); // re-Add pulls latest HEAD
                    }
                    if (GUILayout.Button("Uninstall", GUILayout.Width(90)))
                        _ = UninstallPackageAsync(new PackageInfo { name = inst.name, displayName = inst.displayName });
                }
            }
        }

        // ----- Update detection helpers (version OR git commit change) -----
        private static bool HasUpdate(PackageInfo remote, InstalledDatabase.Installed inst)
        {
            if (remote == null || inst == null) return false;

            if (IsVersionNewer(remote.version, inst.version))
                return true;

            bool isGit = !string.IsNullOrEmpty(remote.gitUrl) || !string.IsNullOrEmpty(inst.gitUrl);
            if (isGit)
            {
                if (!string.IsNullOrEmpty(remote.latestCommitSha) &&
                    !string.IsNullOrEmpty(inst.gitHash) &&
                    !string.Equals(remote.latestCommitSha, inst.gitHash, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsVersionNewer(string remote, string installed)
        {
            Version rv = SafeVer(remote);
            Version iv = SafeVer(installed);
            return rv > iv;
        }

        private static Version SafeVer(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Version(0, 0, 0);
            try { return new Version(s.Split(new[] { '+', '-', ' ' })[0]); }
            catch { return new Version(0, 0, 0); }
        }

        // --------------------- Install / Update / Uninstall ---------------------

        private async Task InstallWithDependenciesAsync(PackageInfo root)
        {
            try
            {
                // Your existing flow: resolve and prompt using EditorUtility.DisplayDialog
                DependencyResolver resolver = new DependencyResolver();
                List<PackageInfo> order = resolver.ResolveDependencies(root, _catalog);

                List<PackageInfo> toInstall = new List<PackageInfo>();
                for (int i = 0; i < order.Count; i++)
                {
                    PackageInfo p = order[i];
                    bool already = (_installed != null) && _installed.ContainsKey(p.name);
                    if (!already) toInstall.Add(p);
                }

                if (toInstall.Count > 0)
                {
                    List<PackageInfo> depOnly = toInstall.Where(p => !string.Equals(p.name, root.name, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (depOnly.Count > 0)
                    {
                        string names = string.Join(", ", depOnly.Select(d => d.displayName).ToArray());
                        bool proceed = EditorUtility.DisplayDialog(
                            "Install Dependencies",
                            "\"" + root.displayName + "\" requires:\n" + names + "\n\nInstall dependencies first?",
                            "Install All",
                            "Cancel");
                        if (!proceed) return;
                    }

                    for (int i = 0; i < toInstall.Count; i++)
                    {
                        PackageInfo p = toInstall[i];
                        EditorUtility.DisplayProgressBar("Installing",
                            "Installing " + p.displayName + " (" + (i + 1) + "/" + toInstall.Count + ")…",
                            (float)(i + 1) / (float)toInstall.Count);

                        await PackageInstaller.InstallPackageAsync(p); // Git install pulls latest HEAD
                    }
                    EditorUtility.ClearProgressBar();
                }

                RequestRefresh(0.1);
                EditorUtility.DisplayDialog("Success", root.displayName + " installed successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", "Installation failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Install error: " + e);
            }
        }

        private async Task UpdatePackageAsync(PackageInfo latest)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Updating", "Updating " + latest.displayName + "…", 0.5f);
                await PackageInstaller.InstallPackageAsync(latest); // re-Add pulls latest HEAD
                EditorUtility.ClearProgressBar();
                RequestRefresh(0.1);
                EditorUtility.DisplayDialog("Success", latest.displayName + " updated!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", "Update failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Update error: " + e);
            }
        }

        private async Task UninstallPackageAsync(PackageInfo package)
        {
            try
            {
                List<string> installedKeys = (_installed != null) ? new List<string>(_installed.Keys) : new List<string>();
                HashSet<string> installedCustom = new HashSet<string>(installedKeys, StringComparer.OrdinalIgnoreCase);

                List<PackageInfo> dependents = _catalog
                    .Where(p => p.dependencies != null && p.dependencies.Contains(package.name))
                    .Where(p => installedCustom.Contains(p.name))
                    .ToList();

                if (dependents.Count > 0)
                {
                    string list = string.Join(", ", dependents.Select(d => d.displayName).ToArray());
                    bool alsoUninstall = EditorUtility.DisplayDialog(
                        "Package is required",
                        package.displayName + " is required by:\n" + list + "\n\nUninstall dependents first?",
                        "Uninstall Dependents",
                        "Cancel");
                    if (!alsoUninstall) return;

                    for (int i = 0; i < dependents.Count; i++)
                    {
                        PackageInfo dep = dependents[i];
                        EditorUtility.DisplayProgressBar("Uninstalling dependents",
                            "Removing " + dep.displayName + " (" + (i + 1) + "/" + dependents.Count + ")…",
                            (float)(i + 1) / (float)(dependents.Count + 1));

                        await PackageInstaller.UninstallPackageAsync(new PackageInfo { name = dep.name, displayName = dep.displayName });
                    }
                    EditorUtility.ClearProgressBar();
                }

                bool proceed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    "Uninstall " + package.displayName + "?",
                    "Uninstall", "Cancel");
                if (!proceed) return;

                await PackageInstaller.UninstallPackageAsync(package);
                RequestRefresh(0.1);
                EditorUtility.DisplayDialog("Success", package.displayName + " uninstalled successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", "Uninstallation failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Uninstall error: " + e);
            }
        }
    }
}
#endif
