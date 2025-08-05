using UnityEngine;
using UnityEditor;

public class ColorAnalyzerTool : EditorWindow
{
    private const string FOLDER_PATH = "Assets/Script/UnityScreenDebuger/ColorDebug/Shader";

    private enum CaptureTarget { GameView, SceneView }
    private enum AnalysisMode { Histogram, Vectorscope, Waveform }

    private CaptureTarget captureTarget = CaptureTarget.GameView;
    private AnalysisMode analysisMode = AnalysisMode.Histogram;

    private Texture2D capturedTexture;
    private bool autoUpdate = false;
    private float updateInterval = 0.01f;
    private double lastUpdateTime;

    // ───────── Histogram options
    private bool useLogScale = true;
    private float amplificationFactor = 2.0f;

    private bool showRed = true;
    private bool showGreen = true;
    private bool showBlue = true;
    private bool showLuminance = true;

    // ───────── Compute & resources
    private ComputeShader unifiedComputeShader;

    // Histogram
    private ComputeBuffer histogramBuffer;
    private RenderTexture histogramTexture;
    private Material histogramMaterial;

    // Vectorscope
    private ComputeBuffer vectorscopeBuffer;
    private RenderTexture vectorscopeTexture;
    private Material vectorscopeMaterial;
    private int vectorscopeSize = 256;

    // Waveform
    private ComputeBuffer waveformBuffer;
    private RenderTexture waveformTexture;
    private Material waveformMaterial;
    private float waveformExposure = 0.02f;   // 기본 노출

    // misc
    private bool resourcesLoaded = false;

    private const int HISTOGRAM_TEXTURE_HEIGHT = 200;
    private const int VECTORSCOPE_TEXTURE_SIZE = 256;
    private const int WAVEFORM_TEXTURE_HEIGHT = 200;

    // ─────────────────────────────────────────────────────────────────────────────

    [MenuItem("Window/Color Analysis Tool")]
    public static void ShowWindow()
    {
        var window = GetWindow<ColorAnalyzerTool>("Color Analyzer");
        window.minSize = new Vector2(380, 600);
        window.maxSize = new Vector2(600, 1200);
    }

    void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        LoadResources();
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;

        if (capturedTexture != null) DestroyImmediate(capturedTexture);

        histogramBuffer?.Release();
        vectorscopeBuffer?.Release();
        waveformBuffer?.Release();

        if (histogramTexture != null) { histogramTexture.Release(); DestroyImmediate(histogramTexture); }
        if (vectorscopeTexture != null) { vectorscopeTexture.Release(); DestroyImmediate(vectorscopeTexture); }
        if (waveformTexture != null) { waveformTexture.Release(); DestroyImmediate(waveformTexture); }

