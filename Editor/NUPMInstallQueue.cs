#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NUPM
{
    [Serializable]
    internal class NUPMInstallOp
    {
        public string name;       // e.g., com.unity.nuget.newtonsoft-json or com.nekoindie.nekounity.lib
        public string display;    // pretty label
        public string gitUrl;     // null/empty => install by name
    }

    /// <summary>
    /// Installs exactly one package at a time. For each op we:
    /// 1) call UPM; 2) wait until the package appears in InstalledDatabase; 3) wait for continuous idle;
    /// 4) wait an extra configurable grace period; only then continue.
    /// All waits have hard timeouts to prevent infinite loops.
    /// </summary>
    [InitializeOnLoad]
    internal static class NUPMInstallQueue
    {
        private const string QueueKey = "NUPM.InstallQueue.v1";

        private static readonly Queue<NUPMInstallOp> _queue;
        private static bool _processing;
        private static double _idleStart = -1;

        private static bool _wasEmpty = true;
        private static double _lastReloadAt; // updated after assembly reload & static init

        public static bool IsBusy => _processing || _queue.Count > 0;

        // ---- UI events ----
        public static event Action<List<NUPMInstallOp>> OpsEnqueued;
        public static event Action<NUPMInstallOp> InstallStarted;
        public static event Action<NUPMInstallOp> InstallSucceeded;
        public static event Action<NUPMInstallOp, string> InstallFailed;
        public static event Action BecameIdle;

        static NUPMInstallQueue()
        {
            _queue = Load();
            _wasEmpty = _queue.Count == 0;
            _lastReloadAt = EditorApplication.timeSinceStartup;

            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.update += Update;
        }

        private static void OnAfterAssemblyReload()
        {
            _lastReloadAt = EditorApplication.timeSinceStartup;
            _idleStart = -1;
        }

        public static List<NUPMInstallOp> GetPendingSnapshot() => new List<NUPMInstallOp>(_queue.ToArray());

        public static void Enqueue(IEnumerable<NUPMInstallOp> ops)
        {
            if (ops == null) return;
            var added = new List<NUPMInstallOp>();
            foreach (var op in ops)
            {
                if (op == null) continue;
                if (string.IsNullOrEmpty(op.name) && string.IsNullOrEmpty(op.gitUrl)) continue;
                _queue.Enqueue(op);
                added.Add(op);
            }
            if (added.Count > 0)
            {
                Save(_queue);
                _wasEmpty = _queue.Count == 0;
                OpsEnqueued?.Invoke(added);
            }
        }

        public static void Clear()
        {
            _queue.Clear();
            Save(_queue);
            if (!_wasEmpty)
            {
                _wasEmpty = true;
                BecameIdle?.Invoke();
            }
        }

        private static void Update()
        {
            // Empty → emit BecameIdle once
            if (_queue.Count == 0)
            {
                if (!_wasEmpty)
                {
                    _wasEmpty = true;
                    BecameIdle?.Invoke();
                }
                return;
            }

            _wasEmpty = false;
            if (_processing) return;

            var s = NUPMSettings.Instance;
            double cooldown = Math.Max(0.0, s != null ? s.postReloadCooldownSeconds : 1.5f);
            double idleNeed = Math.Max(0.2, s != null ? s.idleStableSeconds : 2.0f);

            // Respect post-reload cooldown
            if (EditorApplication.timeSinceStartup - _lastReloadAt < cooldown)
            {
                _idleStart = -1;
                return;
            }

            // Require continuous idle
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _idleStart = -1;
                return;
            }
            if (_idleStart < 0) { _idleStart = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - _idleStart < idleNeed) return;

            // Start next
            var op = _queue.Dequeue();
            Save(_queue);
            _processing = true;
            InstallStarted?.Invoke(op);
            _ = RunInstall(op);
        }

        private static async Task RunInstall(NUPMInstallOp op)
        {
            try
            {
                var pkg = new PackageInfo
                {
                    name = string.IsNullOrEmpty(op.name) ? "(git)" : op.name,
                    displayName = string.IsNullOrEmpty(op.display) ? (string.IsNullOrEmpty(op.name) ? "(git)" : op.name) : op.display,
                    gitUrl = op.gitUrl ?? ""
                };

                // 1) Kick the install (has its own timeout)
                await PackageInstaller.InstallPackageAsync(pkg);

                // 2) Wait until the package is actually present and editor is stably idle
                string expectedName = op.name; // known for both name and git installs (registry package name)
                if (!string.IsNullOrEmpty(expectedName))
                    await WaitUntilInstalledAndIdleAsync(expectedName);

                // 3) Extra grace delay (even after idle) to avoid races in older editors
                float extra = Mathf.Max(0f, NUPMSettings.Instance != null ? NUPMSettings.Instance.extraPostInstallDelaySeconds : 1.0f);
                if (extra > 0f) await Task.Delay((int)(extra * 1000f));

                InstallSucceeded?.Invoke(op);
            }
            catch (Exception e)
            {
                Debug.LogError("[NUPM] Install failed: " + e.Message);
                InstallFailed?.Invoke(op, e.Message);
            }
            finally
            {
                _processing = false;
                _idleStart = -1; // require fresh idle before next op
            }
        }

        /// <summary>
        /// Waits until:
        ///  a) the package appears in InstalledDatabase;
        ///  b) the editor is idle continuously for idleStableSeconds.
        /// Bounded by installTimeoutSeconds to prevent infinite waits.
        /// </summary>
        private static async Task WaitUntilInstalledAndIdleAsync(string packageName)
        {
            var s = NUPMSettings.Instance;
            int overallTimeoutSec = Mathf.Max(30, s != null ? s.installTimeoutSeconds : 300); // hard lower bound
            double deadline = EditorApplication.timeSinceStartup + overallTimeoutSec;

            double idleNeeded = Math.Max(0.2, s != null ? s.idleStableSeconds : 2.0f);
            int pollMs = Mathf.Clamp(s != null ? s.requestPollIntervalMs : 80, 20, 500);

            bool present = false;
            double idleStart = -1.0;

            while (EditorApplication.timeSinceStartup < deadline)
            {
                // Is the package present yet?
                try
                {
                    var snap = await InstalledDatabase.SnapshotAsync();
                    present = snap != null && snap.ContainsKey(packageName);
                }
                catch { present = false; }

                // Are we continuously idle?
                bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
                if (!busy && present)
                {
                    if (idleStart < 0.0) idleStart = EditorApplication.timeSinceStartup;
                    if (EditorApplication.timeSinceStartup - idleStart >= idleNeeded)
                        return; // success condition met
                }
                else
                {
                    idleStart = -1.0; // reset the continuous idle window
                }

                await Task.Delay(pollMs);
            }

            // Timeout reached – log a warning and continue; next step (next install) will also be guarded.
            Debug.LogWarning($"[NUPM] Presence/idle wait for '{packageName}' exceeded timeout; continuing.");
        }

        private static Queue<NUPMInstallOp> Load()
        {
            try
            {
                string raw = EditorPrefs.GetString(QueueKey, "");
                if (string.IsNullOrEmpty(raw)) return new Queue<NUPMInstallOp>();
                var list = JsonUtility.FromJson<Wrapper>(raw);
                if (list?.items != null) return new Queue<NUPMInstallOp>(list.items);
            }
            catch { }
            return new Queue<NUPMInstallOp>();
        }

        private static void Save(Queue<NUPMInstallOp> q)
        {
            try
            {
                var w = new Wrapper { items = q.ToArray() };
                EditorPrefs.SetString(QueueKey, JsonUtility.ToJson(w));
            }
            catch { }
        }

        [Serializable] private class Wrapper { public NUPMInstallOp[] items; }
    }
}
#endif
