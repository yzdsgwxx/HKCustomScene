// 编辑期小工具 · 来源：ModdingDocs «Creating custom Scenes with Unity» → Editor
// 作用：把标了 AssetBundle 名的资源打进 Assets/AssetBundles/。
// 用法：选中场景 → Inspector 最底部 AssetBundle 填 hkcs_scenes → 菜单 HKModCustomSceneTool。
// 注意：StandaloneWindows64 打出来的包只有 Windows 保证可用（用了自定义 shader 时尤其如此）。
//       「打包测试」（整条自动化流程）也在同一个菜单下，见 HKCSMenu.cs。
using UnityEditor;
using System.IO;

public class CreateAssetBundles
{
  private const string MenuRoot = "HKModCustomSceneTool/";

  [MenuItem(MenuRoot + "Build AssetBundles Compressed", false, 20)]
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

  [MenuItem(MenuRoot + "Build AssetBundles Uncompressed", false, 21)]
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
