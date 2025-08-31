using UnityEngine;
using UnityEditor;
using System.Linq;

public class ColorAnalyzerTool : EditorWindow
{
    private const string FOLDER_PATH = "Assets/Script/UnityScreenDebuger/ColorDebug/Shader";

    private enum CaptureTarget { GameView, SceneView }
    private enum AnalysisMode { Histogram, Vectorscope, Waveform, Saliency, ColorPalette }

    private CaptureTarget captureTarget = CaptureTarget.GameView;
    private AnalysisMode analysisMode = AnalysisMode.Histogram;

    private Texture2D capturedTexture;
    private bool autoUpdate = false;
    private float updateInterval = 0.01f;
    private double lastUpdateTime;

    // ───────── Histogram options
    private bool useLogScale = true;
    private float amplificationFactor = 2.0f;

    private bool showRed = true, showGreen = true, showBlue = true, showLuminance = true;

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
    private Texture2D vectorscopeGuide;


    // Waveform
    private ComputeBuffer waveformBuffer;
    private RenderTexture waveformTexture;
    private Material waveformMaterial;
    private float waveformExposure = 0.02f;
    private bool showRed_Waveform = true, showGreen_Waveform = true, showBlue_Waveform = true;

    // Saliency
    private RenderTexture saliencyTexture;
    private RenderTexture saliencyPreview;
    private Material saliencyMaterial;
    private float saliencyExposure = 2.0f; // 기본값 조정
    private bool saliencyNormalize = true;
    private ComputeBuffer minMaxBuffer;
    private struct MinMax { public float min; public float max; };

    // Color Palette
    private Color[] dominantColors;
    private bool showColorValues = false;
    private ComputeBuffer colorPaletteBuffer;
    private ComputeBuffer colorCountBuffer;

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

    // ───────── Lifecycle
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
        minMaxBuffer?.Release();
        colorPaletteBuffer?.Release();
        colorCountBuffer?.Release();

        if (histogramTexture != null) { histogramTexture.Release(); DestroyImmediate(histogramTexture); }
        if (vectorscopeTexture != null) { vectorscopeTexture.Release(); DestroyImmediate(vectorscopeTexture); }
        if (waveformTexture != null) { waveformTexture.Release(); DestroyImmediate(waveformTexture); }
        if (saliencyTexture != null) { saliencyTexture.Release(); DestroyImmediate(saliencyTexture); }
        if (saliencyPreview != null) { saliencyPreview.Release(); DestroyImmediate(saliencyPreview); } // 수정: 추가

