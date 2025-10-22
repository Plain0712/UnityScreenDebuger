using UnityEngine;
using UnityEditor;
using System;
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

    // Histogram options
    private bool useLogScale = true;
    private float amplificationFactor = 2.0f;
    private bool showRed = true, showGreen = true, showBlue = true, showLuminance = true;

    // Compute & resources
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
    private float saliencyExposure = 2.0f;
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
    private const float VECTORSCOPE_VERTICAL_MARGIN = 24f;

    // UI 색상 (디자인 가이드)
    private static readonly Color windowBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
    private static readonly Color panelBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
    private static readonly Color panelBorderColor = new Color(0.17f, 0.17f, 0.17f, 1f);
    private static readonly Color headerBackground = new Color(0.235f, 0.235f, 0.235f, 1f);
    private static readonly Color accentColor = new Color(0.36f, 0.36f, 0.36f, 1f);
    private static readonly Color subtleTextColor = new Color(0.78f, 0.78f, 0.78f, 1f);
    private static readonly Color channelDisabledColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    // GUI Styles
    private GUIStyle sectionStyle;
    private GUIStyle sectionBodyStyle;
    private GUIStyle headerStyle;
    private GUIStyle boldLabelStyle;
    private GUIStyle miniLabelStyle;
    private GUIStyle badgeLabelStyle;
    private GUIStyle captureButtonStyle;
    private Texture2D panelBackgroundTexture;
    private Texture2D headerBackgroundTexture;
    private Vector2 scrollPos;

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
        ResetStyleCache();
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
        if (saliencyPreview != null) { saliencyPreview.Release(); DestroyImmediate(saliencyPreview); }

        if (histogramMaterial != null) DestroyImmediate(histogramMaterial);
        if (vectorscopeMaterial != null) DestroyImmediate(vectorscopeMaterial);
        if (waveformMaterial != null) DestroyImmediate(waveformMaterial);
        if (saliencyMaterial != null) DestroyImmediate(saliencyMaterial);

        if (panelBackgroundTexture != null)
        {
            DestroyImmediate(panelBackgroundTexture);
            panelBackgroundTexture = null;
        }

        if (headerBackgroundTexture != null)
        {
            DestroyImmediate(headerBackgroundTexture);
            headerBackgroundTexture = null;
        }

        ResetStyleCache();
    }

    void ResetStyleCache()
    {
        sectionStyle = null;
        sectionBodyStyle = null;
        headerStyle = null;
        boldLabelStyle = null;
        miniLabelStyle = null;
        badgeLabelStyle = null;
        captureButtonStyle = null;
    }

    void InitializeStyles()
    {
        if (sectionStyle != null &&
            sectionBodyStyle != null &&
            headerStyle != null &&
            boldLabelStyle != null &&
            miniLabelStyle != null &&
            badgeLabelStyle != null &&
            captureButtonStyle != null)
        {
            return;
        }

        if (GUI.skin == null)
        {
            return;
        }

        GUIStyle helpBoxStyle;
        GUIStyle boldLabel;
        GUIStyle miniLabel;
        GUIStyle buttonStyle;

        try
        {
            helpBoxStyle = EditorStyles.helpBox;
            boldLabel = EditorStyles.boldLabel;
            miniLabel = EditorStyles.miniLabel;
            buttonStyle = GUI.skin.button;
        }
        catch (NullReferenceException)
        {
            return;
        }

        if (helpBoxStyle == null || boldLabel == null || miniLabel == null || buttonStyle == null)
        {
            return;
        }

        if (panelBackgroundTexture == null)
        {
            panelBackgroundTexture = CreatePanelTexture(panelBackground, panelBorderColor);
        }

        if (headerBackgroundTexture == null)
        {
            headerBackgroundTexture = CreateColorTexture(headerBackground);
        }

        sectionStyle = new GUIStyle(helpBoxStyle)
        {
            margin = new RectOffset(6, 6, 6, 12),
            padding = new RectOffset(0, 0, 0, 0)
        };
        sectionStyle.normal.background = panelBackgroundTexture;
        sectionStyle.border = new RectOffset(1, 1, 1, 1);

        sectionBodyStyle = new GUIStyle
        {
            padding = new RectOffset(14, 14, 12, 14),
            margin = new RectOffset(0, 0, 0, 0)
        };

        headerStyle = new GUIStyle(boldLabel)
        {
            fontSize = 12
        };
        headerStyle.normal.textColor = Color.white;

        boldLabelStyle = new GUIStyle(boldLabel);
        boldLabelStyle.normal.textColor = subtleTextColor;

        miniLabelStyle = new GUIStyle(miniLabel);
        miniLabelStyle.normal.textColor = subtleTextColor;

        badgeLabelStyle = new GUIStyle(miniLabel)
        {
            fontSize = 9,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleRight
        };
        badgeLabelStyle.normal.textColor = subtleTextColor;

        captureButtonStyle = new GUIStyle(buttonStyle)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 30f,
            margin = new RectOffset(0, 0, 6, 0),
            stretchWidth = true
        };
        captureButtonStyle.normal.textColor = Color.white;
    }

    Texture2D CreateColorTexture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    Texture2D CreatePanelTexture(Color fillColor, Color borderColor)
    {
        var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                bool isBorder = x == 0 || x == 2 || y == 0 || y == 2;
                texture.SetPixel(x, y, isBorder ? borderColor : fillColor);
            }
        }

        texture.Apply();
        return texture;
    }

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
        if (sectionStyle == null || sectionBodyStyle == null) InitializeStyles();

        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), windowBackground);
        }

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        DrawCaptureSection();
        DrawAnalysisSettingsSection();

        if (!resourcesLoaded)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Failed to load resources. Check console for errors.", MessageType.Error);
            if (GUILayout.Button("Retry Loading", GUILayout.Height(26))) LoadResources();
            EditorGUILayout.EndScrollView();
            return;
        }

        if (capturedTexture == null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("No image captured. Press 'Capture'.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawPreviewSection();
        DrawAnalysisSection();

        EditorGUILayout.EndScrollView();
    }

    void DrawCaptureSection()
    {
        DrawSection("Capture", "SOURCE", () =>
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target:", GUILayout.Width(120f));
            captureTarget = (CaptureTarget)EditorGUILayout.EnumPopup(captureTarget, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Analysis:", GUILayout.Width(120f));
            analysisMode = (AnalysisMode)EditorGUILayout.EnumPopup(analysisMode, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            autoUpdate = EditorGUILayout.Toggle("Auto Update", autoUpdate);
            EditorGUILayout.Space(8);

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = accentColor;
            if (GUILayout.Button("Capture", captureButtonStyle, GUILayout.ExpandWidth(true)))
            {
                if (resourcesLoaded) Capture();
            }
            GUI.backgroundColor = prevColor;
        });
    }

    void DrawAnalysisSettingsSection()
    {
        DrawSection("Analysis Options", "SETTINGS", () =>
        {
            if (!resourcesLoaded)
            {
                EditorGUILayout.HelpBox("Resources are not ready. Retry loading to configure analysis.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(2);

            switch (analysisMode)
            {
                case AnalysisMode.Histogram: DrawHistogramSettings(); break;
                case AnalysisMode.Vectorscope: DrawVectorscopeSettings(); break;
                case AnalysisMode.Waveform: DrawWaveformSettings(); break;
                case AnalysisMode.Saliency: DrawSaliencySettings(); break;
                case AnalysisMode.ColorPalette: DrawColorPaletteSettings(); break;
            }
        });
    }

    void DrawSectionHeader(string title, string badge)
    {
        Rect headerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(40f), GUILayout.ExpandWidth(true));

        if (Event.current.type == EventType.Repaint)
        {
            if (headerBackgroundTexture != null)
            {
                GUI.DrawTexture(headerRect, headerBackgroundTexture, ScaleMode.StretchToFill);
            }
            else
            {
                EditorGUI.DrawRect(headerRect, headerBackground);
            }
        }

        float contentLeft = headerRect.x + 14f;
        float contentRight = headerRect.xMax - 14f;

        const float accentWidth = 2f;
        const float accentHeight = 18f;
        Rect accentRect = new Rect(contentLeft,
                                   headerRect.y + (headerRect.height - accentHeight) * 0.5f,
                                   accentWidth,
                                   accentHeight);
        EditorGUI.DrawRect(accentRect, accentColor);

        float titleLeft = accentRect.xMax + 8f;
        float titleRight = contentRight;

        GUIStyle badgeStyle = badgeLabelStyle ?? miniLabelStyle ?? GUI.skin.label;
        if (!string.IsNullOrEmpty(badge))
        {
            string badgeText = badge.ToUpperInvariant();
            Vector2 badgeSize = badgeStyle.CalcSize(new GUIContent(badgeText));
            Rect badgeRect = new Rect(contentRight - badgeSize.x,
                                      headerRect.y + (headerRect.height - badgeSize.y) * 0.5f,
                                      badgeSize.x,
                                      badgeSize.y);
            EditorGUI.LabelField(badgeRect, badgeText, badgeStyle);
            titleRight = badgeRect.x - 8f;
        }

        GUIStyle titleStyle = headerStyle ?? GUI.skin.label;
        Rect titleRect = new Rect(titleLeft,
                                  headerRect.y,
                                  Mathf.Max(0f, titleRight - titleLeft),
                                  headerRect.height);
        EditorGUI.LabelField(titleRect, title, titleStyle);
    }

    void DrawSectionAccentBar()
    {
        Rect accentRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(2f), GUILayout.ExpandWidth(true));
        float inset = 14f;
        accentRect = new Rect(accentRect.x + inset,
                              accentRect.y,
                              Mathf.Max(0f, accentRect.width - inset * 2f),
                              2f);

        if (accentRect.width > 0f && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(accentRect, accentColor);
        }

        GUILayout.Space(6);
    }

    void DrawSection(string title, string badge, Action body)
    {
        if (sectionStyle == null ||
            sectionBodyStyle == null ||
            headerStyle == null ||
            boldLabelStyle == null ||
            miniLabelStyle == null ||
            badgeLabelStyle == null ||
            captureButtonStyle == null)
        {
            InitializeStyles();
        }

        if (sectionStyle == null || sectionBodyStyle == null || headerStyle == null)
        {
            var fallbackBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(14, 14, 12, 14),
                margin = new RectOffset(6, 6, 6, 12)
            };
            var fallbackLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            EditorGUILayout.BeginVertical(fallbackBox);
            EditorGUILayout.LabelField(title, fallbackLabel);
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Loading UI styles...", GUI.skin.label);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginVertical(sectionStyle);
        DrawSectionHeader(title, badge);
        DrawSectionAccentBar();

        EditorGUILayout.BeginVertical(sectionBodyStyle);
        body?.Invoke();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndVertical();
    }

    void DrawHistogramSettings()
    {
        EditorGUILayout.LabelField("Channel Visibility", boldLabelStyle);
        EditorGUILayout.Space(3);

        DrawChannelToggle(ref showRed, "Red Channel", Color.red);
        DrawChannelToggle(ref showGreen, "Green Channel", Color.green);
        DrawChannelToggle(ref showBlue, "Blue Channel", Color.blue);
        DrawChannelToggle(ref showLuminance, "Luminance", Color.white);

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All", GUILayout.Height(22)))
        {
            showRed = showGreen = showBlue = showLuminance = true;
            if (histogramBuffer != null) DrawHistogramWithShader();
        }
        if (GUILayout.Button("RGB", GUILayout.Height(22)))
        {
            showRed = showGreen = showBlue = true;
            showLuminance = false;
            if (histogramBuffer != null) DrawHistogramWithShader();
        }
        if (GUILayout.Button("Luma", GUILayout.Height(22)))
        {
            showRed = showGreen = showBlue = false;
            showLuminance = true;
            if (histogramBuffer != null) DrawHistogramWithShader();
        }
        if (GUILayout.Button("None", GUILayout.Height(22)))
        {
            showRed = showGreen = showBlue = showLuminance = false;
            if (histogramBuffer != null) DrawHistogramWithShader();
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawChannelToggle(ref bool toggle, string label, Color col)
    {
        EditorGUILayout.BeginHorizontal();

        var newToggle = EditorGUILayout.Toggle(toggle, GUILayout.Width(20));
        if (newToggle != toggle)
        {
            toggle = newToggle;
            if (analysisMode == AnalysisMode.Histogram && histogramBuffer != null)
            {
                DrawHistogramWithShader();
            }
            else if (analysisMode == AnalysisMode.Waveform && waveformBuffer != null)
            {
                DrawWaveformWithShader();
            }
        }

        var rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
        EditorGUI.DrawRect(rect, toggle ? col : channelDisabledColor);

        GUILayout.Space(4);
        EditorGUILayout.LabelField(label, GUILayout.Width(100));
        GUILayout.FlexibleSpace();

        EditorGUILayout.EndHorizontal();
    }

    void DrawVectorscopeSettings()
    {
        EditorGUILayout.LabelField("Vectorscope Settings", boldLabelStyle);
        EditorGUILayout.Space(3);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Buffer Size:", GUILayout.Width(120));
        vectorscopeSize = EditorGUILayout.IntSlider(vectorscopeSize, 128, 512);
        EditorGUILayout.EndHorizontal();
    }

    void DrawWaveformSettings()
    {
        EditorGUILayout.LabelField("Channel Visibility", boldLabelStyle);
        EditorGUILayout.Space(3);

        DrawChannelToggle(ref showRed_Waveform, "Red Channel", Color.red);
        DrawChannelToggle(ref showGreen_Waveform, "Green Channel", Color.green);
        DrawChannelToggle(ref showBlue_Waveform, "Blue Channel", Color.blue);

        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All", GUILayout.Height(22)))
        {
            showRed_Waveform = showGreen_Waveform = showBlue_Waveform = true;
            if (waveformBuffer != null) DrawWaveformWithShader();
        }
        if (GUILayout.Button("RGB", GUILayout.Height(22)))
        {
            showRed_Waveform = showGreen_Waveform = showBlue_Waveform = true;
            if (waveformBuffer != null) DrawWaveformWithShader();
        }
        if (GUILayout.Button("None", GUILayout.Height(22)))
        {
            showRed_Waveform = showGreen_Waveform = showBlue_Waveform = false;
            if (waveformBuffer != null) DrawWaveformWithShader();
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawSaliencySettings()
    {
        EditorGUILayout.LabelField("Saliency Options", boldLabelStyle);
        EditorGUILayout.Space(3);

        var newNormalize = EditorGUILayout.Toggle("Auto-Normalize", saliencyNormalize);
        if (newNormalize != saliencyNormalize)
        {
            saliencyNormalize = newNormalize;
            if (saliencyTexture != null)
            {
                DrawSaliencyWithShader();
            }
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Red = High saliency, Blue = Low saliency", miniLabelStyle);
    }

    void DrawColorPaletteSettings()
    {
        EditorGUILayout.LabelField("Color Palette", boldLabelStyle);
        EditorGUILayout.Space(3);

        showColorValues = EditorGUILayout.Toggle("Show RGB Values", showColorValues);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Extracts 5 dominant colors using LAB K-Means clustering.", miniLabelStyle);
    }

    void DrawPreviewSection()
    {
        DrawSection("Captured Image", "PREVIEW", () =>
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(220f), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            GUI.DrawTexture(rect, capturedTexture, ScaleMode.ScaleToFit);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Size: {capturedTexture.width}×{capturedTexture.height}", miniLabelStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Auto Update: {(autoUpdate ? "On" : "Off")}", miniLabelStyle);
            EditorGUILayout.EndHorizontal();
        });
    }

    void DrawAnalysisSection()
    {
        DrawSection("Analysis Output", analysisMode.ToString().ToUpperInvariant(), () =>
        {
            if (analysisMode == AnalysisMode.ColorPalette)
            {
                DrawColorPalette();
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(220f), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, Color.black);

            if (Event.current.type == EventType.Repaint)
            {
                if (analysisMode == AnalysisMode.Vectorscope)
                {
                    float maxScopeHeight = Mathf.Max(rect.height - VECTORSCOPE_VERTICAL_MARGIN * 4f, 0f);
                    float scopeSize = Mathf.Min(rect.width, maxScopeHeight);
                    Rect scopeRect = new Rect(rect.x + (rect.width - scopeSize) * 0.5f,
                                              rect.y + (rect.height - scopeSize) * 0.5f,
                                              scopeSize,
                                              scopeSize);

                    if (vectorscopeGuide != null)
                        GUI.DrawTexture(scopeRect, vectorscopeGuide, ScaleMode.StretchToFill);

                    if (vectorscopeTexture != null)
                        GUI.DrawTexture(scopeRect, vectorscopeTexture, ScaleMode.StretchToFill);
                }
                else
                {
                    RenderTexture tex = analysisMode switch
                    {
                        AnalysisMode.Histogram => histogramTexture,
                        AnalysisMode.Waveform => waveformTexture,
                        AnalysisMode.Saliency => saliencyPreview,
                        _ => null
                    };

                    if (tex != null)
                    {
                        GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
                    }
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();

            switch (analysisMode)
            {
                case AnalysisMode.Histogram:
                    int ch = (showRed ? 1 : 0) + (showGreen ? 1 : 0) + (showBlue ? 1 : 0) + (showLuminance ? 1 : 0);
                    EditorGUILayout.LabelField($"Channels: {ch}/4", miniLabelStyle);
                    break;
                case AnalysisMode.Vectorscope:
                    EditorGUILayout.LabelField($"Buffer: {vectorscopeSize}×{vectorscopeSize}", miniLabelStyle);
                    break;
                case AnalysisMode.Waveform:
                    int ch_w = (showRed_Waveform ? 1 : 0) + (showGreen_Waveform ? 1 : 0) + (showBlue_Waveform ? 1 : 0);
                    EditorGUILayout.LabelField($"Channels: {ch_w}/3", miniLabelStyle);
                    break;
                case AnalysisMode.Saliency:
                    EditorGUILayout.LabelField(saliencyNormalize ? "Normalized" : "Raw values", miniLabelStyle);
                    break;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Mode: {analysisMode}", miniLabelStyle);
            EditorGUILayout.EndHorizontal();
        });
    }

    void DrawColorPalette()
    {
        if (dominantColors == null || dominantColors.Length == 0)
        {
            EditorGUILayout.LabelField("No colors extracted", miniLabelStyle);
            return;
        }

        float totalHeight = 80f;

        Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(totalHeight), GUILayout.ExpandWidth(true));

        if (Event.current.type == EventType.Repaint)
        {
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

                EditorGUI.DrawRect(colorRect, color);

                if (i > 0)
                {
                    EditorGUI.DrawRect(new Rect(colorRect.x, colorRect.y, 1, colorRect.height), panelBorderColor);
                }

                if (showColorValues)
                {
                    string hexColor = ColorUtility.ToHtmlStringRGB(color);
                    float luminance = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
                    Color textColor = luminance > 0.5f ? Color.black : Color.white;

                    GUIStyle textStyle = new GUIStyle(EditorStyles.miniLabel);
                    textStyle.normal.textColor = textColor;
                    textStyle.alignment = TextAnchor.MiddleCenter;
                    textStyle.fontStyle = FontStyle.Bold;
                    textStyle.fontSize = 9;

                    Rect textRect = new Rect(colorRect.x, colorRect.y + colorRect.height * 0.4f, colorRect.width, 20);
                    GUI.Label(textRect, $"#{hexColor}", textStyle);
                }
            }
        }

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
        EditorGUILayout.LabelField("Click color to copy hex value", miniLabelStyle);
    }

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
        GL.Clear(true, true, Color.clear);

        vectorscopeMaterial.SetBuffer("_VectorscopeBuffer", vectorscopeBuffer);
        vectorscopeMaterial.SetVector("_Params", new Vector2(vectorscopeSize, VECTORSCOPE_TEXTURE_SIZE));

        DrawFullScreenQuad(vectorscopeMaterial);
    }

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

    void AnalyzeSaliency(RenderTexture src)
    {
        int kSaliency = unifiedComputeShader.FindKernel("KSaliencyMap");
        int kReduce = unifiedComputeShader.FindKernel("KSaliencyReduce");

        if (kSaliency < 0 || kReduce < 0)
        {
            Debug.LogError("[ColorAnalyzerTool] Saliency kernels not found in compute shader");
            return;
        }

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

        unifiedComputeShader.SetTexture(kSaliency, "_Source", src);
        unifiedComputeShader.SetTexture(kSaliency, "_SaliencyMap", saliencyTexture);
        unifiedComputeShader.SetVector("_Params", new Vector4(src.width, src.height, 0, 0));

        int threadGroupsX = Mathf.CeilToInt(src.width / 16f);
        int threadGroupsY = Mathf.CeilToInt(src.height / 16f);

        unifiedComputeShader.Dispatch(kSaliency, threadGroupsX, threadGroupsY, 1);

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

    void AnalyzeColorPaletteWithCompute(RenderTexture sourceTexture)
    {
        if (sourceTexture == null) return;

        int downsampledWidth = sourceTexture.width / 15;
        int downsampledHeight = sourceTexture.height / 15;

        var downsampledTexture = RenderTexture.GetTemporary(downsampledWidth, downsampledHeight, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(sourceTexture, downsampledTexture);

        Texture2D readableTexture = new Texture2D(downsampledWidth, downsampledHeight, TextureFormat.ARGB32, false);
        RenderTexture.active = downsampledTexture;
        readableTexture.ReadPixels(new Rect(0, 0, downsampledWidth, downsampledHeight), 0, 0);
        readableTexture.Apply();
        RenderTexture.active = null;

        Color32[] pixels = readableTexture.GetPixels32();
        var labColors = new System.Collections.Generic.List<Vector3>();
        var originalColors = new System.Collections.Generic.List<Color>();

        int sampleStep = Mathf.Max(1, pixels.Length / 1000);

        for (int i = 0; i < pixels.Length; i += sampleStep)
        {
            Color rgbColor = new Color(pixels[i].r / 255f, pixels[i].g / 255f, pixels[i].b / 255f, 1f);
            Vector3 labColor = RGBToLAB(rgbColor);

            labColors.Add(labColor);
            originalColors.Add(rgbColor);
        }

        var clusterCenters = PerformKMeansInLAB(labColors.ToArray(), originalColors.ToArray(), 5);

        dominantColors = clusterCenters.Select(center => LABToRGB(center)).ToArray();

        DestroyImmediate(readableTexture);
        RenderTexture.ReleaseTemporary(downsampledTexture);
    }

    Vector3 RGBToLAB(Color rgb)
    {
        float r = rgb.r > 0.04045f ? Mathf.Pow((rgb.r + 0.055f) / 1.055f, 2.4f) : rgb.r / 12.92f;
        float g = rgb.g > 0.04045f ? Mathf.Pow((rgb.g + 0.055f) / 1.055f, 2.4f) : rgb.g / 12.92f;
        float b = rgb.b > 0.04045f ? Mathf.Pow((rgb.b + 0.055f) / 1.055f, 2.4f) : rgb.b / 12.92f;

        float x = (r * 0.4124f + g * 0.3576f + b * 0.1805f) / 0.95047f;
        float y = (r * 0.2126f + g * 0.7152f + b * 0.0722f) / 1.00000f;
        float z = (r * 0.0193f + g * 0.1192f + b * 0.9505f) / 1.08883f;

        x = x > 0.008856f ? Mathf.Pow(x, 1f / 3f) : (7.787f * x + 16f / 116f);
        y = y > 0.008856f ? Mathf.Pow(y, 1f / 3f) : (7.787f * y + 16f / 116f);
        z = z > 0.008856f ? Mathf.Pow(z, 1f / 3f) : (7.787f * z + 16f / 116f);

        float L = 116f * y - 16f;
        float A = 500f * (x - y);
        float B = 200f * (y - z);

        return new Vector3(L, A, B);
    }

    Color LABToRGB(Vector3 lab)
    {
        float y = (lab.x + 16f) / 116f;
        float x = lab.y / 500f + y;
        float z = y - lab.z / 200f;

        x = x > 0.206893f ? x * x * x : (x - 16f / 116f) / 7.787f;
        y = y > 0.206893f ? y * y * y : (y - 16f / 116f) / 7.787f;
        z = z > 0.206893f ? z * z * z : (z - 16f / 116f) / 7.787f;

        x *= 0.95047f;
        y *= 1.00000f;
        z *= 1.08883f;

        float r = x * 3.2406f + y * -1.5372f + z * -0.4986f;
        float g = x * -0.9689f + y * 1.8758f + z * 0.0415f;
        float b = x * 0.0557f + y * -0.2040f + z * 1.0570f;

        r = r > 0.0031308f ? 1.055f * Mathf.Pow(r, 1f / 2.4f) - 0.055f : 12.92f * r;
        g = g > 0.0031308f ? 1.055f * Mathf.Pow(g, 1f / 2.4f) - 0.055f : 12.92f * g;
        b = b > 0.0031308f ? 1.055f * Mathf.Pow(b, 1f / 2.4f) - 0.055f : 12.92f * b;

        return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
    }

    Vector3[] PerformKMeansInLAB(Vector3[] labColors, Color[] originalColors, int k)
    {
        if (labColors.Length == 0) return new Vector3[0];
        if (labColors.Length <= k) return labColors.Take(k).ToArray();

        var random = new System.Random(42);
        var centroids = new Vector3[k];

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
                    float distance = Vector3.Distance(labColors[j], centroids[c]);
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

        for (int iter = 0; iter < 20; iter++)
        {
            var clusters = new System.Collections.Generic.List<Vector3>[k];
            for (int i = 0; i < k; i++) clusters[i] = new System.Collections.Generic.List<Vector3>();

            foreach (var color in labColors)
            {
                float minDistance = float.MaxValue;
                int bestCluster = 0;

                for (int i = 0; i < k; i++)
                {
                    float distance = Vector3.Distance(color, centroids[i]);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestCluster = i;
                    }
                }

                clusters[bestCluster].Add(color);
            }

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

                if (Vector3.Distance(centroids[i], newCentroid) > 1.0f)
                {
                    converged = false;
                }

                centroids[i] = newCentroid;
            }

            if (converged) break;
        }

        return centroids;
    }

    static void DrawFullScreenQuad(Material mat)
    {
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        mat.SetPass(0);
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = null;
    }
}
