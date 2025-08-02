
using UnityEngine;
using UnityEditor;

public class ColorAnalyzerTool : EditorWindow
{
    private const string FOLDER_PATH = "Assets/Script/ColorDebug/Editor/Shader";

    private enum CaptureTarget { GameView, SceneView }

    private CaptureTarget captureTarget = CaptureTarget.GameView;
    private Texture2D capturedTexture;
    private bool autoUpdate = false;
    private float updateInterval = 0.5f;
    private double lastUpdateTime;

    private ComputeShader histogramComputeShader;
    private ComputeBuffer histogramBuffer;
    private RenderTexture histogramTexture; // Changed to RenderTexture
    private Material histogramMaterial;
    private bool resourcesLoaded = false;

    private Rect displayRect;

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
            
            // Changed to RenderTexture
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
            if(histogramComputeShader == null)
                Debug.LogError($"[ColorAnalyzerTool] Failed to load Compute Shader. Check path: {computeShaderPath}");
            if(shader == null)
                Debug.LogError($"[ColorAnalyzerTool] Failed to load Shader. Check path: {shaderPath}");

            resourcesLoaded = false;
        }
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        if (capturedTexture != null) DestroyImmediate(capturedTexture);
        if (histogramBuffer != null) { histogramBuffer.Release(); histogramBuffer = null; }
        if (histogramTexture != null) { histogramTexture.Release(); DestroyImmediate(histogramTexture); } // Release and Destroy
        if (histogramMaterial != null) DestroyImmediate(histogramMaterial);
    }

    void OnEditorUpdate()
    {
        if (autoUpdate && EditorApplication.timeSinceStartup - lastUpdateTime > updateInterval)
        {
            Capture();
            Repaint();
        }
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.Layout)
        {
            CalculateLayout();
        }

        DrawHeader();
        DrawControls();

        if (!resourcesLoaded)
        {
            EditorGUILayout.HelpBox("Failed to load resources! Check console for errors.", MessageType.Error);
            return;
        }

        DrawPreview();
        DrawAnalysis();
    }

    void CalculateLayout()
    {
        if (capturedTexture != null)
        {
            float maxWidth = position.width - 20;
            float maxHeight = 200;
            float aspect = (float)capturedTexture.width / capturedTexture.height;
            float displayWidth, displayHeight;

            if (maxWidth / aspect <= maxHeight)
            {
                displayWidth = maxWidth;
                displayHeight = displayWidth / aspect;
            }
            else
            {
                displayHeight = maxHeight;
                displayWidth = displayHeight * aspect;
            }
            float xOffset = (position.width - displayWidth) / 2;
            displayRect = new Rect(xOffset, 0, displayWidth, displayHeight);
        }
        else
        {
            displayRect = Rect.zero;
        }
    }

    void DrawHeader()
    {
        EditorGUILayout.LabelField("Color Analysis Tool (sRGB Only)", EditorStyles.boldLabel);
        EditorGUILayout.Space();
    }

    void DrawControls()
    {
        EditorGUILayout.BeginVertical("box");
        captureTarget = (CaptureTarget)EditorGUILayout.EnumPopup("Capture Target:", captureTarget);
        autoUpdate = EditorGUILayout.Toggle("Auto Update", autoUpdate);
        if (GUILayout.Button("Capture", GUILayout.Height(30))) Capture();
        EditorGUILayout.EndVertical();
    }

    void DrawPreview()
    {
        if (capturedTexture == null)
        {
            EditorGUILayout.HelpBox("Press 'Capture' to begin.", MessageType.Info);
            return;
        }
        Rect rect = GUILayoutUtility.GetRect(displayRect.width, displayRect.height, GUILayout.Width(displayRect.width), GUILayout.Height(displayRect.height));
        GUI.DrawTexture(rect, capturedTexture, ScaleMode.ScaleToFit);
    }

    void DrawAnalysis()
    {
        if (capturedTexture == null) return;
        DrawHistogram();
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

    void DrawHistogram()
    {
        EditorGUILayout.LabelField("Histogram (sRGB Space)", EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(displayRect.width, displayRect.height, GUILayout.Width(displayRect.width), GUILayout.Height(displayRect.height));
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(rect, histogramTexture);
        }
    }

    void DrawHistogramWithShader()
    {
        if (histogramBuffer == null || histogramTexture == null || histogramMaterial == null) return;

        histogramMaterial.SetBuffer("_HistogramBuffer", histogramBuffer);
        histogramMaterial.SetVector("_Params", new Vector2(256, HISTOGRAM_TEXTURE_HEIGHT));
        Graphics.Blit(null, histogramTexture, histogramMaterial);
    }
}
