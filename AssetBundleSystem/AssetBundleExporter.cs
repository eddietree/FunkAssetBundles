#pragma warning disable CS0162 // Unreachable code detected

namespace FunkAssetBundles
{
    using System.Reflection;
    using System.IO;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.Callbacks;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using System.Linq;
#endif

    // asset bundles are built locally to library cache folder
    // final builds will 'deploy' them to streaming assets before building

    public class AssetBundleExporter
        : MonoBehaviour
    {
        /// <summary>
        /// If none of the bundles are using the "No Dependency" flag, then do not remove bundle metadata from unity assets after we are finished building bundles. 
        /// This is to improve build times, on projects with a lot of assets with long import times. 
        /// If you have bundles with "no dependency" toggled on, this flag does nothing, and things will still take a long time to import.
        /// </summary>
        public const bool IF_NO_ZERO_DEPENDENCY_BUNDLES_DONT_REMOVE_BUNDLE_TAGS = true; 

        /// <summary>
        /// If true, data safety stuff will be ran before every bundle build. For example, duplicate asset references will automatically be removed from bundles. 
        /// </summary>
        public const bool RUN_SAFETY_SCANS_ON_BUILD = true; 

#if UNITY_EDITOR
        public static string GetBundlesBuildFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../FunkAssetBundlesCache"));
        }

        public static string GetBundlesDeployFolder()
        {
            return AssetBundleService.GetBundlesDeployFolder();
        }

