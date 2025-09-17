#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NUPM
{
    public class NUPMWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private string searchFilter = "";
        private List<PackageInfo> availablePackages;
        private List<PackageInfo> installedPackages;
        private Dictionary<string, bool> installInProgress;
        private bool initialized = false;

        [MenuItem("Window/NUPM")]
        public static void ShowWindow()
        {
            var window = GetWindow<NUPMWindow>("NUPM - Neko Unity Package Manager");
            window.minSize = new Vector2(600, 400);
        }

        private void OnEnable()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            if (initialized) return;
            
            availablePackages = PackageRegistry.GetAvailablePackages();
            installedPackages = new List<PackageInfo>();
            installInProgress = new Dictionary<string, bool>();
            
            foreach (var package in availablePackages)
            {
                installInProgress[package.name] = false;
            }
            
            await RefreshInstalledPackages();
            initialized = true;
            Repaint();
        }

        private void OnGUI()
        {
            if (!initialized)
            {
                GUILayout.Label("Loading NUPM...", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            DrawHeader();
            DrawSearchBar();
            DrawPackageList();
        }

        private void DrawHeader()
        {
            GUILayout.Space(10);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label("NUPM - Neko Unity Package Manager", headerStyle);
            GUILayout.Space(10);
        }

        private void DrawSearchBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(50));
            searchFilter = GUILayout.TextField(searchFilter);
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                _ = RefreshInstalledPackages();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawPackageList()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            var filteredPackages = GetFilteredPackages();
            foreach (var package in filteredPackages)
            {
                DrawPackage(package);
            }

            GUILayout.EndScrollView();
        }

        private List<PackageInfo> GetFilteredPackages()
        {
            if (string.IsNullOrEmpty(searchFilter))
                return availablePackages;

            var filtered = new List<PackageInfo>();
            string lowerSearch = searchFilter.ToLower();

            foreach (var package in availablePackages)
            {
                if (package.name.ToLower().Contains(lowerSearch) ||
                    package.displayName.ToLower().Contains(lowerSearch) ||
                    package.description.ToLower().Contains(lowerSearch))
                {
                    filtered.Add(package);
                }
            }

            return filtered;
        }

        private void DrawPackage(PackageInfo package)
        {
            var packageStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(5, 5, 5, 5)
            };

            GUILayout.BeginVertical(packageStyle);

            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label(package.displayName, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            bool isInstalled = IsPackageInstalled(package.name);
            bool inProgress = installInProgress[package.name];

            GUI.enabled = !inProgress;

            if (isInstalled)
            {
                GUI.color = Color.red;
                if (GUILayout.Button("Uninstall", GUILayout.Width(80)))
                {
                    _ = UninstallPackage(package);
                }
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.green;
                string buttonText = inProgress ? "Installing..." : "Install";
                if (GUILayout.Button(buttonText, GUILayout.Width(80)))
                {
                    _ = InstallPackage(package);
                }
                GUI.color = Color.white;
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // Info
            GUILayout.Label($"Version: {package.version}", EditorStyles.miniLabel);
            GUILayout.Label(package.description, EditorStyles.wordWrappedLabel);

            if (package.dependencies.Count > 0)
            {
                string deps = string.Join(", ", package.dependencies);
                GUILayout.Label($"Dependencies: {deps}", EditorStyles.miniLabel);
            }

            GUILayout.EndVertical();
        }

        private bool IsPackageInstalled(string packageName)
        {
            foreach (var package in installedPackages)
            {
                if (package.name == packageName)
                    return true;
            }
            return false;
        }

        private async Task RefreshInstalledPackages()
        {
            installedPackages.Clear();
            var manifest = await PackageManifestHelper.ReadManifestAsync();

            foreach (var package in availablePackages)
            {
                if (manifest.dependencies.ContainsKey(package.name))
                {
                    installedPackages.Add(package);
                }
            }

            Repaint();
        }

        private async Task InstallPackage(PackageInfo package)
        {
            try
            {
                installInProgress[package.name] = true;
                Repaint();

                var dependencyResolver = new DependencyResolver();
                var installOrder = dependencyResolver.ResolveDependencies(package, availablePackages);

                // Show dependency dialog if needed
                if (installOrder.Count > 1)
                {
                    var depNames = new List<string>();
                    for (int i = 0; i < installOrder.Count - 1; i++)
                    {
                        depNames.Add(installOrder[i].displayName);
                    }

                    string dependencyList = string.Join(", ", depNames);
                    bool proceed = EditorUtility.DisplayDialog(
                        "Install Dependencies",
                        $"Installing {package.displayName} requires:\n{dependencyList}\n\nProceed?",
                        "Install All",
                        "Cancel"
                    );

                    if (!proceed) return;
                }

                // Install packages in order
                foreach (var pkg in installOrder)
                {
                    if (!IsPackageInstalled(pkg.name))
                    {
                        EditorUtility.DisplayProgressBar("Installing", $"Installing {pkg.displayName}...", 0.5f);
                        await PackageInstaller.InstallPackageAsync(pkg);
                        await Task.Delay(500);
                    }
                }

                EditorUtility.ClearProgressBar();
                await RefreshInstalledPackages();

                EditorUtility.DisplayDialog("Success", 
                    $"{package.displayName} installed successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", 
                    $"Installation failed:\n{e.Message}", "OK");
                Debug.LogError($"NUPM: {e.Message}");
            }
            finally
            {
                installInProgress[package.name] = false;
                Repaint();
            }
        }

        private async Task UninstallPackage(PackageInfo package)
        {
            try
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "Uninstall Package",
                    $"Uninstall {package.displayName}?",
                    "Uninstall",
                    "Cancel"
                );

                if (!proceed) return;

                EditorUtility.DisplayProgressBar("Uninstalling", $"Removing {package.displayName}...", 0.5f);
                await PackageInstaller.UninstallPackageAsync(package);
                await RefreshInstalledPackages();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Success", 
                    $"{package.displayName} uninstalled successfully!", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", 
                    $"Uninstallation failed:\n{e.Message}", "OK");
                Debug.LogError($"NUPM: {e.Message}");
            }
        }
    }
}
#endif