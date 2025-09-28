#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NUPM
{
    /// <summary>
    /// Editor-only settings stored in ProjectSettings/NUPMSettings.asset.
    /// Not included in player builds.
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

        private void OnEnable()
        {
            // Hide the asset; ScriptableSingleton handles persistence in ProjectSettings
            hideFlags = HideFlags.HideAndDontSave;
        }

        // Project Settings page under Project/NUPM
        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/NUPM", SettingsScope.Project)
            {
                label = "NUPM",
                guiHandler = _ =>
                {
                    var s = Instance;

                    EditorGUILayout.HelpBox("NUPM – Timing & Timeout Settings (Editor-only)", MessageType.Info);
                    EditorGUILayout.Space(4);

                    EditorGUILayout.LabelField("Queue cadence", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    s.idleStableSeconds = Mathf.Max(0.2f,
                        EditorGUILayout.FloatField(new GUIContent("Idle Stable Seconds",
                            "Required continuous idle time before starting the next queued install."), s.idleStableSeconds));
                    s.postReloadCooldownSeconds = Mathf.Max(0f,
                        EditorGUILayout.FloatField(new GUIContent("Post-Reload Cooldown (s)",
                            "Extra delay after a domain reload before queue resumes."), s.postReloadCooldownSeconds));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UPM operation polling", EditorStyles.boldLabel);
                    s.requestPollIntervalMs = Mathf.Clamp(
                        EditorGUILayout.IntSlider(new GUIContent("Request Poll Interval (ms)",
                            "How often to poll Unity Package Manager requests."), s.requestPollIntervalMs, 20, 500),
                        20, 500);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UPM timeouts", EditorStyles.boldLabel);
                    s.installTimeoutSeconds = Mathf.Max(1,
                        EditorGUILayout.IntField(new GUIContent("Install Timeout (s)",
                            "Max time to wait for a single install before timing out."), s.installTimeoutSeconds));
                    s.uninstallTimeoutSeconds = Mathf.Max(1,
                        EditorGUILayout.IntField(new GUIContent("Uninstall Timeout (s)",
                            "Max time to wait for a single uninstall before timing out."), s.uninstallTimeoutSeconds));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UI refresh", EditorStyles.boldLabel);
                    s.refreshTimeoutSeconds = Mathf.Max(1,
                        EditorGUILayout.IntField(new GUIContent("Refresh Timeout (s)",
                            "Soft timeout for Browse/Installed refresh; avoids hanging when offline."), s.refreshTimeoutSeconds));

                    EditorGUILayout.Space(6);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Reset to Defaults", GUILayout.Width(150)))
                        {
                            ResetToDefaults(s);
                        }
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        s.Save(true); // persist to ProjectSettings/NUPMSettings.asset
                        // Make sure the Inspector reflects saved values immediately
                        RepaintAllInspectors();
                    }
                }
            };
        }

        // Menu shortcut: NUPM/Settings -> opens the Project Settings page
        [MenuItem("NUPM/Settings", priority = 1)]
        private static void OpenNupmSettings()
        {
            SettingsService.OpenProjectSettings("Project/NUPM");
        }

        private static void ResetToDefaults(NUPMSettings s)
        {
            s.idleStableSeconds = 2.0f;
            s.postReloadCooldownSeconds = 1.5f;
            s.requestPollIntervalMs = 80;
            s.installTimeoutSeconds = 300;
            s.uninstallTimeoutSeconds = 300;
            s.refreshTimeoutSeconds = 10;
            s.Save(true);
            RepaintAllInspectors();
        }

        private static void RepaintAllInspectors()
        {
            // Nicety so changes show immediately in Project Settings panel
            foreach (var win in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (win != null) win.Repaint();
            }
        }
    }
}
#endif
