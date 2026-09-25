// 编辑期小工具 · 来源：ModdingDocs «Creating custom Scenes with Unity» → Editor
// 作用：把标了 AssetBundle 名的资源打进 Assets/AssetBundles/。
// 用法：选中场景 → Inspector 最底部 AssetBundle 填 hkcs_scenes → 菜单 Build AssetBundles。
// 注意：StandaloneWindows64 打出来的包只有 Windows 保证可用（用了自定义 shader 时尤其如此）。
using UnityEditor;
using System.IO;

public class CreateAssetBundles
{
  [MenuItem("Build AssetBundles/Build AssetBundles Compressed")]
  static void BuildAllAssetBundlesCompressed()
  {
    string assetBundleDirectory = "Assets/AssetBundles";
    if (!Directory.Exists(assetBundleDirectory))
    {
      Directory.CreateDirectory(assetBundleDirectory);
    }
    BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                    BuildAssetBundleOptions.None,
                                    BuildTarget.StandaloneWindows64);
  }

  [MenuItem("Build AssetBundles/Build AssetBundles Uncompressed")]
  static void BuildAllAssetBundlesUncompressed()
  {
    string assetBundleDirectory = "Assets/AssetBundles";
    if (!Directory.Exists(assetBundleDirectory))
    {
      Directory.CreateDirectory(assetBundleDirectory);
    }
    BuildPipeline.BuildAssetBundles(assetBundleDirectory,
                                    BuildAssetBundleOptions.UncompressedAssetBundle,
                                    BuildTarget.StandaloneWindows64);
  }
}
