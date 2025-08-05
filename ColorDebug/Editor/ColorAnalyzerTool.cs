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

    // 히스토그램 표시 옵션
    private bool useLogScale = true;
    private float amplificationFactor = 2.0f;

    // 채널 토글 옵션
    private bool showRed = true;
    private bool showGreen = true;
    private bool showBlue = true;
    private bool showLuminance = true;

    // 통합 컴퓨트 쉐이더
    private ComputeShader unifiedComputeShader;

    // 히스토그램 리소스
    private ComputeBuffer histogramBuffer;
    private RenderTexture histogramTexture;
    private Material histogramMaterial;

    // 벡터스코프 리소스
    private ComputeBuffer vectorscopeBuffer;
    private RenderTexture vectorscopeTexture;
    private Material vectorscopeMaterial;
    private int vectorscopeSize = 256;
    private bool useLinearColorSpace = false;

    // 웨이브폼 리소스
    private ComputeBuffer waveformBuffer;
    private RenderTexture waveformTexture;
    private Material waveformMaterial;
    private bool useLinearWaveform = false;

    private bool resourcesLoaded = false;

    private const int HISTOGRAM_TEXTURE_HEIGHT = 200;
    private const int VECTORSCOPE_TEXTURE_SIZE = 256;
    private const int WAVEFORM_TEXTURE_HEIGHT = 200;

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

    void LoadResources()
    {
        // 통합 컴퓨트 쉐이더 로드
        string computeShaderPath = $"{FOLDER_PATH}/UnifiedImageAnalysis.compute";
        unifiedComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(computeShaderPath);

        if (unifiedComputeShader == null)
        {
            Debug.LogError($"[ColorAnalyzerTool] Failed to load Unified Compute Shader. Check path: {computeShaderPath}");
            resourcesLoaded = false;
            return;
        }

        // 히스토그램 리소스
        LoadHistogramResources();

        // 벡터스코프 리소스
        LoadVectorscopeResources();

        // 웨이브폼 리소스
        LoadWaveformResources();

        resourcesLoaded = true;
    }

    void LoadHistogramResources()
    {
        string shaderPath = $"{FOLDER_PATH}/Histogram.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        if (shader != null)
        {
            // RGB + Luminance 4채널을 위해 256 * 4 크기로 생성
            histogramBuffer = new ComputeBuffer(256 * 4, sizeof(uint));

            histogramTexture = new RenderTexture(256, HISTOGRAM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32);
            histogramTexture.enableRandomWrite = true;
            histogramTexture.Create();
            histogramTexture.hideFlags = HideFlags.HideAndDontSave;
            histogramTexture.filterMode = FilterMode.Point;

            histogramMaterial = new Material(shader);
            histogramMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogError($"[ColorAnalyzerTool] Failed to load Histogram Shader. Check path: {shaderPath}");
        }
    }

    void LoadVectorscopeResources()
    {
        string shaderPath = $"{FOLDER_PATH}/Vectorscope.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        if (shader != null)
        {
            vectorscopeBuffer = new ComputeBuffer(vectorscopeSize * vectorscopeSize, sizeof(uint));

            vectorscopeTexture = new RenderTexture(VECTORSCOPE_TEXTURE_SIZE, VECTORSCOPE_TEXTURE_SIZE, 0, RenderTextureFormat.ARGB32);
            vectorscopeTexture.enableRandomWrite = true;
            vectorscopeTexture.Create();
            vectorscopeTexture.hideFlags = HideFlags.HideAndDontSave;
            vectorscopeTexture.filterMode = FilterMode.Point;

            vectorscopeMaterial = new Material(shader);
            vectorscopeMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogWarning($"[ColorAnalyzerTool] Vectorscope shader not found at: {shaderPath}");
        }
    }

    void LoadWaveformResources()
    {
        string shaderPath = $"{FOLDER_PATH}/Waveform.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        if (shader != null)
        {
            // 웨이브폼은 가변 크기이므로 나중에 동적으로 할당
            waveformTexture = new RenderTexture(512, WAVEFORM_TEXTURE_HEIGHT, 0, RenderTextureFormat.ARGB32);
            waveformTexture.enableRandomWrite = true;
            waveformTexture.Create();
            waveformTexture.hideFlags = HideFlags.HideAndDontSave;
            waveformTexture.filterMode = FilterMode.Point;

            waveformMaterial = new Material(shader);
            waveformMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogWarning($"[ColorAnalyzerTool] Waveform shader not found at: {shaderPath}");
        }
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;

        if (capturedTexture != null) DestroyImmediate(capturedTexture);

        // 히스토그램 리소스 정리
        if (histogramBuffer != null) { histogramBuffer.Release(); histogramBuffer = null; }
        if (histogramTexture != null) { histogramTexture.Release(); DestroyImmediate(histogramTexture); }
        if (histogramMaterial != null) DestroyImmediate(histogramMaterial);

        // 벡터스코프 리소스 정리
        if (vectorscopeBuffer != null) { vectorscopeBuffer.Release(); vectorscopeBuffer = null; }
        if (vectorscopeTexture != null) { vectorscopeTexture.Release(); DestroyImmediate(vectorscopeTexture); }
        if (vectorscopeMaterial != null) DestroyImmediate(vectorscopeMaterial);

        // 웨이브폼 리소스 정리
        if (waveformBuffer != null) { waveformBuffer.Release(); waveformBuffer = null; }
        if (waveformTexture != null) { waveformTexture.Release(); DestroyImmediate(waveformTexture); }
        if (waveformMaterial != null) DestroyImmediate(waveformMaterial);
    }

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

        // === CAPTURE SECTION ===
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

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Capture", GUILayout.Height(20)))
        {
            Capture();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // === ANALYSIS SETTINGS ===
        EditorGUILayout.BeginVertical("box");

        switch (analysisMode)
        {
            case AnalysisMode.Histogram:
                DrawHistogramSettings();
                break;
            case AnalysisMode.Vectorscope:
                DrawVectorscopeSettings();
                break;
            case AnalysisMode.Waveform:
                DrawWaveformSettings();
                break;
        }

        EditorGUILayout.EndVertical();

        if (!resourcesLoaded)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Failed to load analysis resources!\nCheck console for shader loading errors.", MessageType.Error);
            if (GUILayout.Button("Retry Loading"))
            {
                LoadResources();
            }
            return;
        }

        if (capturedTexture == null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("No image captured yet.\nPress 'Capture' to begin analysis.", MessageType.Info);
            return;
        }

        DrawPreviewSection();
        DrawAnalysisSection();
    }

    void DrawHistogramSettings()
    {
        EditorGUILayout.LabelField("Channel Visibility", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);

        // Red Channel
        EditorGUILayout.BeginHorizontal();
        showRed = EditorGUILayout.Toggle(showRed, GUILayout.Width(20));
        var redRect = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUI.DrawRect(redRect, showRed ? Color.red : new Color(0.3f, 0.3f, 0.3f));
        EditorGUILayout.LabelField("Red Channel", GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Green Channel
        EditorGUILayout.BeginHorizontal();
        showGreen = EditorGUILayout.Toggle(showGreen, GUILayout.Width(20));
        var greenRect = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUI.DrawRect(greenRect, showGreen ? Color.green : new Color(0.3f, 0.3f, 0.3f));
        EditorGUILayout.LabelField("Green Channel", GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Blue Channel
        EditorGUILayout.BeginHorizontal();
        showBlue = EditorGUILayout.Toggle(showBlue, GUILayout.Width(20));
        var blueRect = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUI.DrawRect(blueRect, showBlue ? Color.blue : new Color(0.3f, 0.3f, 0.3f));
        EditorGUILayout.LabelField("Blue Channel", GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Luminance Channel
        EditorGUILayout.BeginHorizontal();
        showLuminance = EditorGUILayout.Toggle(showLuminance, GUILayout.Width(20));
        var lumaRect = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
        EditorGUI.DrawRect(lumaRect, showLuminance ? Color.white : new Color(0.3f, 0.3f, 0.3f));
        EditorGUILayout.LabelField("Luminance", GUILayout.Width(80));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Quick toggle buttons
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All", GUILayout.Height(20)))
        {
            showRed = showGreen = showBlue = showLuminance = true;
        }
        if (GUILayout.Button("RGB", GUILayout.Height(20)))
        {
            showRed = showGreen = showBlue = true;
            showLuminance = false;
        }
        if (GUILayout.Button("Luma", GUILayout.Height(20)))
        {
            showRed = showGreen = showBlue = false;
            showLuminance = true;
        }
        if (GUILayout.Button("None", GUILayout.Height(20)))
        {
            showRed = showGreen = showBlue = showLuminance = false;
        }
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

        EditorGUILayout.BeginHorizontal();
        useLinearColorSpace = EditorGUILayout.Toggle("Linear Color Space", useLinearColorSpace);
        EditorGUILayout.EndHorizontal();
    }

    void DrawWaveformSettings()
    {
        EditorGUILayout.LabelField("Waveform Settings", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        useLinearWaveform = EditorGUILayout.Toggle("Linear Waveform", useLinearWaveform);
        EditorGUILayout.EndHorizontal();
    }

    void DrawPreviewSection()
    {
        EditorGUILayout.Space(8);

        // === PREVIEW IMAGE ===
        EditorGUILayout.LabelField("Captured Image", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        float aspectRatio = (float)capturedTexture.width / capturedTexture.height;
        float windowWidth = EditorGUIUtility.currentViewWidth - 30;
        float previewHeight = Mathf.Min(windowWidth / aspectRatio, 200f);

        Rect previewRect = GUILayoutUtility.GetRect(windowWidth, previewHeight);
        EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f));
        GUI.DrawTexture(previewRect, capturedTexture, ScaleMode.ScaleToFit);

        // Image info
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Size: {capturedTexture.width} × {capturedTexture.height}", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(true);
        GUILayout.Toggle(autoUpdate, "Auto", EditorStyles.miniButton, GUILayout.Width(50));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    void DrawAnalysisSection()
    {
        EditorGUILayout.Space(15);

        string analysisTitle = analysisMode switch
        {
            AnalysisMode.Histogram => "Color Histogram",
            AnalysisMode.Vectorscope => "Vectorscope",
            AnalysisMode.Waveform => "Waveform",
            _ => "Analysis"
        };

        EditorGUILayout.LabelField(analysisTitle, EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        float windowWidth = EditorGUIUtility.currentViewWidth - 30;
        float analysisHeight = analysisMode == AnalysisMode.Vectorscope ? windowWidth : Mathf.Clamp(windowWidth * 0.6f, 120f, 250f);
        Rect analysisRect = GUILayoutUtility.GetRect(windowWidth, analysisHeight);

        EditorGUI.DrawRect(analysisRect, new Color(0.15f, 0.15f, 0.15f));

        if (Event.current.type == EventType.Repaint)
        {
            RenderTexture displayTexture = analysisMode switch
            {
                AnalysisMode.Histogram => histogramTexture,
                AnalysisMode.Vectorscope => vectorscopeTexture,
                AnalysisMode.Waveform => waveformTexture,
                _ => null
            };

            if (displayTexture != null)
            {
                GUI.DrawTexture(analysisRect, displayTexture, ScaleMode.StretchToFill);
            }
        }

        // Analysis info
        EditorGUILayout.BeginHorizontal();
        switch (analysisMode)
        {
            case AnalysisMode.Histogram:
                int activeChannels = (showRed ? 1 : 0) + (showGreen ? 1 : 0) + (showBlue ? 1 : 0) + (showLuminance ? 1 : 0);
                EditorGUILayout.LabelField($"Active Channels: {activeChannels}/4", EditorStyles.miniLabel);
                break;
            case AnalysisMode.Vectorscope:
                EditorGUILayout.LabelField($"Buffer: {vectorscopeSize}×{vectorscopeSize}", EditorStyles.miniLabel);
                break;
            case AnalysisMode.Waveform:
                EditorGUILayout.LabelField($"Mode: {(useLinearWaveform ? "Linear" : "Gamma")}", EditorStyles.miniLabel);
                break;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"Analysis: {analysisMode}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    void Capture()
    {
        if (!resourcesLoaded) return;
        RenderTexture srgbRT = CaptureFromCamera();
        if (srgbRT != null)
        {
            switch (analysisMode)
            {
                case AnalysisMode.Histogram:
                    AnalyzeHistogram(srgbRT);
                    DrawHistogramWithShader();
                    break;
                case AnalysisMode.Vectorscope:
                    AnalyzeVectorscope(srgbRT);
                    DrawVectorscopeWithShader();
                    break;
                case AnalysisMode.Waveform:
                    AnalyzeWaveform(srgbRT);
                    DrawWaveformWithShader();
                    break;
            }
            RenderTexture.ReleaseTemporary(srgbRT);
        }
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

    void AnalyzeHistogram(RenderTexture source)
    {
        int kernelClear = unifiedComputeShader.FindKernel("KHistogramClear");
        int kernelGather = unifiedComputeShader.FindKernel("KHistogramGather");

        unifiedComputeShader.SetBuffer(kernelClear, "_HistogramBuffer", histogramBuffer);
        unifiedComputeShader.Dispatch(kernelClear, Mathf.CeilToInt((256 * 4) / 16f), 1, 1);

        unifiedComputeShader.SetTexture(kernelGather, "_Source", source);
        unifiedComputeShader.SetBuffer(kernelGather, "_HistogramBuffer", histogramBuffer);
        unifiedComputeShader.SetVector("_Params", new Vector4(source.width, source.height, 0, 0));
        unifiedComputeShader.Dispatch(kernelGather, Mathf.CeilToInt(source.width / 16f), Mathf.CeilToInt(source.height / 16f), 1);
    }

    void AnalyzeVectorscope(RenderTexture source)
    {
        // 버퍼 크기가 변경되었다면 재생성
        if (vectorscopeBuffer == null || vectorscopeBuffer.count != vectorscopeSize * vectorscopeSize)
        {
            if (vectorscopeBuffer != null) vectorscopeBuffer.Release();
            vectorscopeBuffer = new ComputeBuffer(vectorscopeSize * vectorscopeSize, sizeof(uint));
        }

        int kernelClear = unifiedComputeShader.FindKernel("KVectorscopeClear");
        int kernelGather = unifiedComputeShader.FindKernel("KVectorscopeGather");

        unifiedComputeShader.SetBuffer(kernelClear, "_VectorscopeBuffer", vectorscopeBuffer);
        unifiedComputeShader.SetVector("_Params", new Vector4(source.width, source.height, vectorscopeSize, useLinearColorSpace ? 1 : 0));
        unifiedComputeShader.Dispatch(kernelClear, Mathf.CeilToInt(vectorscopeSize / 16f), Mathf.CeilToInt(vectorscopeSize / 16f), 1);

        unifiedComputeShader.SetTexture(kernelGather, "_Source", source);
        unifiedComputeShader.SetBuffer(kernelGather, "_VectorscopeBuffer", vectorscopeBuffer);
        unifiedComputeShader.Dispatch(kernelGather, Mathf.CeilToInt(source.width / 16f), Mathf.CeilToInt(source.height / 16f), 1);
    }

    void AnalyzeWaveform(RenderTexture source)
    {
        // 웨이브폼 버퍼는 소스 크기에 따라 동적으로 할당
        if (waveformBuffer == null || waveformBuffer.count != source.width * source.height)
        {
            if (waveformBuffer != null) waveformBuffer.Release();
            waveformBuffer = new ComputeBuffer(source.width * source.height, sizeof(uint) * 4); // uint4
        }

        int kernelClear = unifiedComputeShader.FindKernel("KWaveformClear");
        int kernelGather = unifiedComputeShader.FindKernel("KWaveformGather");

        unifiedComputeShader.SetBuffer(kernelClear, "_WaveformBuffer", waveformBuffer);
        unifiedComputeShader.SetVector("_Params", new Vector4(source.width, source.height, useLinearWaveform ? 1 : 0, 0));
        unifiedComputeShader.Dispatch(kernelClear, Mathf.CeilToInt(source.width / 16f), Mathf.CeilToInt(source.height / 16f), 1);

        unifiedComputeShader.SetTexture(kernelGather, "_Source", source);
        unifiedComputeShader.SetBuffer(kernelGather, "_WaveformBuffer", waveformBuffer);
        unifiedComputeShader.Dispatch(kernelGather, Mathf.CeilToInt(source.width / 16f), Mathf.CeilToInt(source.height / 16f), 1);
    }

    void DrawHistogramWithShader()
    {
        if (histogramBuffer == null || histogramTexture == null || histogramMaterial == null) return;

        RenderTexture.active = histogramTexture;
        GL.Clear(true, true, Color.black);

        histogramMaterial.SetBuffer("_HistogramBuffer", histogramBuffer);
        histogramMaterial.SetVector("_Params", new Vector2(256, HISTOGRAM_TEXTURE_HEIGHT));
        histogramMaterial.SetFloat("_UseLogScale", useLogScale ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_AmplificationFactor", amplificationFactor);

        histogramMaterial.SetFloat("_ShowRed", showRed ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_ShowGreen", showGreen ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_ShowBlue", showBlue ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_ShowLuminance", showLuminance ? 1.0f : 0.0f);

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

    void DrawVectorscopeWithShader()
    {
        if (vectorscopeBuffer == null || vectorscopeTexture == null || vectorscopeMaterial == null) return;

        // 벡터스코프 렌더링 로직 (셰이더가 있을 경우)
        RenderTexture.active = vectorscopeTexture;
        GL.Clear(true, true, Color.black);

        // 벡터스코프 전용 셰이더 사용
        vectorscopeMaterial.SetBuffer("_VectorscopeBuffer", vectorscopeBuffer);
        vectorscopeMaterial.SetVector("_Params", new Vector2(vectorscopeSize, VECTORSCOPE_TEXTURE_SIZE));

        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);

        vectorscopeMaterial.SetPass(0);

        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);

        GL.End();
        GL.PopMatrix();

        RenderTexture.active = null;
    }

    void DrawWaveformWithShader()
    {
        if (waveformBuffer == null || waveformTexture == null || waveformMaterial == null) return;

        // 웨이브폼 렌더링 로직 (셰이더가 있을 경우)
        RenderTexture.active = waveformTexture;
        GL.Clear(true, true, Color.black);

        // 웨이브폼 전용 셰이더 사용
        waveformMaterial.SetBuffer("_WaveformBuffer", waveformBuffer);
        waveformMaterial.SetVector("_Params", new Vector4(capturedTexture.width, capturedTexture.height, WAVEFORM_TEXTURE_HEIGHT, 0));

        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);

        waveformMaterial.SetPass(0);

        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);

        GL.End();
        GL.PopMatrix();

        RenderTexture.active = null;
    }
}