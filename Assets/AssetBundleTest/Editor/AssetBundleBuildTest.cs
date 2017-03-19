using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class AssetBundleCompressTest
{
    static string[] _assetNames = new string[3] {
        "Assets/AssetBundleTest/UnityChan/portrait_kohaku_01a.png",
        "Assets/AssetBundleTest/UnityChan/foo/portrait_kohaku_01a.png",
        "Assets/AssetBundleTest/UnityChan/portrait_kohaku_02a.png",
    };

    static AssetBundleBuild[] CreateBuildMap (string mode, BuildTarget target)
    {
        AssetBundleBuild[] buildMap = new AssetBundleBuild[1];
        buildMap [0].assetBundleName = string.Format ("Test_{0}_{1}.unity3d", mode, target);
        buildMap [0].assetNames = _assetNames;
        return buildMap;
    }

    [MenuItem ("AssetBundlesCompress/CreateSample")]
    static void CreateSample ()
    {
        var outputPath = "AssetBundles";

        System.IO.Directory.CreateDirectory (outputPath);

        //BuildTarget.Android,
        //BuildTarget.iOS,
        var target = EditorUserBuildSettings.activeBuildTarget;
        BuildPipeline.BuildAssetBundles (
            outputPath: outputPath,
            builds: CreateBuildMap ("RAW", target),
            assetBundleOptions: BuildAssetBundleOptions.UncompressedAssetBundle,
            targetPlatform: target);
        BuildPipeline.BuildAssetBundles (
            outputPath: outputPath,
            builds: CreateBuildMap ("LZ4", target),
            assetBundleOptions: BuildAssetBundleOptions.ChunkBasedCompression,
            targetPlatform: target);
        BuildPipeline.BuildAssetBundles (
            outputPath: outputPath,
            builds: CreateBuildMap ("LZMA", target),
            assetBundleOptions: BuildAssetBundleOptions.None,
            targetPlatform: target);
    }

    [MenuItem ("AssetBundlesCompress/CompressAssetBundle")]
    static void CompressAssetBundle ()
    {
        AssetBundleCompressor.Compress ("AssetBundles/test_raw_ios.unity3d", _assetNames, "AssetBundles/test_raw_ios.unity3d_c_nz", AssetBundleCompressor.CompressMode.Raw);
        AssetBundleCompressor.Compress ("AssetBundles/test_raw_ios.unity3d", _assetNames, "AssetBundles/test_raw_ios.unity3d_c_zi", AssetBundleCompressor.CompressMode.Deflate);
        AssetBundleCompressor.Compress ("AssetBundles/test_raw_ios.unity3d", _assetNames, "AssetBundles/test_raw_ios.unity3d_c_zl", AssetBundleCompressor.CompressMode.LZMA);
    }

    [MenuItem ("AssetBundlesCompress/DecompressAssetBundle")]
    static void DecomporessAssetBundle ()
    {
        AssetBundleCompressor.Decompress ("AssetBundles/test_raw_ios.unity3d_c_nz", "AssetBundles/test_raw_ios.unity3d_d_nz");
        AssetBundleCompressor.Decompress ("AssetBundles/test_raw_ios.unity3d_c_zi", "AssetBundles/test_raw_ios.unity3d_d_zi");
        AssetBundleCompressor.Decompress ("AssetBundles/test_raw_ios.unity3d_c_zl", "AssetBundles/test_raw_ios.unity3d_d_zl");
    }

}
