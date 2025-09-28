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
        public string display;    // optional pretty label
        public string gitUrl;     // null/empty => install by name
    }

    /// <summary>
    /// Persisted queue that survives domain reloads. Installs exactly one package when the Editor is idle,
    /// then waits for import/compile to finish before attempting the next.
    /// Emits BecameIdle **only once** when the queue transitions to empty.
    /// </summary>
    [InitializeOnLoad]
    internal static class NUPMInstallQueue
    {
        private const string QueueKey = "NUPM.InstallQueue.v1";
        private static readonly Queue<NUPMInstallOp> _queue;
        private static bool _processing;
        private static double _idleStart = -1;

        private static bool _wasEmpty = true;   // <-- transition guard

        public static bool IsBusy => _processing || _queue.Count > 0;

        /// <summary>Raised exactly once when the queue transitions from non-empty to empty.</summary>
        public static event Action BecameIdle;

        static NUPMInstallQueue()
        {
            _queue = Load();
            _wasEmpty = _queue.Count == 0;
            EditorApplication.update += Update;
        }

        public static void Enqueue(IEnumerable<NUPMInstallOp> ops)
        {
            if (ops == null) return;
            foreach (var op in ops)
            {
                if (op == null) continue;
                if (string.IsNullOrEmpty(op.name) && string.IsNullOrEmpty(op.gitUrl)) continue;
                _queue.Enqueue(op);
            }
            Save(_queue);
            _wasEmpty = _queue.Count == 0; // queue may no longer be empty
        }

        public static void Clear()
        {
            _queue.Clear();
            Save(_queue);
            // Transition to empty — fire once.
            if (!_wasEmpty)
            {
                _wasEmpty = true;
                BecameIdle?.Invoke();
            }
        }

        private static void Update()
        {
            // If empty: only notify once when transitioning to empty.
            if (_queue.Count == 0)
            {
                if (!_wasEmpty)
                {
                    _wasEmpty = true;
                    BecameIdle?.Invoke();
                }
                return;
            }

            // Queue is non-empty.
            _wasEmpty = false;

            if (_processing) return;

            // Wait until editor is idle for a short, stable period.
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
            if (EditorApplication.timeSinceStartup - _idleStart < 0.6f) return;

            // Ready to process the next op
            var op = _queue.Dequeue();
            Save(_queue);
            _processing = true;
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
            }
            catch (Exception e)
            {
                Debug.LogError("[NUPM] Install failed: " + e.Message);
            }
            finally
            {
                _processing = false;
                _idleStart = -1; // require idle again before next op

                // If we just emptied the queue, fire BecameIdle once next Update() will detect it.
                // (No-op here; transition is handled in Update with _wasEmpty guard.)
            }
        }

        private static Queue<NUPMInstallOp> Load()
        {
            try
            {
                string raw = EditorPrefs.GetString(QueueKey, "");
                if (string.IsNullOrEmpty(raw)) return new Queue<NUPMInstallOp>();

                var list = JsonUtility.FromJson<Wrapper>(raw);
                if (list != null && list.items != null) return new Queue<NUPMInstallOp>(list.items);
            }
            catch { }
            return new Queue<NUPMInstallOp>();
        }

        private static void Save(Queue<NUPMInstallOp> q)
        {
            try
            {
                var w = new Wrapper { items = q.ToArray() };
                string raw = JsonUtility.ToJson(w);
                EditorPrefs.SetString(QueueKey, raw);
            }
            catch { }
        }

        [Serializable]
        private class Wrapper { public NUPMInstallOp[] items; }
    }
}
#endif