        [MenuItem("Build System/Asset Bundles/Helper/Force Reserialize All .meta")]
        public static void UnityForceReserializeAllAssets()
        {
            var confirm = EditorUtility.DisplayDialog("Confirm", "Are you sure you want reserialize EVERYTHING? It will take a very long time.", "Yes", "No");
            if (confirm)
            {
                var guids = AssetDatabase.FindAssets("*");

                var confirm2 = EditorUtility.DisplayDialog("Confirm", string.Format("Proceed with touching {0} assets?", guids.Length), "Yes", "No");
                if (confirm2)
                {
                    var paths = new List<string>(guids.Length);

                    foreach (var guid in guids)
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));

                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    AssetDatabase.StartAssetEditing();
                    AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeMetadata);
                    AssetDatabase.StopAssetEditing();
                    stopwatch.Stop();
                    Debug.LogFormat("Reserialized all assets in {0} seconds", stopwatch.Elapsed.Seconds);
                }
            }
        }

        [MenuItem("Build System/Asset Bundles/Clean (Delete)")]
        public static void CleanBundlesMenu()
        {
            CleanBundles(false);
        }

        public static void CleanBundles(bool force = true)
        {
            if (Application.isPlaying != false)
            {
                Debug.LogError("Cannot export in play mode");
                return;
            }

            var confirmed = force;

            if (force == false)
            {
                confirmed = EditorUtility.DisplayDialog("Confirm", "Delete all exported bundles?", "Delete", "Cancel");
            }

            if (confirmed)
            {
                var bundlesFolder = GetBundlesBuildFolder();
                var bundlesFolderMeta = $"{bundlesFolder}.meta";

                Debug.LogFormat("AssetBundleExporter.CleanBundles: cleaning bundles at {0}...", bundlesFolder);

                if (Directory.Exists(bundlesFolder))
                    Directory.Delete(bundlesFolder, true);
                if (File.Exists(bundlesFolderMeta))
                    File.Delete(bundlesFolderMeta);
            }

            AssetDatabase.Refresh();
        }

        [MenuItem("Build System/Asset Bundles/Build")]
        public static void BuildBundles()
        {
            if (Application.isPlaying != false)
            {
                Debug.LogError("Cannot build in play mode");
                return;
            }

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var isServer = GetIsPlayerServer();
            
            Debug.Log($"Exporting bundles. Current build target: {buildTarget}");

            // active build target 
            BuildBundlesForTarget(buildTarget, isDedicatedServer: isServer);
        }

        public static bool GetIsPlayerServer()
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;

            var isServer = false;

            if (buildTarget == BuildTarget.StandaloneWindows || buildTarget == BuildTarget.StandaloneWindows64 || buildTarget == BuildTarget.StandaloneLinux64 || buildTarget == BuildTarget.LinuxHeadlessSimulation)
            {
                isServer = EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
            }

            return isServer; 
        }

        [MenuItem("Build System/Asset Bundles/Build (force full rebuild)")]
        public static void BuildBundlesForceFull()
        {
            if (Application.isPlaying != false)
            {
                Debug.LogError("Cannot build in play mode");
                return;
            }

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var isServer = GetIsPlayerServer();

            Debug.Log($"Exporting bundles. Current build target: {buildTarget}");

            // active build target 
            BuildBundlesForTarget(buildTarget, forceBundleRebuild: true, isDedicatedServer: isServer);
        }

        public static string GetRuntimePlatformNameFromBuildTarget(BuildTarget buildTarget, bool isDedicatedServer)
        {
            var runtime = ConvertBuildTargetToRuntime(buildTarget);
            var runtimeName = AssetBundleService.GetRuntimePlatformName(runtime, isDedicatedServer);
            return runtimeName; 
        }

        public static void BuildBundlesForTarget(BuildTarget platform, bool forceBundleRebuild = false, bool isDedicatedServer = false, 
            AssetBundleData forceSingleBundleBuildDebug = null, AssetBundleData[] onlyRebuildSpecificBundles = null, string[] onlyRebuildSpecificCategories = null)
        {
            Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: building for {0}...", platform);

            if (Application.isPlaying != false)
            {
                Debug.LogErrorFormat("AssetBundleExporter.BuildBundlesForTarget: > cannot build in play mode");
                return;
            }

            Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: Removing any duplicate bundle references...");
            FixDuplicatesAutomatically();

            Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: refreshing asset references...");
            AssetBundleService.EditorRefreshAssetBundleListOnPrefab();
            AssetBundleService.EditorUpdateBundleReferencesForBuilds();

            // prepare assets for export 
            Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: tagging assets for bundle inclusion...");

            var bundleOptions = BuildAssetBundleOptions.None;

            switch(platform)
            {
                case BuildTarget.PS4:
                case BuildTarget.PS5:
                    bundleOptions |= BuildAssetBundleOptions.UncompressedAssetBundle;       //PS5: bundles should be uncompressed and then compressed via the PS5 build process with kraken compression instead (decreases load times)
                    break;
                case BuildTarget.Switch:
                default:
                    bundleOptions |= BuildAssetBundleOptions.ChunkBasedCompression;         // important, ensures only assets we care about are pulled into memory + for full loads: disabling this explodes loading time (ex from 4s to 20s)
                    break; 
            }

            if (forceBundleRebuild)
            {
                bundleOptions |= BuildAssetBundleOptions.ForceRebuildAssetBundle;
            }

            bundleOptions |= BuildAssetBundleOptions.DisableLoadAssetByFileName;                // saves lookup time 
            bundleOptions |= BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;   // saves lookup time 
            // bundleOptions |= BuildAssetBundleOptions.AssetBundleStripUnityVersion;           // causes issues

#if UNITY_2022_3_OR_NEWER
            bundleOptions |= BuildAssetBundleOptions.UseContentHash;
#endif

            // bundleOptions |= BuildAssetBundleOptions.AssetBundleStripUnityVersion;
            // bundleOptions |= BuildAssetBundleOptions.DisableWriteTypeTree;
            // bundleOptions |= BuildAssetBundleOptions.UncompressedAssetBundle;    // explodes filesize and does not really help by by much 

            var runtime = ConvertBuildTargetToRuntime(platform);
            var runtimeName = AssetBundleService.GetRuntimePlatformName(runtime, isDedicatedServer);

            var assetBundleService = AssetDatabaseE.FindSingletonAsset<AssetBundleService>("PfbAssetBundleService");
            var assetBundleDatas = assetBundleService.AssetBundleDatas;

            var subTarget = (int) StandaloneBuildSubtarget.Player;

            if(isDedicatedServer)
            {
                subTarget = (int) StandaloneBuildSubtarget.Server;
            }

            var buildRoot = GetBundlesBuildFolder() + $"/{runtimeName}";
            if (Directory.Exists(buildRoot) == false)
            {
                Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: creating build folder...");
                Directory.CreateDirectory(buildRoot);
            }

            // safety checks 
            if(RUN_SAFETY_SCANS_ON_BUILD)
            {
                foreach (var assetBundleData in assetBundleDatas)
                {
                    assetBundleData.EditorRemoveDuplicateReferences();

                    if(!assetBundleData.NoDependencies)
                    {
                        assetBundleData.EditorRemoveOurAssetsFromOtherBundles(); 
                    }
                }
            }

            // build 
            // var any_no_dependency_bundles_built = false;


            // scan and build "NoDependency" bundles one at a time (clears tags on all bundles between each step)
            foreach (var assetBundleData in assetBundleDatas)
            {
                if(!assetBundleData.EnabledInBuild)
                {
                    continue;
                }

                if (!assetBundleData.NoDependencies)
                {
                    continue;
                }

                if(assetBundleData.DoNotBuildForDedicatedServer && isDedicatedServer)
                {
                    continue;
                }

                if(forceSingleBundleBuildDebug != null && forceSingleBundleBuildDebug != assetBundleData)
                {
                    continue;
                }

                // new way (explicit list and build from list) 
                var noDependencyBundleDefinitions = new AssetBundleBuildDefinitions();
                BuildBundleDefinitionsList(platform, assetBundleData, isDedicatedServer, noDependencyBundleDefinitions);
                InternalBuildBundlesExplicit(buildRoot, bundleOptions, platform, subTarget, noDependencyBundleDefinitions);

                // old way (tag and build) 
                // TagAssetsForAssetBundles(platform, assetBundleData, isDedicatedServer);
                // InternalBuildTaggedBundles(buildRoot, bundleOptions, platform);

                // prepare assets for export 
                // Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: removing asset tags...");
                // RemoveAssetBundleTagsFromAssets();

                // any_no_dependency_bundles_built = true; 
            }


            var bundleDefinitionData = new AssetBundleBuildDefinitions();

            // scan and tag all other bundles, then build once at the end 
            var foundBundlesWithDependencies = false;
            foreach (var assetBundleData in assetBundleDatas)
            {
                if (!assetBundleData.EnabledInBuild)
                {
                    continue;
                }

                if (assetBundleData.DoNotBuildForDedicatedServer && isDedicatedServer)
                {
                    continue;
                }

                if (assetBundleData.NoDependencies)
                {
                    continue;
                }

                if (forceSingleBundleBuildDebug != null && forceSingleBundleBuildDebug != assetBundleData)
                {
                    continue;
                }

                // skip tagging bundles we dont care about 
                // note: this will cause issues if using NoDependency on any bundles.. 
                if(onlyRebuildSpecificBundles != null)
                {
                    var isValid = false;

                    foreach(var validBundle in onlyRebuildSpecificBundles)
                    {
                        if(assetBundleData == validBundle)
                        {
                            isValid = true;
                            break; 
                        }
                    }

                    if(!isValid)
                    {
                        continue;
                    }
                }

                // new way, handle list manually 
                BuildBundleDefinitionsList(platform, assetBundleData, isDedicatedServer, bundleDefinitionData);

                // old way, tag 
                // TagAssetsForAssetBundles(platform, assetBundleData, isDedicatedServer);

                foundBundlesWithDependencies = true;
            }

            if (foundBundlesWithDependencies)
            {
                if (onlyRebuildSpecificBundles != null)
                {
                    InternalBuildSpecificBundles(buildRoot, bundleOptions, platform, subTarget, onlyRebuildSpecificBundles, onlyRebuildSpecificCategories, isDedicatedServer);
                }
                else
                {
                    // new way, build from explicit list 
                    InternalBuildBundlesExplicit(buildRoot, bundleOptions, platform, subTarget, bundleDefinitionData);

                    // old way, build tagged bundles 
                    // InternalBuildTaggedBundles(buildRoot, bundleOptions, platform);
                }
            }

            // old way, needed to remove tags 
            // var remove_tags = false;
            // 
            // if(any_no_dependency_bundles_built || !IF_NO_ZERO_DEPENDENCY_BUNDLES_DONT_REMOVE_BUNDLE_TAGS)
            // {
            //     remove_tags = true; 
            // }

            // if(remove_tags)
            // {
            //     RemoveAssetBundleTagsFromAssets();
            // }
        }



        private static void InternalBuildBundlesExplicit(string buildRoot, BuildAssetBundleOptions bundleOptions, BuildTarget platform, int subtarget, AssetBundleBuildDefinitions bundleDefinitions)
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();

