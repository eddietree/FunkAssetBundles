#if UNITY_EDITOR
namespace FunkAssetBundles
{
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// The script gives you choice to whether to build addressable bundles when clicking the build button.
    /// For custom build script, it will be called automatically.
    /// </summary>
    public class BuildAssetBundlesAutomatically
    {
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            //BuildPlayerWindow.RegisterBuildPlayerHandler(BuildPlayerHandler);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (AssetBundleService.USE_ASSETDATABASE)
            {
                return;
            }

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                try
                {
                    var deployFolder = AssetBundleExporter.GetBundlesDeployFolder();


                    string[] bundleDirectories = null;

                    if (Directory.Exists(deployFolder))
                    {
                        bundleDirectories = Directory.GetDirectories(deployFolder);
                    }

                    if (bundleDirectories == null || bundleDirectories.Length == 0)
                    {
                        EditorUtility.DisplayDialog("AssetBundles Missing!",
                            "AssetBundleService.USE_ASSETDATABASE == false, so AssetBundles are required to play. There are no bundles in your StreamingAssets/bundles folder. " +
                            "You need to build them before playing.\ntoolbar->AssetBundles/Build/Export (Editor)",
                            "ok daddy");
                        EditorApplication.ExitPlaymode();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void BuildPlayerHandler(BuildPlayerOptions options)
        {
            if (SystemInfo.graphicsDeviceName == null || Application.isBatchMode)
            {
                return;
            }


            try
            {
                if (options.options.HasFlag(BuildOptions.BuildScriptsOnly) || EditorUtility.DisplayDialog("Build AssetBundles first?",
                    "Do you want to build the AssetBundles before export? You can skip if nothing has changed.",
                    "Build with AssetBundles", "Skip"))
                {
                    AssetBundleExporter.DeployBundles();
                }

                OnPreprocessBuild();

                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                OnPostprocessBuild();
            }
        }

        private class MovedFolders
        {
            public string original;
            public string moved;
        }

        private static List<MovedFolders> _movedFolders = new List<MovedFolders>();

        public static void OnPostprocessBuild()
        {
            return;
            Debug.Log("Restoring non-platform asset bundles into the editor's StreamingAssets/bundles.");

            for (var i = 0; i < _movedFolders.Count; ++i)
            {
                var data = _movedFolders[i];

                // this can happen if unity recreates the folder because the meta file still exists..
                // it basically happens every time 
                if (Directory.Exists(data.original))
                {
                    Directory.Delete(data.original);
                }

                Directory.Move(data.moved, data.original);
            }

            _movedFolders.Clear();
        }

        public static void OnPreprocessBuild()
        {
            return;
            _movedFolders.Clear();

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var runtimeTarget = AssetBundleExporter.ConvertBuildTargetToRuntime(buildTarget);
            var runtimeTargetName = AssetBundleService.GetRuntimePlatformName(runtimeTarget);

            var streamingAssets = Application.streamingAssetsPath;

            var tempFolder = streamingAssets + "/../temp_bundles";
            var bundleFolder = streamingAssets + "/bundles";

            var platformBundleDirectories = Directory.GetDirectories(bundleFolder);
            for (var i = 0; i < platformBundleDirectories.Length; ++i)
            {
                var platformBundleDirectory = platformBundleDirectories[i];
                var platformBundleDirectoryName = Path.GetFileNameWithoutExtension(platformBundleDirectory);

                if (platformBundleDirectoryName != runtimeTargetName)
                {
                    var newFolder = tempFolder + $"/{platformBundleDirectoryName}";
                    if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);
                    if (Directory.Exists(newFolder)) Directory.Delete(newFolder);
                    Directory.Move(platformBundleDirectory, newFolder);

                    Debug.Log($"Temporarily removing non-platform AssetBundle from {platformBundleDirectory} to {newFolder}.");

                    _movedFolders.Add(new MovedFolders()
                    {
                        original = platformBundleDirectory,
                        moved = newFolder,
                    });
                }
            }
        }
    }
}
#endif