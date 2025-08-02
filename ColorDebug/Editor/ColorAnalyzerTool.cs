using UnityEngine;
using UnityEditor;

public class ColorAnalyzerTool : EditorWindow
{
    private const string FOLDER_PATH = "Assets/Script/UnityScreenDebuger/ColorDebug/Shader";

    private enum CaptureTarget { GameView, SceneView }

    private CaptureTarget captureTarget = CaptureTarget.GameView;
    private Texture2D capturedTexture;
    private bool autoUpdate = false;
    private float updateInterval = 0.1f; // 더 빠른 업데이트 (10fps)
    private double lastUpdateTime;

    // 히스토그램 표시 옵션 (UI에 표시하지 않음)
    private bool useLogScale = true;
    private float amplificationFactor = 3.0f;

    private ComputeShader histogramComputeShader;
    private ComputeBuffer histogramBuffer;
    private RenderTexture histogramTexture;
    private Material histogramMaterial;
    private bool resourcesLoaded = false;

    private const int HISTOGRAM_TEXTURE_HEIGHT = 200;

    [MenuItem("Window/Color Analysis Tool (sRGB Final)")]
    public static void ShowWindow()
    {
        var window = GetWindow<ColorAnalyzerTool>("Color Analysis (sRGB)");
        window.minSize = new Vector2(420, 500);
    }

