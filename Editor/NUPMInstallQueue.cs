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

    [InitializeOnLoad]
    internal static class NUPMInstallQueue
    {
        private const string QueueKey = "NUPM.InstallQueue.v1";
        private static readonly Queue<NUPMInstallOp> _queue;
        private static bool _processing;
        private static double _idleStart = -1;

        public static bool IsBusy { get { return _processing || _queue.Count > 0; } }

        // New: notify when queue becomes idle so windows can refresh
        public static event Action BecameIdle;

        static NUPMInstallQueue()
        {
            _queue = Load();
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
        }

        public static void Clear()
        {
            _queue.Clear();
            Save(_queue);
        }

        private static void Update()
        {
            if (_processing) return;

            // If queue is empty, emit idle once.
            if (_queue.Count == 0)
            {
                if (BecameIdle != null) BecameIdle();
                return;
            }

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
                var pkg = new PackageInfo();
                pkg.name = string.IsNullOrEmpty(op.name) ? "(git)" : op.name;
                pkg.displayName = string.IsNullOrEmpty(op.display) ? pkg.name : op.display;
                pkg.gitUrl = op.gitUrl ?? "";

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
                if (_queue.Count == 0 && BecameIdle != null) BecameIdle();
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
                var w = new Wrapper();
                w.items = q.ToArray();
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