        if (histogramMaterial != null) DestroyImmediate(histogramMaterial);
        if (vectorscopeMaterial != null) DestroyImmediate(vectorscopeMaterial);
        if (waveformMaterial != null) DestroyImmediate(waveformMaterial);
    }

    // ───────── Resource loading
    void LoadResources()
    {
        string computeShaderPath = $"{FOLDER_PATH}/UnifiedImageAnalysis.compute";
        unifiedComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(computeShaderPath);

        if (unifiedComputeShader == null)
        {
            Debug.LogError($"[ColorAnalyzerTool] Failed to load compute shader: {computeShaderPath}");
            resourcesLoaded = false;
            return;
        }

        LoadHistogramResources();
        LoadVectorscopeResources();
        LoadWaveformResources();

        resourcesLoaded = true;
    }

    void LoadHistogramResources()
    {
        string shaderPath = $"{FOLDER_PATH}/Histogram.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null) { Debug.LogError($"Histogram shader missing: {shaderPath}"); return; }

        histogramBuffer = new ComputeBuffer(256 * 4, sizeof(uint));
        histogramTexture = new RenderTexture(256, HISTOGRAM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32)
        { enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        histogramTexture.Create();

        histogramMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void LoadVectorscopeResources()
    {
        string shaderPath = $"{FOLDER_PATH}/Vectorscope.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null) { Debug.LogWarning($"Vectorscope shader missing: {shaderPath}"); return; }

        vectorscopeBuffer = new ComputeBuffer(vectorscopeSize * vectorscopeSize, sizeof(uint));
        vectorscopeTexture = new RenderTexture(VECTORSCOPE_TEXTURE_SIZE, VECTORSCOPE_TEXTURE_SIZE, 0,
                                               RenderTextureFormat.ARGB32)
        { enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        vectorscopeTexture.Create();

        vectorscopeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void LoadWaveformResources()
    {
        string shaderPath = $"{FOLDER_PATH}/Waveform.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null) { Debug.LogWarning($"Waveform shader missing: {shaderPath}"); return; }

        waveformTexture = new RenderTexture(512, WAVEFORM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32)
        { enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        waveformTexture.Create();

        waveformMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    // ───────── Update & UI
    void OnEditorUpdate()
    {
        if (autoUpdate && EditorApplication.timeSinceStartup - lastUpdateTime > updateInterval)
        {
            lastUpdateTime = EditorApplication.timeSinceStartup;
            Capture();
            Repaint();
        }
    }

    void OnGUI()
    {
        EditorGUILayout.Space(5);

        DrawCaptureSection();

        EditorGUILayout.Space(6);
        DrawAnalysisSettingsSection();

        if (!resourcesLoaded)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Failed to load resources.", MessageType.Error);
            if (GUILayout.Button("Retry Loading")) LoadResources();
            return;
        }

        if (capturedTexture == null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("No image captured. Press 'Capture'.", MessageType.Info);
            return;
        }

        DrawPreviewSection();
        DrawAnalysisSection();
    }

    // ----- Capture settings UI
    void DrawCaptureSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Capture Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Target:", GUILayout.Width(50));
        captureTarget = (CaptureTarget)EditorGUILayout.EnumPopup(captureTarget);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Analysis:", GUILayout.Width(60));
        analysisMode = (AnalysisMode)EditorGUILayout.EnumPopup(analysisMode);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        autoUpdate = EditorGUILayout.Toggle("Auto Update", autoUpdate);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Capture", GUILayout.Height(20))) Capture();

        EditorGUILayout.EndVertical();
    }

    // ----- Per-analysis options UI
    void DrawAnalysisSettingsSection()
    {
        EditorGUILayout.BeginVertical("box");

        switch (analysisMode)
        {
            case AnalysisMode.Histogram: DrawHistogramSettings(); break;
            case AnalysisMode.Vectorscope: DrawVectorscopeSettings(); break;
            case AnalysisMode.Waveform: DrawWaveformSettings(); break;
        }

        EditorGUILayout.EndVertical();
    }

    void DrawHistogramSettings()
    {
        EditorGUILayout.LabelField("Channel Visibility", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);

        DrawChannelToggle(ref showRed, "Red Channel", Color.red);
        DrawChannelToggle(ref showGreen, "Green Channel", Color.green);
        DrawChannelToggle(ref showBlue, "Blue Channel", Color.blue);
        DrawChannelToggle(ref showLuminance, "Luminance", Color.white);

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All")) { showRed = showGreen = showBlue = showLuminance = true; }
        if (GUILayout.Button("RGB")) { showRed = showGreen = showBlue = true; showLuminance = false; }
        if (GUILayout.Button("Luma")) { showRed = showGreen = showBlue = false; showLuminance = true; }
        if (GUILayout.Button("None")) { showRed = showGreen = showBlue = showLuminance = false; }
        EditorGUILayout.EndHorizontal();
    }

    void DrawChannelToggle(ref bool toggle, string label, Color color)
    {
        EditorGUILayout.BeginHorizontal();
        toggle = EditorGUILayout.Toggle(toggle, GUILayout.Width(20));
        var rect = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUI.DrawRect(rect, toggle ? color : new Color(0.3f, 0.3f, 0.3f));
        EditorGUILayout.LabelField(label, GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    void DrawVectorscopeSettings()
    {
        EditorGUILayout.LabelField("Vectorscope Settings", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Buffer Size:", GUILayout.Width(80));
        vectorscopeSize = EditorGUILayout.IntSlider(vectorscopeSize, 128, 512);
        EditorGUILayout.EndHorizontal();
    }

    void DrawWaveformSettings()
    {
        EditorGUILayout.LabelField("Waveform Settings", EditorStyles.miniBoldLabel);
        //waveformExposure = EditorGUILayout.Slider("Exposure",waveformExposure, 0.001f, 2.0f);
    }

    // ----- Preview image
    void DrawPreviewSection()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Captured Image", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        float aspect = (float)capturedTexture.width / capturedTexture.height;
        float width = EditorGUIUtility.currentViewWidth - 30;
        float height = Mathf.Min(width / aspect, 200f);

        Rect rect = GUILayoutUtility.GetRect(width, height);
        EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
        GUI.DrawTexture(rect, capturedTexture, ScaleMode.ScaleToFit);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Size: {capturedTexture.width}×{capturedTexture.height}", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace(); GUILayout.Toggle(autoUpdate, "Auto", GUILayout.Width(50));
        EditorGUILayout.EndHorizontal();
    }

    // ----- Analysis viewport & info
    void DrawAnalysisSection()
    {
        EditorGUILayout.Space(15);
        string title = analysisMode switch
        {
            AnalysisMode.Histogram => "Color Histogram",
            AnalysisMode.Vectorscope => "Vectorscope",
            AnalysisMode.Waveform => "Waveform",
            _ => "Analysis"
        };
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        float winW = EditorGUIUtility.currentViewWidth - 30;
        float viewH = analysisMode == AnalysisMode.Vectorscope ? winW
                   : Mathf.Clamp(winW * 0.6f, 120f, 250f);
        Rect rect = GUILayoutUtility.GetRect(winW, viewH);
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));

        if (Event.current.type == EventType.Repaint)
        {
            RenderTexture tex = analysisMode switch
            {
                AnalysisMode.Histogram => histogramTexture,
                AnalysisMode.Vectorscope => vectorscopeTexture,
                AnalysisMode.Waveform => waveformTexture,
                _ => null
            };
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
        }

        // Info bar
        EditorGUILayout.BeginHorizontal();
        switch (analysisMode)
        {
            case AnalysisMode.Histogram:
                int ch = (showRed ? 1 : 0) + (showGreen ? 1 : 0) + (showBlue ? 1 : 0) + (showLuminance ? 1 : 0);
                EditorGUILayout.LabelField($"Active Channels: {ch}/4", EditorStyles.miniLabel);
                break;
            case AnalysisMode.Vectorscope:
                EditorGUILayout.LabelField($"Buffer: {vectorscopeSize}×{vectorscopeSize}", EditorStyles.miniLabel);
                break;
            case AnalysisMode.Waveform:
                EditorGUILayout.LabelField($"Resolution: {capturedTexture.width}×{capturedTexture.height}", EditorStyles.miniLabel);
                break;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"Analysis: {analysisMode}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ───────── Capture & analysis
    void Capture()
    {
        if (!resourcesLoaded) return;
        RenderTexture src = CaptureFromCamera();
        if (src == null) return;

        switch (analysisMode)
        {
            case AnalysisMode.Histogram:
                AnalyzeHistogram(src); DrawHistogramWithShader(); break;
            case AnalysisMode.Vectorscope:
                AnalyzeVectorscope(src); DrawVectorscopeWithShader(); break;
            case AnalysisMode.Waveform:
                AnalyzeWaveform(src); DrawWaveformWithShader(); break;
        }
        RenderTexture.ReleaseTemporary(src);
    }

    RenderTexture CaptureFromCamera()
    {
        Camera cam = (captureTarget == CaptureTarget.GameView)
            ? (Camera.main ?? FindObjectOfType<Camera>())
            : (SceneView.lastActiveSceneView ?? GetWindow<SceneView>()).camera;
        if (cam == null) return null;

        int w = cam.pixelWidth, h = cam.pixelHeight;
        var linearRT = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

        var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0)
        { sRGB = true, enableRandomWrite = true };
        var srgbRT = RenderTexture.GetTemporary(desc);

        var prev = cam.targetTexture;
        cam.targetTexture = linearRT; cam.Render(); cam.targetTexture = prev;

        Graphics.Blit(linearRT, srgbRT);
        RenderTexture.ReleaseTemporary(linearRT);

        if (capturedTexture != null) DestroyImmediate(capturedTexture);
        capturedTexture = new Texture2D(w, h, TextureFormat.ARGB32, false);
        RenderTexture.active = srgbRT;
        capturedTexture.ReadPixels(new Rect(0, 0, w, h), 0, 0); capturedTexture.Apply();
        RenderTexture.active = null;

        return srgbRT;
    }

    // ----- Histogram
    void AnalyzeHistogram(RenderTexture src)
    {
        int kClear = unifiedComputeShader.FindKernel("KHistogramClear");
        int kGather = unifiedComputeShader.FindKernel("KHistogramGather");

        unifiedComputeShader.SetBuffer(kClear, "_HistogramBuffer", histogramBuffer);
        unifiedComputeShader.Dispatch(kClear, Mathf.CeilToInt((256 * 4) / 16f), 1, 1);

        unifiedComputeShader.SetTexture(kGather, "_Source", src);
        unifiedComputeShader.SetBuffer(kGather, "_HistogramBuffer", histogramBuffer);
        unifiedComputeShader.SetVector("_Params", new Vector4(src.width, src.height, 0, 0));
        unifiedComputeShader.Dispatch(kGather, Mathf.CeilToInt(src.width / 16f), Mathf.CeilToInt(src.height / 16f), 1);
    }

    void DrawHistogramWithShader()
    {
        if (histogramBuffer == null || histogramTexture == null || histogramMaterial == null) return;

        RenderTexture.active = histogramTexture;
        GL.Clear(true, true, Color.black);

        histogramMaterial.SetBuffer("_HistogramBuffer", histogramBuffer);
        histogramMaterial.SetVector("_Params", new Vector2(256, HISTOGRAM_TEXTURE_HEIGHT));
        histogramMaterial.SetFloat("_UseLogScale", useLogScale ? 1 : 0);
        histogramMaterial.SetFloat("_AmplificationFactor", amplificationFactor);
        histogramMaterial.SetFloat("_ShowRed", showRed ? 1 : 0);
        histogramMaterial.SetFloat("_ShowGreen", showGreen ? 1 : 0);
        histogramMaterial.SetFloat("_ShowBlue", showBlue ? 1 : 0);
        histogramMaterial.SetFloat("_ShowLuminance", showLuminance ? 1 : 0);

        DrawFullScreenQuad(histogramMaterial);
    }

    // ----- Vectorscope
    void AnalyzeVectorscope(RenderTexture src)
    {
        if (vectorscopeBuffer == null || vectorscopeBuffer.count != vectorscopeSize * vectorscopeSize)
        {
            vectorscopeBuffer?.Release();
            vectorscopeBuffer = new ComputeBuffer(vectorscopeSize * vectorscopeSize, sizeof(uint));
        }

        int kClear = unifiedComputeShader.FindKernel("KVectorscopeClear");
        int kGather = unifiedComputeShader.FindKernel("KVectorscopeGather");

        unifiedComputeShader.SetBuffer(kClear, "_VectorscopeBuffer", vectorscopeBuffer);
        unifiedComputeShader.SetVector("_Params", new Vector4(src.width, src.height, vectorscopeSize, 0));
        unifiedComputeShader.Dispatch(kClear, Mathf.CeilToInt(vectorscopeSize / 16f), Mathf.CeilToInt(vectorscopeSize / 16f), 1);

        unifiedComputeShader.SetTexture(kGather, "_Source", src);
        unifiedComputeShader.SetBuffer(kGather, "_VectorscopeBuffer", vectorscopeBuffer);
        unifiedComputeShader.Dispatch(kGather, Mathf.CeilToInt(src.width / 16f), Mathf.CeilToInt(src.height / 16f), 1);
    }

    void DrawVectorscopeWithShader()
    {
        if (vectorscopeBuffer == null || vectorscopeTexture == null || vectorscopeMaterial == null) return;

        RenderTexture.active = vectorscopeTexture;
        GL.Clear(true, true, Color.black);

        vectorscopeMaterial.SetBuffer("_VectorscopeBuffer", vectorscopeBuffer);
        vectorscopeMaterial.SetVector("_Params", new Vector2(vectorscopeSize, VECTORSCOPE_TEXTURE_SIZE));

        DrawFullScreenQuad(vectorscopeMaterial);
    }

    // ----- Waveform
    void AnalyzeWaveform(RenderTexture src)
    {
        if (waveformBuffer == null || waveformBuffer.count != src.width * src.height)
        {
            waveformBuffer?.Release();
            waveformBuffer = new ComputeBuffer(src.width * src.height, sizeof(uint) * 4);
        }

        int kClear = unifiedComputeShader.FindKernel("KWaveformClear");
        int kGather = unifiedComputeShader.FindKernel("KWaveformGather");

        unifiedComputeShader.SetBuffer(kClear, "_WaveformBuffer", waveformBuffer);
        unifiedComputeShader.SetVector("_Params", new Vector4(src.width, src.height, 0, 0));
        unifiedComputeShader.Dispatch(kClear, Mathf.CeilToInt(src.width / 16f), Mathf.CeilToInt(src.height / 16f), 1);

        unifiedComputeShader.SetTexture(kGather, "_Source", src);
        unifiedComputeShader.SetBuffer(kGather, "_WaveformBuffer", waveformBuffer);
        unifiedComputeShader.Dispatch(kGather, Mathf.CeilToInt(src.width / 16f), Mathf.CeilToInt(src.height / 16f), 1);
    }

    void DrawWaveformWithShader()
    {
        if (waveformBuffer == null || waveformTexture == null || waveformMaterial == null) return;

        RenderTexture.active = waveformTexture;
        GL.Clear(true, true, Color.black);

        waveformMaterial.SetBuffer("_WaveformBuffer", waveformBuffer);
        waveformMaterial.SetVector("_Params",new Vector3(capturedTexture.width,capturedTexture.height,waveformExposure));


        DrawFullScreenQuad(waveformMaterial);
    }

    // ───────── Utility
    static void DrawFullScreenQuad(Material mat)
    {
        GL.PushMatrix(); GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        mat.SetPass(0);
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.End(); GL.PopMatrix();
        RenderTexture.active = null;
    }
}
