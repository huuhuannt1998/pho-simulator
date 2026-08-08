using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-shot headless URP setup for Phase 0 bootstrap.
/// Run via: Unity -batchmode -nographics -projectPath . -executeMethod RenderPipelineSetup.Run -quit
///
/// URP asset/renderer creation has no stable public constructor API, so this
/// uses SerializedObject reflection to wire the renderer list -- the same
/// technique URP's own internal menu-item creator uses under the hood.
/// </summary>
public static class RenderPipelineSetup
{
    const string PipelineFolder = "Assets/Settings";
    const string RendererAssetPath = PipelineFolder + "/PhoUniversalRenderer.asset";
    const string PipelineAssetPath = PipelineFolder + "/PhoUniversalRenderPipeline.asset";

    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(PipelineFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        var urpAssetType = System.Type.GetType(
            "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
        var rendererDataType = System.Type.GetType(
            "UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");

        if (urpAssetType == null || rendererDataType == null)
        {
            Debug.LogError("[RenderPipelineSetup] FAILED: could not resolve URP runtime types via reflection.");
            return;
        }

        var rendererData = ScriptableObject.CreateInstance(rendererDataType);
        AssetDatabase.CreateAsset(rendererData, RendererAssetPath);

        var pipelineAsset = ScriptableObject.CreateInstance(urpAssetType);
        var so = new SerializedObject(pipelineAsset);
        var rendererListProp = so.FindProperty("m_RendererDataList");
        if (rendererListProp == null)
        {
            Debug.LogError("[RenderPipelineSetup] FAILED: m_RendererDataList not found on URP asset.");
            return;
        }

        rendererListProp.arraySize = 1;
        rendererListProp.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
        AssetDatabase.SaveAssets();

        var loaded = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelineAssetPath);
        if (loaded == null)
        {
            Debug.LogError("[RenderPipelineSetup] FAILED: could not reload created URP asset.");
            return;
        }

        GraphicsSettings.defaultRenderPipeline = loaded;
        foreach (var name in QualitySettings.names)
        {
            var idx = System.Array.IndexOf(QualitySettings.names, name);
            QualitySettings.SetQualityLevel(idx, false);
            QualitySettings.renderPipeline = loaded;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[RenderPipelineSetup] OK: URP assigned as default pipeline at {PipelineAssetPath}");
    }
}