#if UNITY_2022_3_OR_NEWER
            var manifest = BuildPipeline.BuildAssetBundles(new BuildAssetBundlesParameters()
            {
                options = bundleOptions,
                outputPath = buildRoot,
                targetPlatform = platform,
                bundleDefinitions = bundleDefinitions.GenerateBundleBuildArray(),
                subtarget = subtarget,
            });
#else
            var manifest = BuildPipeline.BuildAssetBundles(buildRoot, bundleDefinitions.GenerateBundleBuildArray(), bundleOptions, platform); 
#endif

            if (manifest != null)
            {
                foreach (var name in manifest.GetAllAssetBundles())
                {
                    Debug.LogFormat("AssetBundleExporter.ExportBundle: bundle hash: {0} = {1}", name, manifest.GetAssetBundleHash(name));
                }
            }
            else
            {
                Debug.LogFormat("AssetBundleExporter.ExportBundle: manifest was null");
            }

            stopwatch.Stop();
            Debug.LogFormat("AssetBundleExporter.ExportBundle: completed in {0} seconds", stopwatch.Elapsed.Seconds);
        }

        private static void InternalBuildTaggedBundles(string buildRoot, BuildAssetBundleOptions bundleOptions, BuildTarget platform, bool dryRun = false)
        {
            var stopwatch = new System.Diagnostics.Stopwatch();

            stopwatch.Start();

#if ASSET_BUNDLE_SCRIPTABLE_BUILD_PIPELINE
        var group = ConvertBuildTargetToBuildTargetGroup(platform);

        if (group == BuildTargetGroup.Unknown)
        {
            Debug.LogErrorFormat("AssetBundleExporter: unsupported build platform {0}, aborting", platform);
            return;
        }

        var parameters = new UnityEditor.Build.Pipeline.BundleBuildParameters(platform, group, buildRoot);
        var bundleBuilds = UnityEditor.Build.Content.ContentBuildInterface.GenerateAssetBundleBuilds();
        var content = new UnityEditor.Build.Pipeline.BundleBuildContent(bundleBuilds);

        parameters.CacheServerPort = 0;
        parameters.CacheServerHost = string.Empty;
        parameters.UseCache = false;

        parameters.ContiguousBundles = true;
        parameters.ContentBuildFlags = UnityEditor.Build.Content.ContentBuildFlags.StripUnityVersion;
        parameters.BundleCompression = BuildCompression.LZ4;

        // TODO: what does this actually need to be?
        parameters.NonRecursiveDependencies = false;

        Debug.LogFormat("AssetBundleExporter.ExportBundle: found {0} bundle builds", bundleBuilds.Length);
        foreach (var b in bundleBuilds)
            Debug.LogFormat("AssetBundleExporter.ExportBundle: > {0} ({1} assets)", b.assetBundleName, b.assetNames.Length);

        if (dryRun == false)
        {
            UnityEditor.Build.Pipeline.ContentPipeline.BuildAssetBundles(parameters, content, out var results);

            foreach (var r in results.BundleInfos)
            {
                Debug.LogFormat("AssetBundleExporter.ExportBundle: built {0} = {1} ({2})", r.Key, r.Value.FileName, r.Value.Hash);
                foreach (var d in r.Value.Dependencies)
                    Debug.LogFormat("AssetBundleExporter.ExportBundle: > dependency: {0}", d);
            }
        }
#else
            // todo: we should use this method, so we can specify load order (which implicitly specifies dependency order) 
            // var parameters = new BuildAssetBundlesParameters()
            // {
            //     outputPath = buildRoot,
            //     targetPlatform = platform,
            //     options = bundleOptions,
            // 
            //      bundleDefinitions = new AssetBundleBuild[]
            //      {
            //           new AssetBundleBuild()
            //           {
            //                
            //           }
            //      },
            //      
            // };
            // 
            // BuildPipeline.BuildAssetBundles()

            var manifest = BuildPipeline.BuildAssetBundles(buildRoot, bundleOptions, platform);

            if (manifest != null)
            {
                foreach (var name in manifest.GetAllAssetBundles())
                    Debug.LogFormat("AssetBundleExporter.ExportBundle: bundle hash: {0} = {1}", name, manifest.GetAssetBundleHash(name));
            }
            else
            {
                Debug.LogFormat("AssetBundleExporter.ExportBundle: manifest was null");
            }
#endif
            stopwatch.Stop();

            Debug.LogFormat("AssetBundleExporter.ExportBundle: completed in {0} seconds", stopwatch.Elapsed.Seconds);
        }

        private static void InternalBuildSpecificBundles(string buildRoot, BuildAssetBundleOptions bundleOptions, BuildTarget platform, int subTarget, AssetBundleData[] bundleDatas, string[] validPackCategories, bool isDedicatedServer)
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();

            var platformName = AssetBundleService.GetRuntimePlatformName(Application.platform, isDedicatedServer);
            var assetBundleRoot = GetBundlesDeployFolder();

            // construct bundle build data 
            var bundleBuilds = new List<AssetBundleBuild>(bundleDatas.Length);
            for (var bi = 0; bi < bundleDatas.Length; ++bi)
            {
                var bundleData = bundleDatas[bi];

                var bundleName = $"{bundleData.name}.bundle";

                var assetBundleDataName = AssetBundleService.GetBundleFilenameFromBundleName(Application.platform, isDedicatedServer, bundleData.name);
                var assetBundleRef = $"{assetBundleRoot}/{assetBundleDataName}";

                if(bundleData.PackSeparately && bundleData.PackMode == AssetBundleData.PackSeparatelyMode.EachFile)
                {
                    for (var ai = 0; ai < bundleData.Assets.Count; ++ai)
                    {
                        var assetEntry = bundleData.Assets[ai];
                        var assetPath = AssetDatabase.GUIDToAssetPath(assetEntry.GUID);

                        var assetBundleSetName = $"{assetEntry.GUID}.bundle";
                        var assetName = bundleData.GetPackedBundleDataName(assetEntry, platformName, assetBundleRoot, assetBundleRef);

                        var bundleBuild = new AssetBundleBuild();
                            bundleBuild.assetBundleName = assetBundleSetName;
                            bundleBuild.assetNames = new string[] { assetName };

                        bundleBuilds.Add(bundleBuild);
                    }
                }

                else if(bundleData.PackSeparately && bundleData.PackMode == AssetBundleData.PackSeparatelyMode.ByCategory)
                {
                    // gather categories 
                    var categoryBuilds = new Dictionary<string, List<string>>();
                    for (var ai = 0; ai < bundleData.Assets.Count; ++ai)
                    {
                        var assetEntry = bundleData.Assets[ai];
                        var assetPath = AssetDatabase.GUIDToAssetPath(assetEntry.GUID);
                        var assetName = bundleData.GetPackedBundleDataName(assetEntry, platformName, assetBundleRoot, assetBundleRef);

                        var packCategory = assetEntry.PackCategory;
                        if (string.IsNullOrEmpty(packCategory)) packCategory = "default";
                        var assetBundleSetName = $"{bundleData.name}_{packCategory}.bundle";

                        if(categoryBuilds.TryGetValue(assetBundleSetName, out var existingList))
                        {
                            existingList.Add(assetName); 
                        }
                        else
                        {
                            categoryBuilds.Add(assetBundleSetName, new List<string>() { assetName }); 
                        }
                    }

                    // configure AssetBundleBuild for each category 
                    foreach(var entry in categoryBuilds)
                    {
                        // if pack categories are defined, skip any that are not in the list 
                        if (validPackCategories != null && !validPackCategories.Contains(entry.Key))
                        {
                            continue;
                        }

                        var bundleBuild = new AssetBundleBuild();
                            bundleBuild.assetBundleName = entry.Key;
                            bundleBuild.assetNames = entry.Value.ToArray();

                        bundleBuilds.Add(bundleBuild);
                    }
                }
                else if(bundleData.PackSeparately)
                {
                    Debug.LogError($"Unknown bundleData.PackMode? Skipped bundle build. {bundleData.PackMode}");
                }
                else
                {
                    var bundleBuild = new AssetBundleBuild();
                        bundleBuild.assetBundleName = bundleData.name;
                        bundleBuild.assetBundleVariant = string.Empty;

                    bundleBuild.assetNames = new string[bundleData.Assets.Count];
                    for(var ai = 0; ai < bundleData.Assets.Count; ++ai)
                    {
                        var assetEntry = bundleData.Assets[ai];
                        var assetPath = AssetDatabase.GUIDToAssetPath(assetEntry.GUID);

                        bundleBuild.assetNames[ai] = assetPath;
                    }

                    bundleBuilds.Add(bundleBuild);
                }
            }

            // build 
