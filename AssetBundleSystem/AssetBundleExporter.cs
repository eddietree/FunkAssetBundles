﻿#pragma warning disable CS0162 // Unreachable code detected

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
#endif

    // asset bundles are built locally to library cache folder
    // final builds will 'deploy' them to streaming assets before building

    public class AssetBundleExporter
        : MonoBehaviour
    {
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

        public static void BuildBundlesForTarget(BuildTarget platform, bool forceBundleRebuild = false, bool isDedicatedServer = false, AssetBundleData forceSingleBundleBuildDebug = null)
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
#if UNITY_PS5
        bundleOptions |= BuildAssetBundleOptions.UncompressedAssetBundle;   //PS5: bundles should be uncompressed and then compressed via the PS5 build process with kraken compression instead (decreases load times)
#else
            bundleOptions |= BuildAssetBundleOptions.ChunkBasedCompression;         // important, ensures only assets we care about are pulled into memory + for full loads: disabling this explodes loading time (ex from 4s to 20s)
#endif

            if (forceBundleRebuild)
            {
                bundleOptions |= BuildAssetBundleOptions.ForceRebuildAssetBundle;
            }

            bundleOptions |= BuildAssetBundleOptions.DisableLoadAssetByFileName;                // saves lookup time 
            bundleOptions |= BuildAssetBundleOptions.DisableLoadAssetByFileNameWithExtension;   // saves lookup time 
            bundleOptions |= BuildAssetBundleOptions.AssetBundleStripUnityVersion;

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

            var buildRoot = GetBundlesBuildFolder() + $"/{runtimeName}";
            if (Directory.Exists(buildRoot) == false)
            {
                Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: creating build folder...");
                Directory.CreateDirectory(buildRoot);
            }

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

                TagAssetsForAssetBundles(platform, assetBundleData, isDedicatedServer);
                InternalBuildTaggedBundles(buildRoot, bundleOptions, platform);

                // prepare assets for export 
                Debug.LogFormat("AssetBundleExporter.BuildBundlesForTarget: removing asset tags...");
                RemoveAssetBundleTagsFromAssets();
            }

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

                TagAssetsForAssetBundles(platform, assetBundleData, isDedicatedServer);
                foundBundlesWithDependencies = true;
            }

            if (foundBundlesWithDependencies)
            {
                InternalBuildTaggedBundles(buildRoot, bundleOptions, platform);
            }

            RemoveAssetBundleTagsFromAssets();
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

        [MenuItem("Build System/Asset Bundles/Deploy")] 
        public static void DeployBundles() 
        {
            var isServer = GetIsPlayerServer();
            DeployBundlesForTarget(EditorUserBuildSettings.activeBuildTarget, isServer); 
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

            if (Directory.Exists(deployPath))
            {
                Debug.LogFormat("AssetBundleExporter.DeployBundlesForTarget: clearing existing deployment...");
                Directory.Delete(deployPath, true);
            }

            // no real way to know if the delete is still pending
            for (int i = 0; i < 3; ++i)
            {
                if (Directory.Exists(deployPath))
                    System.Threading.Thread.Sleep(100);
            }

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
                // case BuildTarget.StandaloneLinux: // deprecated 
                // case BuildTarget.StandaloneLinuxUniversal: // deprecated 
                case BuildTarget.StandaloneLinux64:
                    return RuntimePlatform.LinuxPlayer;
                // TODO: PSVR
                case BuildTarget.PS4:
                    return RuntimePlatform.PS4;
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
                // case BuildTarget.StandaloneLinux: // deprecated 
                // case BuildTarget.StandaloneLinuxUniversal: // deprecated 
                case BuildTarget.StandaloneLinux64:
                    return BuildTargetGroup.Standalone;
                case BuildTarget.PS4:
                    return BuildTargetGroup.PS4;
                case BuildTarget.PS5:
                    return BuildTargetGroup.PS5;
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
                Directory.CreateDirectory(dstFolder);

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