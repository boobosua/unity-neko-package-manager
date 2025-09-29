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
        public float idleStableSeconds = 2.0f;            // continuous idle required before starting/continuing
        [Min(0f)]
        public float postReloadCooldownSeconds = 1.5f;    // guard after a domain reload before queue resumes
        [Min(0f)]
        public float extraPostInstallDelaySeconds = 1.0f; // extra wait even after idle+presence satisfied

        // ---------- UPM operation polling ----------
        [Range(20, 500)]
        public int requestPollIntervalMs = 80;            // how often to poll UPM requests & idle checks

        // ---------- UPM timeouts ----------
        [Min(1)]
        public int installTimeoutSeconds = 300;           // per-operation cap (also used by presence-wait)
        [Min(1)]
        public int uninstallTimeoutSeconds = 300;

        // ---------- UI refresh ----------
        [Min(1)]
        public int refreshTimeoutSeconds = 10;            // soft timeout for Browse/Installed refresh

        public static NUPMSettings Instance => instance;

        private void OnEnable() => hideFlags = HideFlags.HideAndDontSave;

        // Project Settings page
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
                    s.idleStableSeconds = Mathf.Max(0.2f,
                        EditorGUILayout.FloatField(new GUIContent("Idle Stable Seconds",
                            "Required continuous idle time before starting the next queued install."), s.idleStableSeconds));
                    s.postReloadCooldownSeconds = Mathf.Max(0f,
                        EditorGUILayout.FloatField(new GUIContent("Post-Reload Cooldown (s)",
                            "Extra delay after a domain reload before the queue resumes."), s.postReloadCooldownSeconds));
                    s.extraPostInstallDelaySeconds = Mathf.Max(0f,
                        EditorGUILayout.FloatField(new GUIContent("Extra Post-Install Delay (s)",
                            "Safety margin even after the editor is idle and the package is visible."), s.extraPostInstallDelaySeconds));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UPM operation polling", EditorStyles.boldLabel);
                    s.requestPollIntervalMs = Mathf.Clamp(
                        EditorGUILayout.IntSlider(new GUIContent("Request/Idle Poll Interval (ms)"),
                            s.requestPollIntervalMs, 20, 500), 20, 500);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UPM timeouts", EditorStyles.boldLabel);
                    s.installTimeoutSeconds = Mathf.Max(1,
                        EditorGUILayout.IntField(new GUIContent("Install Timeout (s)"), s.installTimeoutSeconds));
                    s.uninstallTimeoutSeconds = Mathf.Max(1,
                        EditorGUILayout.IntField(new GUIContent("Uninstall Timeout (s)"), s.uninstallTimeoutSeconds));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("UI refresh", EditorStyles.boldLabel);
                    s.refreshTimeoutSeconds = Mathf.Max(1,
                        EditorGUILayout.IntField(new GUIContent("Refresh Timeout (s)"), s.refreshTimeoutSeconds));

                    EditorGUILayout.Space(6);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Reset to Defaults", GUILayout.Width(150)))
                        {
                            s.idleStableSeconds = 2.0f;
                            s.postReloadCooldownSeconds = 1.5f;
                            s.extraPostInstallDelaySeconds = 1.0f;
                            s.requestPollIntervalMs = 80;
                            s.installTimeoutSeconds = 300;
                            s.uninstallTimeoutSeconds = 300;
                            s.refreshTimeoutSeconds = 10;
                        }
                    }

                    s.Save(true);
                }
            };
        }

        // Menu shortcut: NUPM/Settings -> opens the Project Settings page
        [MenuItem("NUPM/Settings", priority = 1)]
        private static void OpenNupmSettings()
        {
            SettingsService.OpenProjectSettings("Project/NUPM");
        }
    }
}
#endif