    void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        LoadResources();
    }

    void LoadResources()
    {
        string computeShaderPath = $"{FOLDER_PATH}/Histogram.compute";
        histogramComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(computeShaderPath);

        string shaderPath = $"{FOLDER_PATH}/Histogram.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        if (histogramComputeShader != null && shader != null)
        {
            histogramBuffer = new ComputeBuffer(256, sizeof(uint));

            histogramTexture = new RenderTexture(256, HISTOGRAM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32);
            histogramTexture.enableRandomWrite = true;
            histogramTexture.Create();

            histogramTexture.hideFlags = HideFlags.HideAndDontSave;
            histogramTexture.filterMode = FilterMode.Point;

            histogramMaterial = new Material(shader);
            histogramMaterial.hideFlags = HideFlags.HideAndDontSave;

            resourcesLoaded = true;
        }
        else
        {
            if (histogramComputeShader == null)
                Debug.LogError($"[ColorAnalyzerTool] Failed to load Compute Shader. Check path: {computeShaderPath}");
            if (shader == null)
                Debug.LogError($"[ColorAnalyzerTool] Failed to load Shader. Check path: {shaderPath}");

            resourcesLoaded = false;
        }
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        if (capturedTexture != null) DestroyImmediate(capturedTexture);
        if (histogramBuffer != null) { histogramBuffer.Release(); histogramBuffer = null; }
        if (histogramTexture != null) { histogramTexture.Release(); DestroyImmediate(histogramTexture); }
        if (histogramMaterial != null) DestroyImmediate(histogramMaterial);
    }

    void OnEditorUpdate()
    {
        if (autoUpdate && EditorApplication.timeSinceStartup - lastUpdateTime > updateInterval)
        {
            lastUpdateTime = EditorApplication.timeSinceStartup; // 시간 업데이트 추가
            Capture();
            Repaint();
        }
    }

    void OnGUI()
    {
        // Header and Controls
        EditorGUILayout.LabelField("Color Analysis Tool (sRGB Only)", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        EditorGUILayout.BeginVertical("box");
        captureTarget = (CaptureTarget)EditorGUILayout.EnumPopup("Capture Target:", captureTarget);
        autoUpdate = EditorGUILayout.Toggle("Auto Update", autoUpdate);
        if (GUILayout.Button("Capture", GUILayout.Height(30))) Capture();
        EditorGUILayout.EndVertical();

        if (!resourcesLoaded)
        {
            EditorGUILayout.HelpBox("Failed to load resources! Check console for errors.", MessageType.Error);
            return;
        }

        if (capturedTexture == null)
        {
            EditorGUILayout.HelpBox("Press 'Capture' to begin.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();

        // 1. Draw the preview texture
        float aspectRatio = (float)capturedTexture.width / capturedTexture.height;
        Rect previewRect = GUILayoutUtility.GetAspectRect(aspectRatio, GUILayout.ExpandWidth(true));
        GUI.DrawTexture(previewRect, capturedTexture, ScaleMode.ScaleToFit);

        EditorGUILayout.Space(10);

        // 2. Draw histogram using GUILayout for proper sizing
        EditorGUILayout.LabelField("Histogram (Value Channel)", EditorStyles.boldLabel);

        // 캡처된 이미지 크기에 맞춰 히스토그램 높이 계산
        float windowWidth = EditorGUIUtility.currentViewWidth - 20; // Account for padding
        float capturedImageAspect = (float)capturedTexture.width / capturedTexture.height;
        float displayedImageWidth = windowWidth;
        float displayedImageHeight = displayedImageWidth / capturedImageAspect;

        // 히스토그램 높이를 표시된 이미지 높이의 30-50% 정도로 설정
        float histogramHeight = Mathf.Clamp(displayedImageHeight, 100f, 300f);

        Rect histogramLayoutRect = GUILayoutUtility.GetRect(windowWidth, histogramHeight, GUILayout.ExpandWidth(true));

        if (Event.current.type == EventType.Repaint && histogramTexture != null)
        {
            // Draw with proper texture coordinates (no flipping needed)
            GUI.DrawTexture(histogramLayoutRect, histogramTexture, ScaleMode.StretchToFill);
        }
    }

    void Capture()
    {
        if (!resourcesLoaded) return;
        RenderTexture srgbRT = CaptureFromCamera();
        if (srgbRT != null)
        {
            AnalyzeImageWithCompute(srgbRT);
            DrawHistogramWithShader();
            RenderTexture.ReleaseTemporary(srgbRT);
        }
        // lastUpdateTime 업데이트 제거 (OnEditorUpdate에서 처리)
    }

    RenderTexture CaptureFromCamera()
    {
        Camera camera = (captureTarget == CaptureTarget.GameView)
            ? (Camera.main ?? FindObjectOfType<Camera>())
            : (SceneView.lastActiveSceneView ?? GetWindow<SceneView>()).camera;

        if (camera == null) return null;

        int width = camera.pixelWidth;
        int height = camera.pixelHeight;

        var linearRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

        var srgbDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0);
        srgbDesc.sRGB = true;
        srgbDesc.enableRandomWrite = true;
        var srgbRT = RenderTexture.GetTemporary(srgbDesc);

        var prevTarget = camera.targetTexture;
        camera.targetTexture = linearRT;
        camera.Render();
        camera.targetTexture = prevTarget;

        Graphics.Blit(linearRT, srgbRT);

        if (capturedTexture != null) DestroyImmediate(capturedTexture);
        capturedTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        RenderTexture.active = srgbRT;
        capturedTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        capturedTexture.Apply();
        RenderTexture.active = null;

        RenderTexture.ReleaseTemporary(linearRT);
        return srgbRT;
    }

    void AnalyzeImageWithCompute(RenderTexture source)
    {
        int kernelClear = histogramComputeShader.FindKernel("KHistogramClear");
        int kernelGather = histogramComputeShader.FindKernel("KHistogramGather");

        histogramComputeShader.SetBuffer(kernelClear, "_HistogramBuffer", histogramBuffer);
        histogramComputeShader.Dispatch(kernelClear, 256 / 16, 1, 1);

        histogramComputeShader.SetTexture(kernelGather, "_Source", source);
        histogramComputeShader.SetBuffer(kernelGather, "_HistogramBuffer", histogramBuffer);
        histogramComputeShader.SetVector("_Params", new Vector4(source.width, source.height, 0, 0));
        histogramComputeShader.Dispatch(kernelGather, Mathf.CeilToInt(source.width / 16f), Mathf.CeilToInt(source.height / 16f), 1);
    }

    void DrawHistogramWithShader()
    {
        if (histogramBuffer == null || histogramTexture == null || histogramMaterial == null) return;

        // RenderTexture 클리어
        RenderTexture.active = histogramTexture;
        GL.Clear(true, true, Color.black);

        histogramMaterial.SetBuffer("_HistogramBuffer", histogramBuffer);
        histogramMaterial.SetVector("_Params", new Vector2(256, HISTOGRAM_TEXTURE_HEIGHT));
        histogramMaterial.SetFloat("_UseLogScale", useLogScale ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_AmplificationFactor", amplificationFactor);

        // 명시적으로 full-screen quad 그리기
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);

        histogramMaterial.SetPass(0);

        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);

        GL.End();
        GL.PopMatrix();

        RenderTexture.active = null;
    }
}