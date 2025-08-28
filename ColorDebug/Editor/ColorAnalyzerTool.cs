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
    private int paletteSize = 8;
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

        paletteSize = EditorGUILayout.IntSlider("Colors Count", paletteSize, 4, 8);
        showColorValues = EditorGUILayout.Toggle("Show RGB Values", showColorValues);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Extracts dominant colors from the image", EditorStyles.miniLabel);
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
                EditorGUILayout.LabelField($"Extracted Colors: {colorCount}/{paletteSize}", EditorStyles.miniLabel);
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

    // ────────── Color Palette Analysis
    void AnalyzeColorPaletteWithCompute(RenderTexture sourceTexture)
    {
        if (unifiedComputeShader == null || sourceTexture == null) return;

        // 버퍼 준비
        if (colorPaletteBuffer == null || colorPaletteBuffer.count != paletteSize)
        {
            colorPaletteBuffer?.Release();
            colorCountBuffer?.Release();
            
            colorPaletteBuffer = new ComputeBuffer(paletteSize, sizeof(float) * 4);
            colorCountBuffer = new ComputeBuffer(paletteSize, sizeof(uint));
        }

        // 컴퓨트 셰이더 설정
        int kClear = unifiedComputeShader.FindKernel("KColorPaletteClear");
        int kExtract = unifiedComputeShader.FindKernel("KColorPaletteExtract");
        
        if (kClear < 0 || kExtract < 0)
        {
            Debug.LogError($"[ColorPalette] Kernel not found: Clear={kClear}, Extract={kExtract}");
            return;
        }

        unifiedComputeShader.SetTexture(kExtract, "_Source", sourceTexture);
        unifiedComputeShader.SetBuffer(kClear, "_ColorPaletteBuffer", colorPaletteBuffer);
        unifiedComputeShader.SetBuffer(kClear, "_ColorCountBuffer", colorCountBuffer);
        unifiedComputeShader.SetBuffer(kExtract, "_ColorPaletteBuffer", colorPaletteBuffer);
        unifiedComputeShader.SetBuffer(kExtract, "_ColorCountBuffer", colorCountBuffer);

        unifiedComputeShader.SetVector("_Params", new Vector4(sourceTexture.width, sourceTexture.height, paletteSize, 0));

        // 1. 버퍼 클리어
        int clearThreads = Mathf.CeilToInt(paletteSize / 16f);
        unifiedComputeShader.Dispatch(kClear, clearThreads, 1, 1);

        // 2. 색상 추출
        int threadGroupsX = Mathf.CeilToInt(sourceTexture.width / 16f);
        int threadGroupsY = Mathf.CeilToInt(sourceTexture.height / 16f);
        unifiedComputeShader.Dispatch(kExtract, threadGroupsX, threadGroupsY, 1);

        // 3. 결과 읽기
        Vector4[] paletteData = new Vector4[paletteSize];
        uint[] countData = new uint[paletteSize];
        
        colorPaletteBuffer.GetData(paletteData);
        colorCountBuffer.GetData(countData);


        // === 지능형 후처리 시스템 ===
        
        // 1단계: 유효한 색상 수집 및 중요도 계산
        var candidateColors = new System.Collections.Generic.List<(Color color, uint votes, float importance)>();
        
        for (int i = 0; i < paletteSize; i++)
        {
            if (countData[i] > 10) // 최소 투표 수 필터링
            {
                Color color = new Color(paletteData[i].x, paletteData[i].y, paletteData[i].z, 1f);
                float importance = paletteData[i].w; // 컴퓨트 셰이더에서 계산된 중요도
                candidateColors.Add((color, countData[i], importance));
            }
        }

        // 투표 수와 중요도를 결합한 스코어로 정렬
        candidateColors.Sort((a, b) => (b.votes * b.importance).CompareTo(a.votes * a.importance));

        // 2단계: 적응적 다양성 필터링 (정확한 개수 보장)
        var diverseColors = new System.Collections.Generic.List<(Color color, uint votes, float importance)>();
        float minColorDistance = 0.15f; // 초기 거리
        
        // 첫 번째 시도: 기본 거리로 필터링
        foreach (var candidate in candidateColors)
        {
            if (diverseColors.Count >= paletteSize) break;
            
            bool tooSimilar = false;
            foreach (var existing in diverseColors)
            {
                Vector3 diff = new Vector3(
                    candidate.color.r - existing.color.r,
                    candidate.color.g - existing.color.g,
                    candidate.color.b - existing.color.b
                );
                
                if (diff.magnitude < minColorDistance)
                {
                    tooSimilar = true;
                    break;
                }
            }
            
            if (!tooSimilar)
            {
                diverseColors.Add(candidate);
            }
        }
        
        // 필요한 개수에 못 미치면 거리 기준을 점진적으로 완화
        while (diverseColors.Count < paletteSize && minColorDistance > 0.05f)
        {
            minColorDistance -= 0.03f;
            diverseColors.Clear();
            
            foreach (var candidate in candidateColors)
            {
                if (diverseColors.Count >= paletteSize) break;
                
                bool tooSimilar = false;
                foreach (var existing in diverseColors)
                {
                    Vector3 diff = new Vector3(
                        candidate.color.r - existing.color.r,
                        candidate.color.g - existing.color.g,
                        candidate.color.b - existing.color.b
                    );
                    
                    if (diff.magnitude < minColorDistance)
                    {
                        tooSimilar = true;
                        break;
                    }
                }
                
                if (!tooSimilar)
                {
                    diverseColors.Add(candidate);
                }
            }
        }
        
        // 여전히 부족하면 상위 색상으로 채우기
        if (diverseColors.Count < paletteSize)
        {
            var remainingSlots = paletteSize - diverseColors.Count;
            var existing = diverseColors.Select(d => d.color).ToHashSet();
            var additional = candidateColors
                .Where(c => !existing.Contains(c.color))
                .Take(remainingSlots);
            
            diverseColors.AddRange(additional);
        }

        // 3단계: 정확히 paletteSize 개수로 최종 팔레트 생성
        dominantColors = diverseColors.Take(paletteSize).Select(item => item.color).ToArray();

        // 최종 폴백: 아무 색상도 없으면 화면의 평균 색상 사용
        if (dominantColors.Length == 0)
        {
            dominantColors = ExtractFallbackColors(sourceTexture);
        }
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
        return fallbackColors.Take(paletteSize).ToArray();
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
