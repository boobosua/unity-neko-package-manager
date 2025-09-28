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
    /// Persisted queue that survives domain reloads. Installs exactly one package when the Editor
    /// has been idle long enough, then waits for import/compile to finish before attempting the next.
    /// Emits BecameIdle only when transitioning non-empty -> empty.
    /// Compatible with Unity 2021+ and 6+.
    /// </summary>
    [InitializeOnLoad]
    internal static class NUPMInstallQueue
    {
        private const string QueueKey = "NUPM.InstallQueue.v1";

        private static readonly Queue<NUPMInstallOp> _queue;
        private static bool _processing;
        private static double _idleStart = -1;

        private static bool _wasEmpty = true;
        private static double _lastReloadAt; // timeSinceStartup at last domain reload or static init

        public static bool IsBusy => _processing || _queue.Count > 0;

        // ---- Progress events for UI (Editor thread) ----
        public static event Action<List<NUPMInstallOp>> OpsEnqueued;
        public static event Action<NUPMInstallOp> InstallStarted;
        public static event Action<NUPMInstallOp> InstallSucceeded;
        public static event Action<NUPMInstallOp, string> InstallFailed;

        public static event Action BecameIdle;

        static NUPMInstallQueue()
        {
            _queue = Load();
            _wasEmpty = _queue.Count == 0;
            _lastReloadAt = EditorApplication.timeSinceStartup; // treat static init as a reload moment

            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.update += Update;
        }

        private static void OnAfterAssemblyReload()
        {
            _lastReloadAt = EditorApplication.timeSinceStartup;
            _idleStart = -1; // require a fresh stable window
        }

        /// <summary>Returns a shallow snapshot of the current pending queue (order preserved).</summary>
        public static List<NUPMInstallOp> GetPendingSnapshot()
        {
            return new List<NUPMInstallOp>(_queue.ToArray());
        }

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
            // If empty, emit BecameIdle only once on the transition.
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

            // Settings (with safe clamps)
            var s = NUPMSettings.Instance;
            double cooldown = Math.Max(0.0, s != null ? s.postReloadCooldownSeconds : 1.5f);
            double idleNeed = Math.Max(0.2, s != null ? s.idleStableSeconds : 2.0f);

            // Respect post-reload cooldown even if Editor looks idle.
            if (EditorApplication.timeSinceStartup - _lastReloadAt < cooldown)
            {
                _idleStart = -1; // keep resetting until cooldown passes
                return;
            }

            // Wait until the editor is REALLY idle for a continuous window.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _idleStart = -1;
                return;
            }
            if (_idleStart < 0)
            {
                _idleStart = EditorApplication.timeSinceStartup;
                return;
            }
            if (EditorApplication.timeSinceStartup - _idleStart < idleNeed)
                return;

            // Start next op
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
                await PackageInstaller.InstallPackageAsync(pkg);
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
                _idleStart = -1; // require a fresh idle window before the next op
                // After this install, Unity may reload the domain; our event handler will record it.
                // Transition to empty is handled in Update() with the _wasEmpty guard.
            }
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
