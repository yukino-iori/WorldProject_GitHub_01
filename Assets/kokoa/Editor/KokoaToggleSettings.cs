using UnityEngine;
using UnityEditor;

public class KokoaToggleSettings : EditorWindow
{
    private static KokoaToggleSettings window;
    
    // 設定値
    private static bool enabledToggleButtons = true;
    private static bool showEditorOnlyToggle = true;
    private static bool showActiveToggle = true;
    private static bool requireShiftForBulkOperation = true;
    private static int buttonOffsetFromRight = 80;
    private static float inactiveButtonAlpha = 0.7f;
    
    // EditorPrefsのキー
    private const string PREF_ENABLED = "KokoaToggle_Enabled";
    private const string PREF_SHOW_EDITOR_ONLY = "KokoaToggle_ShowEditorOnly";
    private const string PREF_SHOW_ACTIVE = "KokoaToggle_ShowActive";
    private const string PREF_REQUIRE_SHIFT = "KokoaToggle_RequireShift";
    private const string PREF_BUTTON_OFFSET = "KokoaToggle_ButtonOffset";
    private const string PREF_INACTIVE_ALPHA = "KokoaToggle_InactiveAlpha";

    [MenuItem("Tools/kokoaToggle")]
    public static void ShowWindow()
    {
        window = GetWindow<KokoaToggleSettings>("kokoa Toggle Settings");
        window.minSize = new Vector2(400, 300);
        window.Show();
    }

    private void OnEnable()
    {
        LoadPreferences();
    }

    private void OnGUI()
    {
        GUILayout.Label("kokoa Toggle Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 基本設定
        GUILayout.Label("基本設定", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        
        enabledToggleButtons = EditorGUILayout.Toggle("トグルボタンを有効にする", enabledToggleButtons);
        
        EditorGUI.BeginDisabledGroup(!enabledToggleButtons);
        showEditorOnlyToggle = EditorGUILayout.Toggle("EditorOnlyボタンを表示", showEditorOnlyToggle);
        showActiveToggle = EditorGUILayout.Toggle("Activeボタンを表示", showActiveToggle);
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.Space();

        // 操作設定
        GUILayout.Label("操作設定", EditorStyles.boldLabel);
        requireShiftForBulkOperation = EditorGUILayout.Toggle("Shiftキーで一括操作", requireShiftForBulkOperation);
        EditorGUILayout.HelpBox("チェックを入れると、複数選択時にShiftキーを押しながらクリックした場合のみ一括操作が実行されます。", MessageType.Info);
        EditorGUILayout.Space();

        // 表示設定
        GUILayout.Label("表示設定", EditorStyles.boldLabel);
        buttonOffsetFromRight = EditorGUILayout.IntSlider("ボタンの右端からの距離", buttonOffsetFromRight, 0, 200);
        inactiveButtonAlpha = EditorGUILayout.Slider("非アクティブボタンの透明度", inactiveButtonAlpha, 0.1f, 1.0f);
        
        EditorGUILayout.Space();

        // プレビュー
        GUILayout.Label("プレビュー", EditorStyles.boldLabel);
        string previewText = $"ボタン位置: 右端から {buttonOffsetFromRight}px";
        if (showEditorOnlyToggle && showActiveToggle)
        {
            previewText += "\n表示順: Active EditorOnly";
        }
        else if (showEditorOnlyToggle)
        {
            previewText += "\n表示: EditorOnlyボタンのみ";
        }
        else if (showActiveToggle)
        {
            previewText += "\n表示: Activeボタンのみ";
        }
        else
        {
            previewText += "\n表示: ボタンなし";
        }
        EditorGUILayout.HelpBox(previewText, MessageType.None);

        if (EditorGUI.EndChangeCheck())
        {
            SavePreferences();
        }

        EditorGUILayout.Space();
        
        // リセットボタン
        if (GUILayout.Button("設定をリセット"))
        {
            if (EditorUtility.DisplayDialog("設定のリセット", "すべての設定をデフォルトに戻しますか？", "はい", "いいえ"))
            {
                ResetToDefaults();
            }
        }
    }

    private void LoadPreferences()
    {
        enabledToggleButtons = EditorPrefs.GetBool(PREF_ENABLED, true);
        showEditorOnlyToggle = EditorPrefs.GetBool(PREF_SHOW_EDITOR_ONLY, true);
        showActiveToggle = EditorPrefs.GetBool(PREF_SHOW_ACTIVE, true);
        requireShiftForBulkOperation = EditorPrefs.GetBool(PREF_REQUIRE_SHIFT, true);
        buttonOffsetFromRight = EditorPrefs.GetInt(PREF_BUTTON_OFFSET, 80);
        inactiveButtonAlpha = EditorPrefs.GetFloat(PREF_INACTIVE_ALPHA, 0.7f);
    }

    private void SavePreferences()
    {
        EditorPrefs.SetBool(PREF_ENABLED, enabledToggleButtons);
        EditorPrefs.SetBool(PREF_SHOW_EDITOR_ONLY, showEditorOnlyToggle);
        EditorPrefs.SetBool(PREF_SHOW_ACTIVE, showActiveToggle);
        EditorPrefs.SetBool(PREF_REQUIRE_SHIFT, requireShiftForBulkOperation);
        EditorPrefs.SetInt(PREF_BUTTON_OFFSET, buttonOffsetFromRight);
        EditorPrefs.SetFloat(PREF_INACTIVE_ALPHA, inactiveButtonAlpha);
    }

    private void ResetToDefaults()
    {
        enabledToggleButtons = true;
        showEditorOnlyToggle = true;
        showActiveToggle = true;
        requireShiftForBulkOperation = true;
        buttonOffsetFromRight = 80;
        inactiveButtonAlpha = 0.7f;
        SavePreferences();
        Repaint();
    }

    // 設定値を取得するための静的メソッド
    public static bool IsEnabled => EditorPrefs.GetBool(PREF_ENABLED, true);
    public static bool ShowEditorOnlyToggle => EditorPrefs.GetBool(PREF_SHOW_EDITOR_ONLY, true);
    public static bool ShowActiveToggle => EditorPrefs.GetBool(PREF_SHOW_ACTIVE, true);
    public static bool RequireShiftForBulkOperation => EditorPrefs.GetBool(PREF_REQUIRE_SHIFT, true);
    public static int ButtonOffsetFromRight => EditorPrefs.GetInt(PREF_BUTTON_OFFSET, 80);
    public static float InactiveButtonAlpha => EditorPrefs.GetFloat(PREF_INACTIVE_ALPHA, 0.7f);
}