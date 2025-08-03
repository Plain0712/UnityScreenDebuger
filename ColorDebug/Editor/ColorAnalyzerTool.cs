using UnityEngine;
using UnityEditor;

public class ColorAnalyzerTool : EditorWindow
{
    private const string FOLDER_PATH = "Assets/Script/UnityScreenDebuger/ColorDebug/Shader";

    private enum CaptureTarget { GameView, SceneView }

    private CaptureTarget captureTarget = CaptureTarget.GameView;
    private Texture2D capturedTexture;
    private bool autoUpdate = false;
    private float updateInterval = 0.01f; // 업데이트 변수
    private double lastUpdateTime;

    // 히스토그램 표시 옵션
    private bool useLogScale = true;
    private float amplificationFactor = 2.0f;

    // 채널 토글 옵션
    private bool showRed = true;
    private bool showGreen = true;
    private bool showBlue = true;
    private bool showLuminance = true;

    private ComputeShader histogramComputeShader;
    private ComputeBuffer histogramBuffer;
    private RenderTexture histogramTexture;
    private Material histogramMaterial;
    private bool resourcesLoaded = false;

    private const int HISTOGRAM_TEXTURE_HEIGHT = 200;

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
        string computeShaderPath = $"{FOLDER_PATH}/Histogram.compute";
        histogramComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(computeShaderPath);

        string shaderPath = $"{FOLDER_PATH}/Histogram.shader";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);

        if (histogramComputeShader != null && shader != null)
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
        autoUpdate = EditorGUILayout.Toggle("Auto Update", autoUpdate);
       
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Capture",GUILayout.Height(20)))
        {
            Capture();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // === HISTOGRAM SETTINGS ===
        EditorGUILayout.BeginVertical("box");
        //EditorGUILayout.LabelField("Histogram Settings", EditorStyles.boldLabel);
        //EditorGUILayout.Space(3);

        //EditorGUILayout.BeginHorizontal();
        //useLogScale = EditorGUILayout.Toggle("Log Scale", useLogScale, GUILayout.Width(80));
        //EditorGUILayout.LabelField("Amplify:", GUILayout.Width(50));
        //amplificationFactor = EditorGUILayout.Slider(amplificationFactor, 0.5f, 10.0f);
        //EditorGUILayout.EndHorizontal();

        //EditorGUILayout.Space(8);

        // === CHANNEL VISIBILITY ===
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

        EditorGUILayout.EndVertical();

        if (!resourcesLoaded)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Failed to load histogram resources!\nCheck console for shader loading errors.", MessageType.Error);
            if (GUILayout.Button("Retry Loading"))
            {
                LoadResources();
            }
            return;
        }

        if (capturedTexture == null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("No image captured yet.\nPress 'Capture Now' to begin analysis.", MessageType.Info);
            return;
        }

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

        EditorGUILayout.Space(15);

        // === HISTOGRAM ===
        EditorGUILayout.LabelField("Color Histogram", EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        float histogramHeight = Mathf.Clamp(windowWidth * 1.0f, 120f, 250f);
        Rect histogramRect = GUILayoutUtility.GetRect(windowWidth, histogramHeight);

        EditorGUI.DrawRect(histogramRect, new Color(0.15f, 0.15f, 0.15f));

        if (Event.current.type == EventType.Repaint && histogramTexture != null)
        {
            GUI.DrawTexture(histogramRect, histogramTexture, ScaleMode.StretchToFill);
        }

        // Histogram info
        EditorGUILayout.BeginHorizontal();
        int activeChannels = (showRed ? 1 : 0) + (showGreen ? 1 : 0) + (showBlue ? 1 : 0) + (showLuminance ? 1 : 0);
        EditorGUILayout.LabelField($"Active Channels: {activeChannels}/4", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField($"Scale: {(useLogScale ? "Log" : "Linear")}, Amp: {amplificationFactor:F1}x", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
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
        // RGB + Luminance 4채널(1024개) 클리어를 위해 스레드 그룹 수 증가
        histogramComputeShader.Dispatch(kernelClear, Mathf.CeilToInt((256 * 4) / 16f), 1, 1);

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

        // 채널 토글 상태를 셰이더에 전달
        histogramMaterial.SetFloat("_ShowRed", showRed ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_ShowGreen", showGreen ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_ShowBlue", showBlue ? 1.0f : 0.0f);
        histogramMaterial.SetFloat("_ShowLuminance", showLuminance ? 1.0f : 0.0f);

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