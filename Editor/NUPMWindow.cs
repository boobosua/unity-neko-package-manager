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

        private bool _init;
        private bool _loading;
        private bool _scheduledInit;

        private Task _refreshTask;
        private bool _delayedRefreshArmed;
        private double _delayedRefreshUntil;

        private bool _bootstrapRetryHooked;
        private int _bootstrapRetriesLeft = 5;
        private double _nextRetryAt;

        private const int RefreshTimeoutMs = 10000;
        private bool _lastRefreshTimedOut;

        // If UPM fires while queue is busy, we refresh when queue is idle.
        private bool _pendingRefreshFromUPM;

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
            NUPMInstallQueue.BecameIdle += OnQueueBecameIdle;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            NUPMInstallQueue.BecameIdle -= OnQueueBecameIdle;
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
            // Keep NUPM in sync with UPM.
            if (NUPMInstallQueue.IsBusy)
            {
                _pendingRefreshFromUPM = true; // defer until queue becomes idle
                return;
            }
            RequestRefresh(0.2);
        }

        private void OnQueueBecameIdle()
        {
            if (_pendingRefreshFromUPM)
            {
                _pendingRefreshFromUPM = false;
                RequestRefresh(0.1);
            }
            else
            {
                // Still refresh once after queue completes to pick up versions/locks etc.
                RequestRefresh(0.2);
            }
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
            if (src == null) return new List<PackageInfo>();
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
                if (i != null && i.name != null)
                    PackageRegistry.TryGetByName(i.name, out remote);

                bool hasUpdate = HasUpdate(remote, i);
                InstalledRow row = new InstalledRow();
                row.Inst = i; row.Remote = remote; row.HasUpdate = hasUpdate;
                rows.Add(row);
            }

            rows.Sort(delegate (InstalledRow a, InstalledRow b)
            {
                int byUpdate = b.HasUpdate.CompareTo(a.HasUpdate);
                if (byUpdate != 0) return byUpdate;
                string an = (a.Inst != null && a.Inst.displayName != null) ? a.Inst.displayName : "";
                string bn = (b.Inst != null && b.Inst.displayName != null) ? b.Inst.displayName : "";
                return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
            });

            string s = string.IsNullOrEmpty(_search) ? null : _search.ToLowerInvariant();
            for (int r = 0; r < rows.Count; r++)
            {
                InstalledRow row = rows[r];
                if (s != null)
                {
                    string n = (row.Inst != null && row.Inst.name != null) ? row.Inst.name.ToLowerInvariant() : "";
                    string dn = (row.Inst != null && row.Inst.displayName != null) ? row.Inst.displayName.ToLowerInvariant() : "";
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
                    GUIStyle title = new GUIStyle(EditorStyles.label);
                    title.fontSize = 16; title.fontStyle = FontStyle.Bold;
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
                    GUIStyle title = new GUIStyle(EditorStyles.label);
                    title.fontSize = 16; title.fontStyle = FontStyle.Bold;
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
                            _ = UpdatePackageAsync(remote);
                    }
                    if (GUILayout.Button("Uninstall", GUILayout.Width(90)))
                        _ = UninstallPackageAsync(new PackageInfo { name = inst.name, displayName = inst.displayName });
                }
            }
        }

        // ---- Update detection (SemVer OR Git HEAD changed) ----
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
            try { return new Version(s.Split(new char[] { '+', '-', ' ' })[0]); }
            catch { return new Version(0, 0, 0); }
        }

        // ---------------- Install / Update / Uninstall ----------------

        private async Task InstallWithDependenciesAsync(PackageInfo root)
        {
            try
            {
                // Always synchronize with UPM first to avoid stale state.
                Dictionary<string, InstalledDatabase.Installed> installedNow = await InstalledDatabase.SnapshotAsync();

                // 1) Resolve Git deps (deps-first, includes root)
                DependencyResolver resolver = new DependencyResolver();
                List<PackageInfo> order = resolver.ResolveDependencies(root, _catalog);

                // 2) Merge Unity-by-name deps (e.g., Newtonsoft) from live package.json for root + each git dep
                HashSet<string> nameDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Action<PackageInfo> collectFrom = delegate (PackageInfo pkg)
                {
                    if (pkg == null || pkg.dependencies == null) return;
                    for (int i = 0; i < pkg.dependencies.Count; i++)
                    {
                        string d = pkg.dependencies[i];
                        if (!string.IsNullOrEmpty(d) && d.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                            nameDeps.Add(d);
                    }
                };

                collectFrom(root);
                for (int i = 0; i < order.Count; i++) collectFrom(order[i]);

                async Task MergeLiveDeps(PackageInfo p)
                {
                    if (p == null || string.IsNullOrEmpty(p.gitUrl)) return;
                    try
                    {
                        PackageInfo live = await GitMetadataFetcher.FetchAsync(p.gitUrl);
                        if (live != null && live.dependencies != null)
                        {
                            for (int j = 0; j < live.dependencies.Count; j++)
                            {
                                string d = live.dependencies[j];
                                if (!string.IsNullOrEmpty(d) && d.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                                    nameDeps.Add(d);
                                if (p.dependencies != null && !p.dependencies.Contains(d))
                                    p.dependencies.Add(d);
                            }
                        }
                    }
                    catch { }
                }
                await MergeLiveDeps(root);
                for (int i = 0; i < order.Count; i++) await MergeLiveDeps(order[i]);

                // 3) Compute what is actually missing RIGHT NOW (fresh UPM snapshot)
                List<PackageInfo> gitToInstall = new List<PackageInfo>();
                for (int i = 0; i < order.Count; i++)
                {
                    PackageInfo p = order[i];
                    bool already = installedNow != null && installedNow.ContainsKey(p.name);
                    if (!already) gitToInstall.Add(p);
                }

                List<string> nameDepsMissing = new List<string>();
                foreach (string n in nameDeps)
                {
                    if (installedNow == null || !installedNow.ContainsKey(n))
                        nameDepsMissing.Add(n);
                }

                // 4) Confirm: show ONLY missing items (IDs), one per line
                if (nameDepsMissing.Count > 0 || gitToInstall.Any(p => !string.Equals(p.name, root.name, StringComparison.OrdinalIgnoreCase)))
                {
                    List<string> displayList = new List<string>();
                    for (int i = 0; i < nameDepsMissing.Count; i++) displayList.Add(nameDepsMissing[i]);
                    for (int i = 0; i < gitToInstall.Count; i++)
                    {
                        PackageInfo g = gitToInstall[i];
                        if (!string.Equals(g.name, root.name, StringComparison.OrdinalIgnoreCase))
                            displayList.Add(g.name);
                    }

                    if (displayList.Count > 0)
                    {
                        string names = string.Join("\n• ", displayList.ToArray());
                        bool proceed = EditorUtility.DisplayDialog(
                            "Install Dependencies",
                            "\"" + root.displayName + "\" requires:\n• " + names + "\n\nInstall missing dependencies first?",
                            "Install All",
                            "Cancel");
                        if (!proceed) return;
                    }
                }

                // 5) Build queue: name deps first, then git deps (deps-first order preserved)
                List<NUPMInstallOp> ops = new List<NUPMInstallOp>();
                for (int i = 0; i < nameDepsMissing.Count; i++)
                {
                    string n = nameDepsMissing[i];
                    ops.Add(new NUPMInstallOp { name = n, display = n, gitUrl = null });
                }
                for (int i = 0; i < gitToInstall.Count; i++)
                {
                    PackageInfo p = gitToInstall[i];
                    ops.Add(new NUPMInstallOp { name = p.name, display = p.displayName, gitUrl = p.gitUrl });
                }

                if (ops.Count == 0)
                {
                    // All deps present; just install (or re-add) the root if missing
                    if (!(installedNow != null && installedNow.ContainsKey(root.name)))
                        ops.Add(new NUPMInstallOp { name = root.name, display = root.displayName, gitUrl = root.gitUrl });
                }

                if (ops.Count > 0)
                {
                    NUPMInstallQueue.Enqueue(ops);
                    EditorUtility.DisplayDialog("Queued",
                        "Queued " + ops.Count + " install(s). Unity will import/compile between steps automatically.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Up to date", "All required packages are already installed.", "OK");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", "Installation failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Install error: " + e);
            }
        }

        // NOTE: no 'async' modifier; returns a completed Task. This removes the analyzer warning.
        private Task UpdatePackageAsync(PackageInfo latest)
        {
            try
            {
                List<NUPMInstallOp> ops = new List<NUPMInstallOp>();
                ops.Add(new NUPMInstallOp { name = latest.name, display = latest.displayName, gitUrl = latest.gitUrl });
                NUPMInstallQueue.Enqueue(ops);
                EditorUtility.DisplayDialog("Queued", "Queued update for " + latest.displayName + ".", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", "Update failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Update error: " + e);
            }
            return Task.CompletedTask;
        }

        private async Task UninstallPackageAsync(PackageInfo package)
        {
            try
            {
                // Take a fresh snapshot so we know current dependents.
                Dictionary<string, InstalledDatabase.Installed> installedNow = await InstalledDatabase.SnapshotAsync();
                List<string> installedKeys = (installedNow != null) ? new List<string>(installedNow.Keys) : new List<string>();
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
                        await PackageInstaller.UninstallPackageAsync(new PackageInfo { name = dependents[i].name, displayName = dependents[i].displayName });
                }

                bool proceed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    "Uninstall " + package.displayName + "?",
                    "Uninstall", "Cancel");
                if (!proceed) return;

                await PackageInstaller.UninstallPackageAsync(package);
                RequestRefresh(0.3);
                EditorUtility.DisplayDialog("Success", package.displayName + " uninstalled successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", "Uninstallation failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Uninstall error: " + e);
            }
        }
    }
}
#endif
