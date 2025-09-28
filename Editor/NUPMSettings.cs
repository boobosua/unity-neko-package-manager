#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NUPM
{
    /// <summary>
    /// Editor-only settings stored in ProjectSettings/NUPMSettings.asset.
    /// This file is never compiled into player builds.
    /// </summary>
    internal sealed class NUPMSettings : ScriptableSingleton<NUPMSettings>
    {
        // ---------- Queue cadence (between package operations) ----------
        [Min(0.2f)]
        public float idleStableSeconds = 2.0f;          // continuous idle required before starting next op

        [Min(0f)]
        public float postReloadCooldownSeconds = 1.5f;  // extra settling delay after domain reload

        // ---------- UPM operation polling ----------
        [Range(20, 500)]
        public int requestPollIntervalMs = 80;          // how often we poll PackageManager requests

        // ---------- UPM timeouts ----------
        [Min(1)]
        public int installTimeoutSeconds = 300;         // max wait for install before timeout (seconds)

        [Min(1)]
        public int uninstallTimeoutSeconds = 300;       // max wait for uninstall before timeout (seconds)

        // ---------- UI refresh ----------
        [Min(1)]
        public int refreshTimeoutSeconds = 10;          // soft timeout for Browse/Installed refresh (seconds)

        /// <summary>Convenience alias so other editor scripts can keep calling NUPMSettings.Instance.</summary>
        public static NUPMSettings Instance => instance;

        void OnEnable() => hideFlags = HideFlags.HideAndDontSave;

        // Draw a Project Settings page under Project/NUPM
        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Project/NUPM", SettingsScope.Project)
            {
                label = "NUPM",
                guiHandler = _ =>
                {
                    var s = Instance;
                    var so = new SerializedObject(s);

                    EditorGUILayout.HelpBox("NUPM – Timing & Timeout Settings (Editor-only)", MessageType.Info);
                    EditorGUILayout.Space(4);

                    EditorGUILayout.LabelField("Queue cadence", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(idleStableSeconds)), new GUIContent("Idle Stable Seconds"));
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(postReloadCooldownSeconds)), new GUIContent("Post-Reload Cooldown (s)"));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UPM operation polling", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(requestPollIntervalMs)), new GUIContent("Request Poll Interval (ms)"));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UPM timeouts", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(installTimeoutSeconds)), new GUIContent("Install Timeout (s)"));
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(uninstallTimeoutSeconds)), new GUIContent("Uninstall Timeout (s)"));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UI refresh", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(refreshTimeoutSeconds)), new GUIContent("Refresh Timeout (s)"));

                    if (so.ApplyModifiedProperties())
                        s.Save(true); // persist to ProjectSettings/NUPMSettings.asset
                }
            };
            return provider;
        }
    }
}
#endif