#if UNITY_2022_3_OR_NEWER
            var manifest = BuildPipeline.BuildAssetBundles(new BuildAssetBundlesParameters()
            {
                outputPath = buildRoot,
                options = bundleOptions,
                targetPlatform = platform,
                bundleDefinitions = bundleBuilds.ToArray(),
                subtarget = subTarget,
            });
#else
            var manifest = BuildPipeline.BuildAssetBundles(buildRoot, bundleBuilds.ToArray(), bundleOptions, platform);
#endif

            // parse 
            if (manifest != null)
            {
                foreach (var name in manifest.GetAllAssetBundles())
                {
                    Debug.LogFormat("AssetBundleExporter.ExportBundle: bundle hash: {0} = {1}", name, manifest.GetAssetBundleHash(name));
                }
            }
            else
            {
                Debug.LogFormat("AssetBundleExporter.ExportBundle: manifest was null");
            }

            // report 
            stopwatch.Stop();
            Debug.LogFormat("AssetBundleExporter.ExportBundle: completed in {0} seconds", stopwatch.Elapsed.Seconds);
        }

        [MenuItem("Build System/Asset Bundles/Deploy")] 
        public static void DeployBundles() 
        {
            var isServer = GetIsPlayerServer();
            DeployBundlesForTarget(EditorUserBuildSettings.activeBuildTarget, isServer); 
        }

        public static void UndeployBundles()
        {
            var deployPath = GetBundlesDeployFolder();

            if (Directory.Exists(deployPath))
            {
                Debug.LogFormat("AssetBundleExporter.UndeployBundles: clearing existing deployment...");
                Directory.Delete(deployPath, true);
            }

            // no real way to know if the delete is still pending
            for (int i = 0; i < 3; ++i)
            {
                if (Directory.Exists(deployPath))
                    System.Threading.Thread.Sleep(100);
            }
        }

        public static void DeployBundlesForTarget(BuildTarget platform, bool isDedicatedServer)
        {
            var runtime = ConvertBuildTargetToRuntime(platform);
            var runtimeName = AssetBundleService.GetRuntimePlatformName(runtime, isDedicatedServer);

            Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: deploying for platform {0} (runtime {1})...", platform, runtimeName);

            if (Application.isPlaying != false)
            {
                // Debug.LogErrorFormat("AssetBundleExporter.DeployBundlesForTarget: > cannot deploy in play mode");
                // return;

                AssetDatabase.SaveAssets();
            }


            var buildPath = GetBundlesBuildFolder();
            var deployPath = GetBundlesDeployFolder();

            Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: cleaning existing bundles...");

            UndeployBundles(); 

            if (Directory.Exists(deployPath))
            {
                Debug.LogErrorFormat("AssetBundleExporter.DeployBundlesForTarget: unable to delete existing deploy folder, aborting...");
                return;
            }

            Directory.CreateDirectory(deployPath);

            if (Directory.Exists(deployPath) == false)
            {
                Debug.LogErrorFormat("AssetBundleExporter.DeployBundlesForTarget: unable to create deploy folder, aborting...");
                return;
            }

            // thanks C#

            var copySrc = Path.Combine(buildPath, runtimeName.ToString().ToLowerInvariant());
            var copyDst = Path.Combine(deployPath, runtimeName.ToString().ToLowerInvariant());

            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: copying {0} to {1}...", copySrc.Replace(root, ""), copyDst.Replace(root, ""));

            // ensure exists 
            if (!Directory.Exists(copySrc))
            {
                Directory.CreateDirectory(copySrc);
            }

            RecursiveFolderCopy(copySrc, copyDst);
        }

        public static RuntimePlatform ConvertBuildTargetToRuntime(BuildTarget platform)
        {
            switch (platform)
            {
                case BuildTarget.Android:
                    return RuntimePlatform.Android;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return RuntimePlatform.WindowsPlayer;
                case BuildTarget.StandaloneLinux64:
                    return RuntimePlatform.LinuxPlayer;
                case BuildTarget.PS4:
                    return RuntimePlatform.PS4;
                case BuildTarget.iOS:
                    return RuntimePlatform.IPhonePlayer;
                case BuildTarget.StandaloneOSX:
                    return RuntimePlatform.OSXPlayer;
                case BuildTarget.Switch:
                    return RuntimePlatform.Switch;
                case BuildTarget.XboxOne:
                    return RuntimePlatform.XboxOne;
                default:
                    Debug.LogError($"platform not configured");
                    return Application.platform;
            }
        }

        public static BuildTargetGroup ConvertBuildTargetToBuildTargetGroup(BuildTarget platform)
        {
            switch (platform)
            {
                case BuildTarget.Android:
                    return BuildTargetGroup.Android;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.StandaloneOSX:
                    return BuildTargetGroup.Standalone;
                case BuildTarget.PS4:
                    return BuildTargetGroup.PS4;
                case BuildTarget.PS5:
                    return BuildTargetGroup.PS5;
                case BuildTarget.iOS:
                    return BuildTargetGroup.iOS;
                case BuildTarget.Switch:
                    return BuildTargetGroup.Switch;
                case BuildTarget.XboxOne:
                    return BuildTargetGroup.XboxOne;
                default:
                    return BuildTargetGroup.Unknown;
            }
        }

        [MenuItem("Build System/Asset Bundles/Helper/Remove All Tags")]
        public static void RemoveAssetBundleTagsFromAssets()
        {
            AssetDatabase.StartAssetEditing();

            try
            {
                var assetBundleDatas = new List<AssetBundleData>();
                AssetDatabaseE.LoadAssetsOfType<AssetBundleData>(assetBundleDatas);

                foreach (var assetBundleData in assetBundleDatas)
                {
                    foreach (var assetData in assetBundleData.Assets)
                    {
                        var assetPath = AssetDatabase.GUIDToAssetPath(assetData.GUID);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            var assetImporter = AssetImporter.GetAtPath(assetPath);
                            if(assetImporter == null)
                            {
                                continue;
                            }

                            assetImporter.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();

            Debug.Log("AssetBundleExporter.RemoveAssetBundleTagsFromAssets: Removed all asset bundle tags from all assets.");
        }

        [MenuItem("Build System/Asset Bundles/Helper/Check If Okay for Build")]
        public static bool CheckIfBundlesOkayForBuild()
        {
            var results = new List<AssetBundleData>();
            AssetDatabaseE.LoadAssetsOfType(results);

            var any_match = false;

            for (var i = 0; i < results.Count; ++i)
            {
                var bundle_a = results[i];

                // when a bundle is marked with 'no dependencies', do not include it in this query 
                if (bundle_a.NoDependencies)
                {
                    continue;
                }

                for (var j = 0; j < results.Count; ++j)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var bundle_b = results[j];

                    if (bundle_b.NoDependencies)
                    {
                        continue;
                    }

                    for (var ia = 0; ia < bundle_a.Assets.Count; ++ia)
                    {
                        var asset_a = bundle_a.Assets[ia];

                        for (var ib = 0; ib < bundle_b.Assets.Count; ++ib)
                        {
                            var asset_b = bundle_b.Assets[ib];

                            if (asset_a.GUID == asset_b.GUID)
                            {
                                any_match = true;

                                var reference = new AssetReference<Object>();
                                reference.Guid = asset_a.GUID;

                                var asset = reference.EditorLoadAsset(null);
                                if (asset != null)
                                {
                                    Debug.LogError($"{bundle_a.name}'s {asset.name} is also present in {bundle_b.name}.", asset);
                                }
                                else
                                {
                                    Debug.LogError($"{bundle_a.name}'s {asset_a.GUID} (missing) is also present in {bundle_b.name}.");
                                }
                            }
                        }
                    }
                }
            }

            var resultCode = any_match ? "✖ ERROR " : "✔ ALL GOOD";
            Debug.Log($"[CheckIfBundlesOkayForBuild]: any duplicates? {any_match} | {resultCode}");

            return !any_match;
        }

        [MenuItem("Build System/Asset Bundles/Helper/Fix Duplicates")]
        public static void FixDuplicatesAutomatically()
        {
            var results = new List<AssetBundleData>();
            AssetDatabaseE.LoadAssetsOfType(results);

            for (var i = 0; i < results.Count; ++i)
            {
                var bundle = results[i];

                if (bundle.NoDependencies)
                {
                    continue;
                }

                if (bundle.name != "default_bundle")
                {
                    bundle.EditorRemoveOurAssetsFromOtherBundles();
                }
            }

            for (var i = 0; i < results.Count; ++i)
            {
                var bundle = results[i];

                if (bundle.NoDependencies)
                {
                    continue;
                }

                if (bundle.name == "default_bundle")
                {
                    bundle.EditorRemoveOurAssetsFromOtherBundles();
                }
            }
        }

        public static void MarkAllAssetsExternallyLoaded(bool externallyLoaded)
        {
            var results = new List<AssetBundleData>();
            AssetDatabaseE.LoadAssetsOfType(results);

            Undo.RecordObjects(results.ToArray(), "MarkExternal");
            for (var i = 0; i < results.Count; ++i)
            {
                var bundle = results[i];
                    bundle.IsDownloadedExternally = externallyLoaded;

                EditorUtility.SetDirty(bundle); 
            }
        }

        [System.Serializable]
        private class AssetBundleBuildDefinitions
        {
            public Dictionary<string, AssetBundleBuildData> bundleDefinitions = new Dictionary<string, AssetBundleBuildData>();

            [System.Serializable]
            public class AssetBundleBuildData
            {
                public string assetBundleName;
                public string assetBundleVariant;
                public List<string> assetNames = new List<string>();
                public List<string> addressableNames = new List<string>();
                public int buildOrder;
            }

            public void AddAsset(string bundleName, string assetPath, int buildOrder)
            {
                if (bundleDefinitions.TryGetValue(bundleName, out var bundleData))
                {
                    bundleData.assetNames.Add(assetPath);
                    bundleData.addressableNames.Add(assetPath);
                }
                else
                {
                    bundleDefinitions.Add(bundleName, new AssetBundleBuildData()
                    {
                        assetBundleName = bundleName,
                        assetBundleVariant = string.Empty,
                        assetNames = new List<string>()
                        {
                            assetPath,
                        },
                        addressableNames = new List<string>()
                        {
                            assetPath,
                        },
                        buildOrder = buildOrder,
                    });
                }
            }

            public AssetBundleBuild[] GenerateBundleBuildArray()
            {
                var buildDataList = new List<AssetBundleBuildData>();
                
                foreach (var entry in bundleDefinitions)
                {
                    var data = entry.Value;
                    buildDataList.Add(data);
                }

                var orderedList = buildDataList.OrderBy(buildData => buildData.buildOrder).ThenBy(data => data.assetBundleName);
                var results = new List<AssetBundleBuild>(buildDataList.Count);

                foreach (var entry in orderedList)
                {
                    results.Add(new AssetBundleBuild()
                    {
                        assetBundleName = entry.assetBundleName,
                        assetBundleVariant = entry.assetBundleVariant,
                        assetNames = entry.assetNames.ToArray(),
                        addressableNames = entry.addressableNames.ToArray(),
                    });
                }

                return results.ToArray(); 
            }
        }

        private static void BuildBundleDefinitionsList(BuildTarget platform, AssetBundleData assetBundleData, bool isDedicatedServer, AssetBundleBuildDefinitions definitionData)
        {
            try
            {
                var bundleName = $"{assetBundleData.name}.bundle";

                // find all the paths 
                var count = 0;
                foreach (var assetData in assetBundleData.Assets)
                {
                    var assetBundleSetName = bundleName;

                    if (assetBundleData.PackSeparately)
                    {
                        assetBundleSetName = $"{assetData.GUID}.bundle";

                        switch (assetBundleData.PackMode)
                        {
                            case AssetBundleData.PackSeparatelyMode.EachFile:
                                assetBundleSetName = $"{assetData.GUID}.bundle";
                                break;
                            case AssetBundleData.PackSeparatelyMode.ByCategory:
                                var packCategory = assetData.PackCategory;
                                if (string.IsNullOrEmpty(packCategory)) packCategory = "default";
                                assetBundleSetName = $"{assetBundleData.name}_{packCategory}.bundle";
                                break;
                        }
                    }

                    var assetPath = AssetDatabase.GUIDToAssetPath(assetData.GUID);
                    definitionData.AddAsset(assetBundleSetName, assetPath, assetBundleData.buildOrder);
                }

                Debug.Log($"AssetBundleExporter.ExportBundle: found {count} assets...");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void TagAssetsForAssetBundles(BuildTarget platform, AssetBundleData assetBundleData, bool isDedicatedServer)
        {
            AssetDatabase.StartAssetEditing();

            try
            {
                var bundleName = $"{assetBundleData.name}.bundle";

                // find all the paths 
                var count = 0;
                foreach (var assetData in assetBundleData.Assets)
                {
                    var assetBundleSetName = bundleName;

                    if (assetBundleData.PackSeparately)
                    {
                        assetBundleSetName = $"{assetData.GUID}.bundle";
                        
                        switch (assetBundleData.PackMode)
                        {
                            case AssetBundleData.PackSeparatelyMode.EachFile:
                                assetBundleSetName = $"{assetData.GUID}.bundle";
                                break; 
                            case AssetBundleData.PackSeparatelyMode.ByCategory:
                                var packCategory = assetData.PackCategory;
                                if (string.IsNullOrEmpty(packCategory)) packCategory = "default";
                                assetBundleSetName = $"{assetBundleData.name}_{packCategory}.bundle";
                                break; 
                        }
                    }

                    var assetPath = AssetDatabase.GUIDToAssetPath(assetData.GUID);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        var assetImporter = AssetImporter.GetAtPath(assetPath);
                        if(assetImporter == null)
                        {
                            continue;
                        }

                        // asset stripping from dedicated server 
                        if(isDedicatedServer)
                        {
                            if(assetImporter is ModelImporter modelImporter)
                            {
                                if(!modelImporter.isReadable)
                                {
                                    continue;
                                }
                            }

                            if(assetImporter is TextureImporter textureImporter)
                            {
                                if(!textureImporter.isReadable)
                                {
                                    continue;
                                }
                            }

                            // use shader stripping for this 
                            // if(assetImporter is ShaderImporter shaderImporter)
                            // {
                            //     continue;
                            // }

                            // may cause problems when spawning objects
                            // if(assetImporter is AudioImporter audioImporter)
                            // {
                            //     continue;
                            // }
                        }

                        assetImporter.SetAssetBundleNameAndVariant(assetBundleSetName, string.Empty);
                        count++;
                    }
                }

                Debug.Log($"AssetBundleExporter.ExportBundle: found {count} assets...");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        public static void RecursiveFolderCopy(string srcFolder, string dstFolder)
        {
            Debug.LogFormat("AssetBundleExporter: copy folder {0} -> {1}", srcFolder, dstFolder);

            if (Directory.Exists(dstFolder) == false)
            {
                Directory.CreateDirectory(dstFolder);
            }

            if(!Directory.Exists(srcFolder))
            {
                return; 
            }

            var files = Directory.GetFiles(srcFolder);

            foreach (var file in files)
            {
                var srcFile = Path.Combine(srcFolder, Path.GetFileName(file));
                var dstFile = Path.Combine(dstFolder, Path.GetFileName(file));

                Debug.LogFormat("AssetBundleExporter: copy file {0} -> {1}", srcFile, dstFile);

                File.Copy(srcFile, dstFile);
            }

            var folders = Directory.GetDirectories(srcFolder);

            foreach (var folder in folders)
            {
                var childSrcFolder = Path.GetFileName(folder);
                var childDstFolder = Path.Combine(dstFolder, childSrcFolder);

                RecursiveFolderCopy(childSrcFolder, childDstFolder);
            }
        }
#endif
        }
}