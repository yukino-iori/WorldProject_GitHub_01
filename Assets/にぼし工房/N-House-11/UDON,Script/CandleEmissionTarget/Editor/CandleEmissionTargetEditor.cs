// CandleEmissionTargetEditor.cs
// CandleEmissionTarget 用のカスタムInspector。
// Emission Min/Max の間のグラデーションをプレビュー表示し、
// 基本設定と詳細設定を分けて見やすくする。
//
// 導入方法: このファイルを "Assets/Editor" フォルダに入れてください。

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CandleEmissionTarget))]
public class CandleEmissionTargetEditor : Editor
{
    bool showAdvanced = false;

    SerializedProperty flameMaterial;
    SerializedProperty targetRenderers;
    SerializedProperty emissionMin;
    SerializedProperty emissionMax;
    SerializedProperty flameOriginOverride;
    SerializedProperty emissionPropertyName;
    SerializedProperty defaultFlickerSpeed;
    SerializedProperty defaultFlickerScale;

    void OnEnable()
    {
        flameMaterial = serializedObject.FindProperty("flameMaterial");
        targetRenderers = serializedObject.FindProperty("targetRenderers");
        emissionMin = serializedObject.FindProperty("emissionMin");
        emissionMax = serializedObject.FindProperty("emissionMax");
        flameOriginOverride = serializedObject.FindProperty("flameOriginOverride");
        emissionPropertyName = serializedObject.FindProperty("emissionPropertyName");
        defaultFlickerSpeed = serializedObject.FindProperty("defaultFlickerSpeed");
        defaultFlickerScale = serializedObject.FindProperty("defaultFlickerScale");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("ろうそくのちらつき設定", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "炎のマテリアル・対象Renderer・Emissionの範囲を入れるだけで動きます。",
            MessageType.None);

        EditorGUILayout.Space(6);

        EditorGUILayout.PropertyField(flameMaterial, new GUIContent("Flame Material (炎のマテリアル)"));
        EditorGUILayout.PropertyField(targetRenderers, new GUIContent("Target Renderers (対象Renderer)"), true);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Emissionの範囲", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(emissionMin, new GUIContent("Emission Min (下限)"));
        EditorGUILayout.PropertyField(emissionMax, new GUIContent("Emission Max (上限)"));

        DrawGradientPreview(emissionMin.colorValue, emissionMax.colorValue);

        EditorGUILayout.Space(10);

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "詳細設定", true, EditorStyles.foldoutHeader);
        if (showAdvanced)
        {
            EditorGUI.indentLevel++;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "Flame Originを空欄にすると、このGameObject自身の位置が\n" +
                    "ちらつきパターンの個体差(他のろうそくとズレる要素)に使われます。\n" +
                    "同じGameObjectに複数のろうそくをまとめる場合は、\n" +
                    "それぞれ別のTransformを指定してください。",
                    MessageType.Info);
                EditorGUILayout.PropertyField(flameOriginOverride, new GUIContent("Flame Origin (任意)"));
                EditorGUILayout.PropertyField(emissionPropertyName, new GUIContent("Emission Property Name"));
                EditorGUILayout.PropertyField(defaultFlickerSpeed, new GUIContent("Default Flicker Speed"));
                EditorGUILayout.PropertyField(defaultFlickerScale, new GUIContent("Default Flicker Scale"));
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawGradientPreview(Color min, Color max)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("プレビュー (Min → Max)", EditorStyles.miniBoldLabel);

        Rect rect = GUILayoutUtility.GetRect(10, 28, GUILayout.ExpandWidth(true));
        const int steps = 40;
        float stepWidth = rect.width / steps;

        for (int i = 0; i < steps; i++)
        {
            float t = (steps <= 1) ? 0f : (float)i / (steps - 1);
            Color c = Color.Lerp(min, max, t);
            c.a = 1f; // プレビューではアルファは無視して見やすくする
            Rect slice = new Rect(rect.x + i * stepWidth, rect.y, stepWidth + 1f, rect.height);
            EditorGUI.DrawRect(slice, c);
        }

        Rect border = new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2);
        Handles.color = new Color(0f, 0f, 0f, 0.4f);
        Handles.DrawSolidRectangleWithOutline(rect, Color.clear, Handles.color);
    }
}
#endif