        if (histogramMaterial != null) DestroyImmediate(histogramMaterial);
        if (vectorscopeMaterial != null) DestroyImmediate(vectorscopeMaterial);
        if (waveformMaterial != null) DestroyImmediate(waveformMaterial);
        if (saliencyMaterial != null) DestroyImmediate(saliencyMaterial);
    }

    // ───────── Resource loading
    void LoadResources()
    {
        string csPath = $"{FOLDER_PATH}/UnifiedImageAnalysis.compute";
        unifiedComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(csPath);
        if (unifiedComputeShader == null)
        {
            Debug.LogError($"[ColorAnalyzerTool] Compute shader not found: {csPath}");
            resourcesLoaded = false;
            return;
        }

        LoadHistogramResources();
        LoadVectorscopeResources();
        LoadWaveformResources();
        LoadSaliencyResources();

        resourcesLoaded = true;
        
    }

    void LoadHistogramResources()
    {
        string path = $"{FOLDER_PATH}/Histogram.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null) { Debug.LogError($"Histogram shader missing: {path}"); return; }

        histogramBuffer = new ComputeBuffer(256 * 4, sizeof(uint));
        histogramTexture = new RenderTexture(256, HISTOGRAM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32)
        { enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        histogramTexture.Create();

        histogramMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void LoadVectorscopeResources()
    {
        string path = $"{FOLDER_PATH}/Vectorscope.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        

        vectorscopeBuffer = new ComputeBuffer(vectorscopeSize * vectorscopeSize, sizeof(uint));
        vectorscopeTexture = new RenderTexture(VECTORSCOPE_TEXTURE_SIZE, VECTORSCOPE_TEXTURE_SIZE, 0,
                                               RenderTextureFormat.ARGB32)
        { enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        vectorscopeTexture.Create();

        vectorscopeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        string guidePath = $"Assets/Script/UnityScreenDebuger/ColorDebug/Texture/ColorScope.png";
        vectorscopeGuide = AssetDatabase.LoadAssetAtPath<Texture2D>(guidePath);
        
    }

    void LoadWaveformResources()
    {
        string path = $"{FOLDER_PATH}/Waveform.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        

        waveformTexture = new RenderTexture(512, WAVEFORM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32)
        { enableRandomWrite = true, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        waveformTexture.Create();

        waveformMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void LoadSaliencyResources()
    {
        string path = $"{FOLDER_PATH}/SaliencyMap.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null)
        {
            Debug.LogError($"SaliencyMap shader missing: {path}");
            return;
        }

        saliencyMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        // minMaxBuffer will be created on demand
        
    }

    // ───────── Update & GUI
    void OnEditorUpdate()
    {
        if (autoUpdate && EditorApplication.timeSinceStartup - lastUpdateTime > updateInterval)
        {
            lastUpdateTime = EditorApplication.timeSinceStartup;
            Capture(); Repaint();
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
            EditorGUILayout.HelpBox("Failed to load resources. Check console for errors.", MessageType.Error);
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

    // ----- Capture UI
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

        autoUpdate = EditorGUILayout.Toggle("Auto Update", autoUpdate);
        EditorGUILayout.Space(5);

        if (GUILayout.Button("Capture", GUILayout.Height(20))) Capture();
        EditorGUILayout.EndVertical();
    }

    // ----- Per-analysis option UI
    void DrawAnalysisSettingsSection()
    {
        EditorGUILayout.BeginVertical("box");
        switch (analysisMode)
        {
            case AnalysisMode.Histogram: DrawHistogramSettings(); break;
            case AnalysisMode.Vectorscope: DrawVectorscopeSettings(); break;
            case AnalysisMode.Waveform: DrawWaveformSettings(); break;
            case AnalysisMode.Saliency: DrawSaliencySettings(); break;
            case AnalysisMode.ColorPalette: DrawColorPaletteSettings(); break;
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
        if (GUILayout.Button("All")) showRed = showGreen = showBlue = showLuminance = true;
        if (GUILayout.Button("RGB")) { showRed = showGreen = showBlue = true; showLuminance = false; }
        if (GUILayout.Button("Luma")) { showRed = showGreen = showBlue = false; showLuminance = true; }
        if (GUILayout.Button("None")) showRed = showGreen = showBlue = showLuminance = false;
        EditorGUILayout.EndHorizontal();
    }

    void DrawChannelToggle(ref bool toggle, string label, Color col)
    {
        EditorGUILayout.BeginHorizontal();
        toggle = EditorGUILayout.Toggle(toggle, GUILayout.Width(20));
        var r = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUI.DrawRect(r, toggle ? col : new Color(0.3f, 0.3f, 0.3f));
        EditorGUILayout.LabelField(label, GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    void DrawVectorscopeSettings()
    {
        EditorGUILayout.LabelField("Vectorscope Settings", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);
        vectorscopeSize = EditorGUILayout.IntSlider("Buffer Size", vectorscopeSize, 128, 512);
    }

    void DrawWaveformSettings()
    {
        EditorGUILayout.LabelField("Waveform Settings", EditorStyles.miniBoldLabel);
        //waveformExposure = EditorGUILayout.Slider("Exposure", waveformExposure, 0.001f, 2f);

        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("Channel Visibility", EditorStyles.miniBoldLabel);
        DrawChannelToggle(ref showRed_Waveform, "Red Channel", Color.red);
        DrawChannelToggle(ref showGreen_Waveform, "Green Channel", Color.green);
        DrawChannelToggle(ref showBlue_Waveform, "Blue Channel", Color.blue);

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All")) showRed_Waveform = showGreen_Waveform = showBlue_Waveform = true;
        if (GUILayout.Button("RGB")) { showRed_Waveform = showGreen_Waveform = showBlue_Waveform = true; }
        if (GUILayout.Button("None")) showRed_Waveform = showGreen_Waveform = showBlue_Waveform = false;
        EditorGUILayout.EndHorizontal();
    }

    void DrawSaliencySettings()
    {
        EditorGUILayout.LabelField("Saliency Settings", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);

        saliencyNormalize = EditorGUILayout.Toggle("Auto-Normalize", saliencyNormalize);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Red = High Saliency, Blue = Low Saliency", EditorStyles.miniLabel);
    }

    void DrawColorPaletteSettings()
    {
        EditorGUILayout.LabelField("Color Palette Settings", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);

        showColorValues = EditorGUILayout.Toggle("Show RGB Values", showColorValues);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Extracts 5 dominant colors using LAB K-Means clustering", EditorStyles.miniLabel);
    }

    // ----- Preview
    void DrawPreviewSection()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Captured Image", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        float aspect = (float)capturedTexture.width / capturedTexture.height;
        float width = EditorGUIUtility.currentViewWidth - 30;
        float height = Mathf.Min(width / aspect, 200f);

        var rect = GUILayoutUtility.GetRect(width, height);
        EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
        GUI.DrawTexture(rect, capturedTexture, ScaleMode.ScaleToFit);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Size: {capturedTexture.width}×{capturedTexture.height}", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.Toggle("Auto", autoUpdate, GUILayout.Width(50));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    // ----- Analysis viewport & info
    void DrawAnalysisSection()
    {
        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField(analysisMode.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        // ColorPalette는 텍스트로 표시하므로 별도 처리
        if (analysisMode == AnalysisMode.ColorPalette)
        {
            DrawColorPalette();
            return;
        }

        float winW = EditorGUIUtility.currentViewWidth - 30;
        // Vectorscope는 높이를 절반으로 줄이고, 나머지는 원래 비율 유지
        float viewH = analysisMode == AnalysisMode.Vectorscope ? winW / 2f
                               : Mathf.Clamp(winW * 0.6f, 120f, 250f);

        var rect = GUILayoutUtility.GetRect(winW, viewH);
        EditorGUI.DrawRect(rect, Color.black);

        if (Event.current.type == EventType.Repaint)
        {
            // ───── Vectorscope 전용: 가이드 + 결과 오버레이 ─────
            if (analysisMode == AnalysisMode.Vectorscope)
            {
                // 뷰의 높이를 기준으로 정사각형 scopeRect를 계산하고 중앙에 배치
                float scopeSize = rect.height;
                float xOffset = (rect.width - scopeSize) / 2;
                var scopeRect = new Rect(rect.x + xOffset, rect.y, scopeSize, scopeSize);

                // 1) 색상환 가이드 (불투명)
                if (vectorscopeGuide != null)
                    GUI.DrawTexture(scopeRect, vectorscopeGuide, ScaleMode.StretchToFill);

                // 2) 연산 결과 텍스처 (알파 포함)
                if (vectorscopeTexture != null)
                    GUI.DrawTexture(scopeRect, vectorscopeTexture, ScaleMode.StretchToFill);

                return; // 여기서 끝내면 아래 공용 코드 실행 안 함
            }

            // ───── 공용 처리: 모드별 결과 텍스처 하나만 ─────
            RenderTexture tex = analysisMode switch
            {
                AnalysisMode.Histogram => histogramTexture,
                AnalysisMode.Waveform => waveformTexture,
                AnalysisMode.Saliency => saliencyPreview,
                _ => null
            };

            if (tex != null)
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
        }

        // info bar
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
                int ch_w = (showRed_Waveform ? 1 : 0) + (showGreen_Waveform ? 1 : 0) + (showBlue_Waveform ? 1 : 0);
                EditorGUILayout.LabelField($"Active Channels: {ch_w}/3", EditorStyles.miniLabel);
                break;
            case AnalysisMode.Saliency:
                string status = (saliencyTexture != null && saliencyPreview != null) ? "Ready" : "Error";
                EditorGUILayout.LabelField($"Status: {status} | Intensity: {saliencyExposure:F1}", EditorStyles.miniLabel);
                break;
            case AnalysisMode.ColorPalette:
                int colorCount = dominantColors != null ? dominantColors.Length : 0;
                EditorGUILayout.LabelField($"Extracted Colors: {colorCount}/5 (LAB K-Means)", EditorStyles.miniLabel);
                break;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"Analysis: {analysisMode}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ───────── Capture & analysis
    void Capture()
    {
        

        var src = CaptureFromCamera();
        

        try
        {
            switch (analysisMode)
            {
                case AnalysisMode.Histogram:
                    AnalyzeHistogram(src);
                    DrawHistogramWithShader();
                    break;
                case AnalysisMode.Vectorscope:
                    AnalyzeVectorscope(src);
                    DrawVectorscopeWithShader();
                    break;
                case AnalysisMode.Waveform:
                    AnalyzeWaveform(src);
                    DrawWaveformWithShader();
                    break;
                case AnalysisMode.Saliency:
                    AnalyzeSaliency(src);
                    DrawSaliencyWithShader();
                    break;
                case AnalysisMode.ColorPalette:
                    AnalyzeColorPaletteWithCompute(src);
                    DrawColorPalette();
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ColorAnalyzerTool] Analysis error: {e.Message}");
        }
        finally
        {
            RenderTexture.ReleaseTemporary(src);
        }
    }

    RenderTexture CaptureFromCamera()
    {
        Camera cam = (captureTarget == CaptureTarget.GameView)
            ? (Camera.main ?? FindObjectOfType<Camera>())
            : (SceneView.lastActiveSceneView ?? GetWindow<SceneView>()).camera;

        if (cam == null)
        {
            Debug.LogError("[ColorAnalyzerTool] No camera found for capture");
            return null;
        }

        int w = cam.pixelWidth, h = cam.pixelHeight;
        if (w <= 0 || h <= 0)
        {
            Debug.LogError($"[ColorAnalyzerTool] Invalid camera dimensions: {w}x{h}");
            return null;
        }

        var linearRT = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

        var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0)
        { sRGB = true, enableRandomWrite = true };
        var srgbRT = RenderTexture.GetTemporary(desc);

        var prev = cam.targetTexture;
        cam.targetTexture = linearRT;
        cam.Render();
        cam.targetTexture = prev;

        Graphics.Blit(linearRT, srgbRT);
        RenderTexture.ReleaseTemporary(linearRT);

        if (capturedTexture != null) DestroyImmediate(capturedTexture);
        capturedTexture = new Texture2D(w, h, TextureFormat.ARGB32, false);
        RenderTexture.active = srgbRT;
        capturedTexture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        capturedTexture.Apply();
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
        GL.Clear(true, true, Color.clear);   // 투명 RT 확보

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
        waveformMaterial.SetVector("_Params", new Vector3(capturedTexture.width, capturedTexture.height, waveformExposure));
        waveformMaterial.SetFloat("_ShowRed", showRed_Waveform ? 1 : 0);
        waveformMaterial.SetFloat("_ShowGreen", showGreen_Waveform ? 1 : 0);
        waveformMaterial.SetFloat("_ShowBlue", showBlue_Waveform ? 1 : 0);

        DrawFullScreenQuad(waveformMaterial);
    }

    // ----- Saliency
    void AnalyzeSaliency(RenderTexture src)
    {
        int kSaliency = unifiedComputeShader.FindKernel("KSaliencyMap");
        int kReduce = unifiedComputeShader.FindKernel("KSaliencyReduce");

        if (kSaliency < 0 || kReduce < 0)
        {
            Debug.LogError("[ColorAnalyzerTool] Saliency kernels not found in compute shader");
            return;
        }

        // Saliency texture 생성/재생성
        if (saliencyTexture == null || saliencyTexture.width != src.width || saliencyTexture.height != src.height)
        {
            if (saliencyTexture != null)
            {
                saliencyTexture.Release();
                DestroyImmediate(saliencyTexture);
            }

            saliencyTexture = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.RFloat)
            {
                enableRandomWrite = true,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };

            if (!saliencyTexture.Create())
            {
                Debug.LogError("[ColorAnalyzerTool] Failed to create saliency texture");
                return;
            }

            
        }

        // 1. Saliency Map 생성
        unifiedComputeShader.SetTexture(kSaliency, "_Source", src);
        unifiedComputeShader.SetTexture(kSaliency, "_SaliencyMap", saliencyTexture);
        unifiedComputeShader.SetVector("_Params", new Vector4(src.width, src.height, 0, 0));

        int threadGroupsX = Mathf.CeilToInt(src.width / 16f);
        int threadGroupsY = Mathf.CeilToInt(src.height / 16f);

        unifiedComputeShader.Dispatch(kSaliency, threadGroupsX, threadGroupsY, 1);

        // 2. Min/Max 값 계산 (정규화가 필요할 경우)
        if (saliencyNormalize)
        {
            int numGroups = threadGroupsX * threadGroupsY;
            if (minMaxBuffer == null || minMaxBuffer.count != numGroups)
            {
                minMaxBuffer?.Release();
                minMaxBuffer = new ComputeBuffer(numGroups, sizeof(float) * 2);
            }

            unifiedComputeShader.SetTexture(kReduce, "_SaliencyMap", saliencyTexture);
            unifiedComputeShader.SetBuffer(kReduce, "_MinMaxBuffer", minMaxBuffer);
            unifiedComputeShader.Dispatch(kReduce, threadGroupsX, threadGroupsY, 1);
        }

        
    }

    void DrawSaliencyWithShader()
    {
        if (saliencyMaterial == null || saliencyTexture == null)
        {
            Debug.LogError("[ColorAnalyzerTool] Saliency resources not available");
            return;
        }

        // 미리보기용 RT 준비
        if (saliencyPreview == null ||
            saliencyPreview.width != saliencyTexture.width ||
            saliencyPreview.height != saliencyTexture.height)
        {
            if (saliencyPreview != null)
            {
                saliencyPreview.Release();
                DestroyImmediate(saliencyPreview);
            }

            saliencyPreview = new RenderTexture(
                saliencyTexture.width, saliencyTexture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            saliencyPreview.hideFlags = HideFlags.HideAndDontSave;
            saliencyPreview.filterMode = FilterMode.Bilinear;
            saliencyPreview.enableRandomWrite = false;

            if (!saliencyPreview.Create())
            {
                Debug.LogError("[ColorAnalyzerTool] Failed to create saliency preview texture");
                return;
            }
        }

        // Heat-map 변환
        saliencyMaterial.SetFloat("_Exposure", saliencyExposure);
        saliencyMaterial.SetTexture("_MainTex", saliencyTexture);
        saliencyMaterial.SetFloat("_Normalize", saliencyNormalize ? 1.0f : 0.0f);

        if (saliencyNormalize)
        {
            int numGroups = Mathf.CeilToInt(saliencyTexture.width / 16f) * Mathf.CeilToInt(saliencyTexture.height / 16f);
            var data = new MinMax[numGroups];
            minMaxBuffer.GetData(data);

            float minVal = data.Select(d => d.min).Min();
            float maxVal = data.Select(d => d.max).Max();

            saliencyMaterial.SetVector("_MinMax", new Vector4(minVal, maxVal, 0, 0));
        }

        Graphics.Blit(saliencyTexture, saliencyPreview, saliencyMaterial);

        
    }

    // ────────── Color Palette Analysis (GPU Histogram 기반)
    void AnalyzeColorPaletteWithHistogram(RenderTexture sourceTexture)
    {
        if (sourceTexture == null) return;

        // 1/30 다운샘플링된 텍스처 생성
        int downsampledWidth = sourceTexture.width / 30;
        int downsampledHeight = sourceTexture.height / 30;
        
        var downsampledTexture = RenderTexture.GetTemporary(downsampledWidth, downsampledHeight, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(sourceTexture, downsampledTexture);

        // 기존 히스토그램 방식 사용
        AnalyzeHistogram(downsampledTexture);
        
        // 히스토그램 버퍼에서 데이터 읽기
        uint[] histogramData = new uint[256 * 4];
        histogramBuffer.GetData(histogramData);
        
        // RGB 히스토그램을 색상 히스토그램으로 변환
        var colorHistogram = new System.Collections.Generic.Dictionary<Color, uint>();
        
        // 모든 가능한 4단계 양자화 색상에 대해 히스토그램 계산
        for (int r = 0; r <= 3; r++)
        {
            for (int g = 0; g <= 3; g++)
            {
                for (int b = 0; b <= 3; b++)
                {
                    Color quantizedColor = new Color(r * 85f / 255f, g * 85f / 255f, b * 85f / 255f, 1f);
                    
                    // 해당 색상 범위의 히스토그램 값 합계
                    uint totalCount = 0;
                    
                    int rStart = r * 64;  // 0, 64, 128, 192
                    int gStart = g * 64;
                    int bStart = b * 64;
                    
                    int rEnd = Mathf.Min(rStart + 64, 255);
                    int gEnd = Mathf.Min(gStart + 64, 255);
                    int bEnd = Mathf.Min(bStart + 64, 255);
                    
                    for (int ri = rStart; ri <= rEnd; ri++)
                    {
                        for (int gi = gStart; gi <= gEnd; gi++)
                        {
                            for (int bi = bStart; bi <= bEnd; bi++)
                            {
                                // 히스토그램에서 해당 값 찾기 (간소화된 버전)
                                if (ri < 256 && gi < 256 && bi < 256)
                                {
                                    totalCount += histogramData[ri] + histogramData[256 + gi] + histogramData[512 + bi];
                                }
                            }
                        }
                    }
                    
                    if (totalCount > 0)
                    {
                        colorHistogram[quantizedColor] = totalCount;
                    }
                }
            }
        }
        
        // 히스토그램에서 직접 상위 5개 추출
        var extractedColors = ExtractHistogramPeaks(colorHistogram, 5);
        
        dominantColors = extractedColors.Take(5).ToArray();
        
        // 다운샘플링된 텍스처 해제
        RenderTexture.ReleaseTemporary(downsampledTexture);
    }

    // ────────── Color Palette Analysis (LAB + K-Means 방식)
    void AnalyzeColorPaletteWithCompute(RenderTexture sourceTexture)
    {
        if (sourceTexture == null) return;

        // 1/15 다운샘플링된 텍스처 생성
        int downsampledWidth = sourceTexture.width / 15;
        int downsampledHeight = sourceTexture.height / 15;
        
        var downsampledTexture = RenderTexture.GetTemporary(downsampledWidth, downsampledHeight, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(sourceTexture, downsampledTexture);

        // CPU에서 직접 픽셀 읽기
        Texture2D readableTexture = new Texture2D(downsampledWidth, downsampledHeight, TextureFormat.ARGB32, false);
        RenderTexture.active = downsampledTexture;
        readableTexture.ReadPixels(new Rect(0, 0, downsampledWidth, downsampledHeight), 0, 0);
        readableTexture.Apply();
        RenderTexture.active = null;

        // LAB 색공간으로 변환 및 샘플링
        Color32[] pixels = readableTexture.GetPixels32();
        var labColors = new System.Collections.Generic.List<Vector3>();
        var originalColors = new System.Collections.Generic.List<Color>();
        
        // 픽셀 샘플링 (너무 많으면 성능 문제로 일부만 샘플링)
        int sampleStep = Mathf.Max(1, pixels.Length / 1000); // 최대 1000개 픽셀만 샘플링
        
        for (int i = 0; i < pixels.Length; i += sampleStep)
        {
            Color rgbColor = new Color(pixels[i].r / 255f, pixels[i].g / 255f, pixels[i].b / 255f, 1f);
            Vector3 labColor = RGBToLAB(rgbColor);
            
            labColors.Add(labColor);
            originalColors.Add(rgbColor);
        }

        // K-Means 클러스터링으로 5개 대표색 추출
        var clusterCenters = PerformKMeansInLAB(labColors.ToArray(), originalColors.ToArray(), 5);
        
        // LAB에서 RGB로 다시 변환
        dominantColors = clusterCenters.Select(center => LABToRGB(center)).ToArray();
        
        // 리소스 정리
        DestroyImmediate(readableTexture);
        RenderTexture.ReleaseTemporary(downsampledTexture);
    }

    void DrawColorPalette()
    {
        if (dominantColors == null || dominantColors.Length == 0)
        {
            EditorGUILayout.LabelField("No colors extracted", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        EditorGUILayout.LabelField("Dominant Colors:", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);
        
        // 화면 전체 너비 사용
        float winW = EditorGUIUtility.currentViewWidth - 30;
        float totalHeight = 150f; // 고정 높이
        
        var rect = GUILayoutUtility.GetRect(winW, totalHeight);
        
        if (Event.current.type == EventType.Repaint)
        {
            // 배경 그리기
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            
            // 각 색상의 너비 계산 (비례적으로)
            float colorWidth = rect.width / dominantColors.Length;
            
            for (int i = 0; i < dominantColors.Length; i++)
            {
                Color color = dominantColors[i];
                
                Rect colorRect = new Rect(
                    rect.x + (i * colorWidth), 
                    rect.y, 
                    colorWidth, 
                    rect.height
                );
                
                // 색상 사각형 그리기
                EditorGUI.DrawRect(colorRect, color);
                
                // 테두리 그리기 (색상 구분)
                if (i > 0)
                {
                    EditorGUI.DrawRect(new Rect(colorRect.x, colorRect.y, 1, colorRect.height), Color.black);
                }
                
                // Hex 텍스트 표시 (중앙 정렬)
                string hexColor = ColorUtility.ToHtmlStringRGB(color);
                
                // 텍스트 색상 결정 (명도에 따라)
                float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
                Color textColor = luminance > 0.5f ? Color.black : Color.white;
                
                GUIStyle textStyle = new GUIStyle(EditorStyles.miniLabel);
                textStyle.normal.textColor = textColor;
                textStyle.alignment = TextAnchor.MiddleCenter;
                textStyle.fontStyle = FontStyle.Bold;
                
                // 텍스트 위치 계산
                Rect textRect = new Rect(colorRect.x, colorRect.y + colorRect.height * 0.4f, colorRect.width, 20);
                GUI.Label(textRect, $"#{hexColor}", textStyle);
                
                // RGB 값 표시 (선택적)
                if (showColorValues)
                {
                    Rect rgbRect = new Rect(colorRect.x, colorRect.y + colorRect.height * 0.6f, colorRect.width, 15);
                    textStyle.fontSize = 9;
                    GUI.Label(rgbRect, $"{(int)(color.r * 255)},{(int)(color.g * 255)},{(int)(color.b * 255)}", textStyle);
                }
            }
        }
        
        // 클릭 감지
        if (rect.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown)
        {
            float colorWidth = rect.width / dominantColors.Length;
            int clickedIndex = (int)((Event.current.mousePosition.x - rect.x) / colorWidth);
            
            if (clickedIndex >= 0 && clickedIndex < dominantColors.Length)
            {
                string hexColor = ColorUtility.ToHtmlStringRGB(dominantColors[clickedIndex]);
                EditorGUIUtility.systemCopyBuffer = "#" + hexColor;
                Debug.Log($"Color #{hexColor} copied to clipboard");
                Event.current.Use();
            }
        }
        
        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("Click color to copy hex value", EditorStyles.miniLabel);
    }


    // ───────── LAB Color Space Conversion
    Vector3 RGBToLAB(Color rgb)
    {
        // RGB to XYZ
        float r = rgb.r > 0.04045f ? Mathf.Pow((rgb.r + 0.055f) / 1.055f, 2.4f) : rgb.r / 12.92f;
        float g = rgb.g > 0.04045f ? Mathf.Pow((rgb.g + 0.055f) / 1.055f, 2.4f) : rgb.g / 12.92f;
        float b = rgb.b > 0.04045f ? Mathf.Pow((rgb.b + 0.055f) / 1.055f, 2.4f) : rgb.b / 12.92f;

        float x = (r * 0.4124f + g * 0.3576f + b * 0.1805f) / 0.95047f;
        float y = (r * 0.2126f + g * 0.7152f + b * 0.0722f) / 1.00000f;
        float z = (r * 0.0193f + g * 0.1192f + b * 0.9505f) / 1.08883f;

        // XYZ to LAB
        x = x > 0.008856f ? Mathf.Pow(x, 1f/3f) : (7.787f * x + 16f/116f);
        y = y > 0.008856f ? Mathf.Pow(y, 1f/3f) : (7.787f * y + 16f/116f);
        z = z > 0.008856f ? Mathf.Pow(z, 1f/3f) : (7.787f * z + 16f/116f);

        float L = 116f * y - 16f;
        float A = 500f * (x - y);
        float B = 200f * (y - z);

        return new Vector3(L, A, B);
    }

    Color LABToRGB(Vector3 lab)
    {
        // LAB to XYZ
        float y = (lab.x + 16f) / 116f;
        float x = lab.y / 500f + y;
        float z = y - lab.z / 200f;

        x = x > 0.206893f ? x * x * x : (x - 16f/116f) / 7.787f;
        y = y > 0.206893f ? y * y * y : (y - 16f/116f) / 7.787f;
        z = z > 0.206893f ? z * z * z : (z - 16f/116f) / 7.787f;

        x *= 0.95047f;
        y *= 1.00000f;
        z *= 1.08883f;

        // XYZ to RGB
        float r = x *  3.2406f + y * -1.5372f + z * -0.4986f;
        float g = x * -0.9689f + y *  1.8758f + z *  0.0415f;
        float b = x *  0.0557f + y * -0.2040f + z *  1.0570f;

        r = r > 0.0031308f ? 1.055f * Mathf.Pow(r, 1f/2.4f) - 0.055f : 12.92f * r;
        g = g > 0.0031308f ? 1.055f * Mathf.Pow(g, 1f/2.4f) - 0.055f : 12.92f * g;
        b = b > 0.0031308f ? 1.055f * Mathf.Pow(b, 1f/2.4f) - 0.055f : 12.92f * b;

        return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
    }

    // ───────── K-Means Clustering in LAB Space
    Vector3[] PerformKMeansInLAB(Vector3[] labColors, Color[] originalColors, int k)
    {
        if (labColors.Length == 0) return new Vector3[0];
        if (labColors.Length <= k) return labColors.Take(k).ToArray();

        var random = new System.Random(42); // 시드 고정으로 결정적 결과
        var centroids = new Vector3[k];
        
        // K-means++ 초기화 (더 좋은 초기 중심점)
        centroids[0] = labColors[random.Next(labColors.Length)];
        
        for (int i = 1; i < k; i++)
        {
            var distances = new float[labColors.Length];
            float totalDistance = 0;
            
            for (int j = 0; j < labColors.Length; j++)
            {
                float minDistanceToCentroid = float.MaxValue;
                
                for (int c = 0; c < i; c++)
                {
                    float distance = LABDistance(labColors[j], centroids[c]);
                    minDistanceToCentroid = Mathf.Min(minDistanceToCentroid, distance);
                }
                
                distances[j] = minDistanceToCentroid * minDistanceToCentroid;
                totalDistance += distances[j];
            }
            
            float targetDistance = (float)random.NextDouble() * totalDistance;
            float currentDistance = 0;
            
            for (int j = 0; j < labColors.Length; j++)
            {
                currentDistance += distances[j];
                if (currentDistance >= targetDistance)
                {
                    centroids[i] = labColors[j];
                    break;
                }
            }
        }
        
        // K-means 반복
        for (int iter = 0; iter < 20; iter++)
        {
            var clusters = new System.Collections.Generic.List<Vector3>[k];
            for (int i = 0; i < k; i++) clusters[i] = new System.Collections.Generic.List<Vector3>();
            
            // 각 점을 가장 가까운 중심점에 할당
            foreach (var color in labColors)
            {
                float minDistance = float.MaxValue;
                int bestCluster = 0;
                
                for (int i = 0; i < k; i++)
                {
                    float distance = LABDistance(color, centroids[i]);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestCluster = i;
                    }
                }
                
                clusters[bestCluster].Add(color);
            }
            
            // 새로운 중심점 계산
            bool converged = true;
            for (int i = 0; i < k; i++)
            {
                if (clusters[i].Count == 0) continue;
                
                Vector3 newCentroid = Vector3.zero;
                foreach (var point in clusters[i])
                {
                    newCentroid += point;
                }
                newCentroid /= clusters[i].Count;
                
                if (LABDistance(centroids[i], newCentroid) > 1.0f)
                {
                    converged = false;
                }
                
                centroids[i] = newCentroid;
            }
            
            if (converged) break;
        }
        
        return centroids;
    }
    
    float LABDistance(Vector3 lab1, Vector3 lab2)
    {
        return Vector3.Distance(lab1, lab2);
    }

    // ───────── Color Quantization (백업용)
    Color QuantizeColor(Color color)
    {
        // RGB를 4단계로 양자화 (0, 85, 170, 255) - 최대 안정성을 위한 극강 양자화
        int r = Mathf.RoundToInt(color.r * 3) * 85;
        int g = Mathf.RoundToInt(color.g * 3) * 85;
        int b = Mathf.RoundToInt(color.b * 3) * 85;
        
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
    
    // ───────── K-means Clustering
    System.Collections.Generic.List<Color> PerformKMeansClustering(System.Collections.Generic.Dictionary<Color, uint> histogram, int k)
    {
        if (histogram.Count == 0) return new System.Collections.Generic.List<Color>();
        
        var colors = histogram.Keys.ToArray();
        var weights = histogram.Values.ToArray();
        
        if (colors.Length <= k)
        {
            return colors.OrderByDescending(c => histogram[c]).ToList();
        }
        
        // K-means 초기 중심점 설정 (가중치 기반 선택)
        var centroids = new Color[k];
        uint totalWeight = 0;
        for (int i = 0; i < weights.Length; i++) totalWeight += weights[i];
        var selectedIndices = new System.Collections.Generic.HashSet<int>();
        
        for (int i = 0; i < k; i++)
        {
            float targetWeight = (float)(i + 1) * totalWeight / (k + 1);
            float currentWeight = 0;
            
            for (int j = 0; j < colors.Length; j++)
            {
                if (selectedIndices.Contains(j)) continue;
                
                currentWeight += weights[j];
                if (currentWeight >= targetWeight)
                {
                    centroids[i] = colors[j];
                    selectedIndices.Add(j);
                    break;
                }
            }
        }
        
        // K-means 반복 (최대 10회)
        for (int iter = 0; iter < 10; iter++)
        {
            var clusters = new System.Collections.Generic.List<Color>[k];
            var clusterWeights = new System.Collections.Generic.List<uint>[k];
            
            for (int i = 0; i < k; i++)
            {
                clusters[i] = new System.Collections.Generic.List<Color>();
                clusterWeights[i] = new System.Collections.Generic.List<uint>();
            }
            
            // 각 색상을 가장 가까운 중심점에 할당
            foreach (var kvp in histogram)
            {
                float minDistance = float.MaxValue;
                int bestCluster = 0;
                
                for (int i = 0; i < k; i++)
                {
                    float distance = ColorDistance(kvp.Key, centroids[i]);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestCluster = i;
                    }
                }
                
                clusters[bestCluster].Add(kvp.Key);
                clusterWeights[bestCluster].Add(kvp.Value);
            }
            
            // 새로운 중심점 계산 (가중 평균)
            bool converged = true;
            for (int i = 0; i < k; i++)
            {
                if (clusters[i].Count == 0) continue;
                
                float totalR = 0, totalG = 0, totalB = 0;
                uint totalWeight2 = 0;
                
                for (int j = 0; j < clusters[i].Count; j++)
                {
                    var color = clusters[i][j];
                    var weight = clusterWeights[i][j];
                    
                    totalR += color.r * weight;
                    totalG += color.g * weight;
                    totalB += color.b * weight;
                    totalWeight2 += weight;
                }
                
                var newCentroid = new Color(totalR / totalWeight2, totalG / totalWeight2, totalB / totalWeight2, 1f);
                
                if (ColorDistance(centroids[i], newCentroid) > 0.01f)
                {
                    converged = false;
                }
                
                centroids[i] = newCentroid;
            }
            
            if (converged) break;
        }
        
        // 중심점들을 가중치 기준으로 정렬
        var result = new System.Collections.Generic.List<(Color color, uint weight)>();
        
        for (int i = 0; i < k; i++)
        {
            uint clusterWeight = 0;
            foreach (var kvp in histogram)
            {
                if (GetClosestCentroid(kvp.Key, centroids) == i)
                {
                    clusterWeight += kvp.Value;
                }
            }
            result.Add((centroids[i], clusterWeight));
        }
        
        return result.OrderByDescending(x => x.weight).Select(x => x.color).ToList();
    }
    
    float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    }
    
    int GetClosestCentroid(Color color, Color[] centroids)
    {
        float minDistance = float.MaxValue;
        int closest = 0;
        
        for (int i = 0; i < centroids.Length; i++)
        {
            float distance = ColorDistance(color, centroids[i]);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = i;
            }
        }
        
        return closest;
    }
    
    // ───────── Histogram Peak Detection
    System.Collections.Generic.List<Color> ExtractHistogramPeaks(System.Collections.Generic.Dictionary<Color, uint> histogram, int count)
    {
        return histogram
            .OrderByDescending(kvp => kvp.Value) // 1차: 픽셀 수 기준
            .ThenBy(kvp => kvp.Key.r + kvp.Key.g + kvp.Key.b) // 2차: RGB 합계 기준 (일관된 순서)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    // ───────── Fallback Color Extraction
    Color[] ExtractFallbackColors(RenderTexture sourceTexture)
    {
        // 간단한 그리드 샘플링으로 대표 색상 추출
        int gridSize = 3; // 3x3 그리드
        Color[] fallbackColors = new Color[gridSize * gridSize];
        
        float stepX = (float)sourceTexture.width / gridSize;
        float stepY = (float)sourceTexture.height / gridSize;
        
        RenderTexture.active = sourceTexture;
        Texture2D tempTex = new Texture2D(sourceTexture.width, sourceTexture.height);
        tempTex.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;
        
        int colorIndex = 0;
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                int pixelX = Mathf.FloorToInt((x + 0.5f) * stepX);
                int pixelY = Mathf.FloorToInt((y + 0.5f) * stepY);
                
                pixelX = Mathf.Clamp(pixelX, 0, sourceTexture.width - 1);
                pixelY = Mathf.Clamp(pixelY, 0, sourceTexture.height - 1);
                
                fallbackColors[colorIndex] = tempTex.GetPixel(pixelX, pixelY);
                colorIndex++;
            }
        }
        
        DestroyImmediate(tempTex);
        return fallbackColors.Take(5).ToArray();
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
