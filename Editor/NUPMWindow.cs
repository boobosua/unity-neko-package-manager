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
        private enum OpState { Pending, Installing, Done, Failed }

        private sealed class InstalledRow
        {
            public InstalledDatabase.Installed Inst;
            public PackageInfo Remote;
            public bool HasUpdate;
        }

        private sealed class TrackOp
        {
            public string id;         // package id (name or "(git)")
            public string display;    // display name
            public OpState state;
            public string error;      // if Failed
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
        private int _bootstrapRetriesLeft = 3;  // gentle
        private double _nextRetryAt;

        private bool _lastRefreshTimedOut;
        private bool _pendingRefreshFromUPM;

        // ---- Install Queue UI tracking ----
        private readonly List<TrackOp> _tracked = new List<TrackOp>();               // visual order
        private readonly Dictionary<string, TrackOp> _byId = new Dictionary<string, TrackOp>(StringComparer.OrdinalIgnoreCase);

        // re-entrancy guard for update
        private bool _inEditorUpdate;

        [MenuItem("NUPM/Package Manager", priority = 0)]
        public static void ShowWindow()
        {
            var w = GetWindow<NUPMWindow>("NUPM");
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
            NUPMInstallQueue.OpsEnqueued += OnOpsEnqueued;
            NUPMInstallQueue.InstallStarted += OnInstallStarted;
            NUPMInstallQueue.InstallSucceeded += OnInstallSucceeded;
            NUPMInstallQueue.InstallFailed += OnInstallFailed;

            EditorApplication.update += OnEditorUpdate;

            // Adopt any pending ops that might have been queued before the window opened
            AdoptPendingSnapshot();
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            NUPMInstallQueue.BecameIdle -= OnQueueBecameIdle;
            NUPMInstallQueue.OpsEnqueued -= OnOpsEnqueued;
            NUPMInstallQueue.InstallStarted -= OnInstallStarted;
            NUPMInstallQueue.InstallSucceeded -= OnInstallSucceeded;
            NUPMInstallQueue.InstallFailed -= OnInstallFailed;

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
            // Keep NUPM in sync with UPM, but avoid refreshing mid-import.
            if (NUPMInstallQueue.IsBusy || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _pendingRefreshFromUPM = true; // defer until queue/editor becomes idle
                return;
            }
            RequestRefresh(0.3); // small debounce
        }

        private void OnQueueBecameIdle()
        {
            // Schedule a refresh when the editor is actually idle; do not run inline.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { _pendingRefreshFromUPM = true; return; }
                _pendingRefreshFromUPM = false;
                RequestRefresh(0.3);
            };
        }

        private void OnOpsEnqueued(List<NUPMInstallOp> ops)
        {
            foreach (var op in ops)
                TrackOrCreate(op.name, op.display, OpState.Pending, null);

            Repaint();
        }

        private void OnInstallStarted(NUPMInstallOp op)
        {
            var id = string.IsNullOrEmpty(op.name) ? "(git)" : op.name;
            if (_byId.TryGetValue(id, out var t)) t.state = OpState.Installing;
            else TrackOrCreate(op.name, op.display, OpState.Installing, null);
            Repaint();
        }

        private void OnInstallSucceeded(NUPMInstallOp op)
        {
            var id = string.IsNullOrEmpty(op.name) ? "(git)" : op.name;
            if (_byId.TryGetValue(id, out var t)) { t.state = OpState.Done; t.error = null; }
            else TrackOrCreate(op.name, op.display, OpState.Done, null);
            Repaint();
        }

        private void OnInstallFailed(NUPMInstallOp op, string error)
        {
            var id = string.IsNullOrEmpty(op.name) ? "(git)" : op.name;
            if (_byId.TryGetValue(id, out var t)) { t.state = OpState.Failed; t.error = error; }
            else TrackOrCreate(op.name, op.display, OpState.Failed, error);
            Repaint();
        }

        private void AdoptPendingSnapshot()
        {
            var pending = NUPMInstallQueue.GetPendingSnapshot();
            if (pending != null && pending.Count > 0)
            {
                foreach (var op in pending)
                    TrackOrCreate(op.name, op.display, OpState.Pending, null);
            }
        }

        private void TrackOrCreate(string id, string display, OpState state, string error)
        {
            string key = string.IsNullOrEmpty(id) ? "(git)" : id;
            if (_byId.TryGetValue(key, out var t))
            {
                t.state = state;
                t.error = error;
                if (!string.IsNullOrEmpty(display)) t.display = display;
                return;
            }
            var nt = new TrackOp
            {
                id = key,
                display = string.IsNullOrEmpty(display) ? key : display,
                state = state,
                error = error
            };
            _byId[key] = nt;
            _tracked.Add(nt);
        }

        private void OnFocus()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            RequestRefresh(0.2);
            TryBeginBootstrapRetries();

            AdoptPendingSnapshot();
        }

        private void OnEditorUpdate()
        {
            // No heavy work here; schedule via delayCall to keep update tick free.
            if (_inEditorUpdate) return;
            _inEditorUpdate = true;

            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return;

                double now = EditorApplication.timeSinceStartup;

                if (_delayedRefreshArmed && now >= _delayedRefreshUntil)
                {
                    _delayedRefreshArmed = false;
                    EditorApplication.delayCall += async () => await RefreshAsyncCoalesced();
                }

                if (_bootstrapRetryHooked && now >= _nextRetryAt)
                {
                    if (_catalog != null && _catalog.Count > 0)
                    {
                        _bootstrapRetryHooked = false;
                    }
                    else if (_bootstrapRetriesLeft > 0)
                    {
                        _bootstrapRetriesLeft--;
                        _nextRetryAt = now + 2.0; // slower retry
                        EditorApplication.delayCall += async () => await RefreshAsyncCoalesced();
                    }
                    else
                    {
                        _bootstrapRetryHooked = false;
                    }
                }
            }
            finally
            {
                _inEditorUpdate = false;
            }
        }

        private void RequestRefresh(double delaySeconds)
        {
            if (delaySeconds <= 0.0)
            {
                EditorApplication.delayCall += async () => await RefreshAsyncCoalesced();
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
                int timeoutMs = Mathf.Max(1000, (NUPMSettings.Instance != null ? NUPMSettings.Instance.refreshTimeoutSeconds : 10) * 1000);

                var installedTask = InstalledDatabase.SnapshotAsync();
                var catalogTask = PackageRegistry.RefreshAsync();

                Task all = Task.WhenAll(installedTask, catalogTask);
                Task completed = await Task.WhenAny(all, Task.Delay(timeoutMs));
                if (completed != all)
                {
                    _lastRefreshTimedOut = true;
                    Debug.LogWarning("[NUPM] Refresh timed out after " + (timeoutMs / 1000) + "s (network/UPM may be unavailable).");
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
            DrawInstallQueuePanel(); // visual status

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
            _bootstrapRetriesLeft = 3;
            _nextRetryAt = EditorApplication.timeSinceStartup + 2.0;
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

        // --------- Install Queue Panel ----------
        private void DrawInstallQueuePanel()
        {
            bool hasAny = _tracked.Count > 0 || NUPMInstallQueue.IsBusy;
            if (!hasAny) return;

            using (new GUILayout.VerticalScope("box"))
            {
                var header = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                GUILayout.Label("Install Queue", header);

                if (_tracked.Count == 0)
                {
                    GUILayout.Label("Waiting…", EditorStyles.miniLabel);
                    return;
                }

                foreach (var t in _tracked)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        string badge = t.state switch
                        {
                            OpState.Pending => "•",
                            OpState.Installing => "▶",
                            OpState.Done => "✓",
                            OpState.Failed => "✗",
                            _ => "•"
                        };
                        GUILayout.Label(badge, GUILayout.Width(16));
                        GUILayout.Label(t.display + "  (" + t.id + ")", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        string text = t.state switch
                        {
                            OpState.Pending => "Pending",
                            OpState.Installing => "Installing…",
                            OpState.Done => "Done",
                            OpState.Failed => "Failed",
                            _ => ""
                        };
                        GUILayout.Label(text, EditorStyles.miniBoldLabel, GUILayout.Width(80));
                    }

                    if (t.state == OpState.Failed && !string.IsNullOrEmpty(t.error))
                        EditorGUILayout.LabelField("  ↳ " + t.error, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private IEnumerable<PackageInfo> Filter(IEnumerable<PackageInfo> src)
        {
            if (src == null) return new List<PackageInfo>();
            if (string.IsNullOrEmpty(_search)) return src;
            string s = _search.ToLowerInvariant();
            return src.Where(p =>
                (!string.IsNullOrEmpty(p.name) && p.name.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrEmpty(p.displayName) && p.displayName.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrEmpty(p.description) && p.description.ToLowerInvariant().Contains(s)));
        }

        private void DrawBrowse()
        {
            var list = new List<PackageInfo>(Filter(_catalog));
            if (list.Count == 0)
            {
                string msg = _lastRefreshTimedOut
                    ? "Network/UPM may be slow or offline. Retrying automatically…"
                    : "No packages in your registry (still initializing…)";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                return;
            }

            foreach (var pkg in list)
                DrawPackageCard(pkg, true);
        }

        private void DrawInstalled()
        {
            var regNames = new HashSet<string>(_catalog.Select(c => c.name), StringComparer.OrdinalIgnoreCase);
            var installedList =
                (_installed != null && _installed.Values != null)
                ? new List<InstalledDatabase.Installed>(_installed.Values).FindAll(i => i != null && regNames.Contains(i.name))
                : new List<InstalledDatabase.Installed>();

            if (installedList.Count == 0)
            {
                EditorGUILayout.HelpBox("No custom packages installed.", MessageType.Info);
                return;
            }

            var rows = new List<InstalledRow>();
            foreach (var i in installedList)
            {
                PackageInfo remote = null;
                if (i != null && i.name != null)
                    PackageRegistry.TryGetByName(i.name, out remote);

                bool hasUpdate = HasUpdate(remote, i);
                rows.Add(new InstalledRow { Inst = i, Remote = remote, HasUpdate = hasUpdate });
            }

            rows.Sort((a, b) =>
            {
                int byUpdate = b.HasUpdate.CompareTo(a.HasUpdate);
                if (byUpdate != 0) return byUpdate;
                string an = a.Inst?.displayName ?? "";
                string bn = b.Inst?.displayName ?? "";
                return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
            });

            string s = string.IsNullOrEmpty(_search) ? null : _search.ToLowerInvariant();
            foreach (var row in rows)
            {
                if (s != null)
                {
                    string n = row.Inst?.name?.ToLowerInvariant() ?? "";
                    string dn = row.Inst?.displayName?.ToLowerInvariant() ?? "";
                    if (!(n.Contains(s) || dn.Contains(s))) continue;
                }
                DrawInstalledCard(row.Inst, row.Remote, row.HasUpdate);
            }
        }

        private void DrawPackageCard(PackageInfo pkg, bool showUpdateBadge)
        {
            InstalledDatabase.Installed inst = null;
            bool isInstalled = false;
            if (_installed != null)
            {
                isInstalled = _installed.ContainsKey(pkg.name);
                _installed.TryGetValue(pkg.name, out inst);
            }

            bool hasUpdate = showUpdateBadge && HasUpdate(pkg, inst);

            using (new GUILayout.VerticalScope("box"))
            {
                using (new GUILayout.HorizontalScope())
                {
                    var title = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
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
                    var title = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
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
            if (IsVersionNewer(remote.version, inst.version)) return true;

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

        // ---------------- Install / Update / Uninstall ----------------

        private async Task InstallWithDependenciesAsync(PackageInfo root)
        {
            try
            {
                // Advisory only if package.json contains non-SemVer dependency values.
                try
                {
                    string bad = await GitMetadataFetcher.FindNonSemverDependencyAsync(root.gitUrl);
                    if (!string.IsNullOrEmpty(bad))
                        Debug.LogWarning($"[NUPM] '{root.displayName}' contains a non-SemVer dependency in package.json: {bad}");
                }
                catch { /* advisory only */ }

                // Always synchronize with UPM before figuring out what is missing.
                Dictionary<string, InstalledDatabase.Installed> installedNow = await InstalledDatabase.SnapshotAsync();

                // 1) Resolve Git deps (deps-first, includes root)
                DependencyResolver resolver = new DependencyResolver();
                List<PackageInfo> order = resolver.ResolveDependencies(root, _catalog);

                // 2) Merge Unity-by-name deps from live package.json for root + each git dep
                HashSet<string> nameDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void CollectFrom(PackageInfo pkg)
                {
                    if (pkg == null || pkg.dependencies == null) return;
                    foreach (var d in pkg.dependencies)
                        if (!string.IsNullOrEmpty(d) && d.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase))
                            nameDeps.Add(d);
                }
                CollectFrom(root);
                foreach (var p in order) CollectFrom(p);

                async Task MergeLiveDeps(PackageInfo p)
                {
                    if (p == null || string.IsNullOrEmpty(p.gitUrl)) return;
                    try
                    {
                        PackageInfo live = await GitMetadataFetcher.FetchAsync(p.gitUrl);
                        if (live != null && live.dependencies != null)
                        {
                            foreach (var d in live.dependencies)
                            {
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
                foreach (var p in order) await MergeLiveDeps(p);

                // 3) Compute what is actually missing RIGHT NOW
                List<PackageInfo> gitToInstall = new List<PackageInfo>();
                foreach (var p in order)
                {
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
                bool needsAny = nameDepsMissing.Count > 0 ||
                                gitToInstall.Exists(pp => !string.Equals(pp.name, root.name, StringComparison.OrdinalIgnoreCase));
                if (needsAny)
                {
                    List<string> displayList = new List<string>();
                    displayList.AddRange(nameDepsMissing);
                    foreach (var g in gitToInstall)
                        if (!string.Equals(g.name, root.name, StringComparison.OrdinalIgnoreCase))
                            displayList.Add(g.name);

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

                // 5) Queue: name deps first, then git deps (deps-first order preserved)
                List<NUPMInstallOp> ops = new List<NUPMInstallOp>();
                foreach (var n in nameDepsMissing)
                    ops.Add(new NUPMInstallOp { name = n, display = n, gitUrl = null });

                foreach (var p in gitToInstall)
                    ops.Add(new NUPMInstallOp { name = p.name, display = p.displayName, gitUrl = p.gitUrl });

                if (ops.Count == 0)
                {
                    // All deps present; just install (or re-add) the root if missing
                    if (!(installedNow != null && installedNow.ContainsKey(root.name)))
                        ops.Add(new NUPMInstallOp { name = root.name, display = root.displayName, gitUrl = root.gitUrl });
                }

                if (ops.Count > 0)
                {
                    NUPMInstallQueue.Enqueue(ops);
                    // no popup; the Install Queue panel reflects progress live
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

        private Task UpdatePackageAsync(PackageInfo latest)
        {
            try
            {
                var ops = new List<NUPMInstallOp>
                {
                    new NUPMInstallOp { name = latest.name, display = latest.displayName, gitUrl = latest.gitUrl }
                };
                NUPMInstallQueue.Enqueue(ops);
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
                Dictionary<string, InstalledDatabase.Installed> installedNow = await InstalledDatabase.SnapshotAsync();
                HashSet<string> installedCustom = new HashSet<string>((installedNow ?? new Dictionary<string, InstalledDatabase.Installed>()).Keys,
                                                                      StringComparer.OrdinalIgnoreCase);

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

                    foreach (var dep in dependents)
                        await PackageInstaller.UninstallPackageAsync(new PackageInfo { name = dep.name, displayName = dep.displayName });
                }

                bool proceed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    "Uninstall " + package.displayName + "?",
                    "Uninstall", "Cancel");
                if (!proceed) return;

                await PackageInstaller.UninstallPackageAsync(package);
                await AwaitEditorIdleAsync(2.0, 30.0);
                await RefreshAsyncCoalesced();

                EditorUtility.DisplayDialog("Success", package.displayName + " uninstalled successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", "Uninstallation failed:\n" + e.Message, "OK");
                Debug.LogError("[NUPM] Uninstall error: " + e);
            }
        }

        private static async Task AwaitEditorIdleAsync(double stableSeconds, double timeoutSeconds)
        {
            double stableStart = -1.0;
            double tEnd = EditorApplication.timeSinceStartup + Math.Max(1.0, timeoutSeconds);

            while (EditorApplication.timeSinceStartup < tEnd)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    stableStart = -1.0;
                }
                else
                {
                    if (stableStart < 0.0) stableStart = EditorApplication.timeSinceStartup;
                    if (EditorApplication.timeSinceStartup - stableStart >= Math.Max(0.2, stableSeconds))
                        return;
                }
                await Task.Delay(80);
            }
        }
    }
}
#endif
