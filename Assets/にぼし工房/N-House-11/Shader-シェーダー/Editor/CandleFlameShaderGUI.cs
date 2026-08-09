// CandleFlameShaderGUI.cs
// "VRC/CandleFlame" シェーダー専用のカスタムマテリアルInspector。
//
// できること:
// 1. Inspectorの項目を「FlameShape / Sway Motion / Flicker / Color / Render Settings」の
//    5つの大項目にベベル調の枠で整理
// 2. 各スライダーは0〜1の正規化ノブになっており、0.5で旧バージョンの見た目を再現する
//    (シェーダー側のRemap01で下限・基準値・上限をなめらかに補間している)
// 3. 「Color Ramp」をオンにすると、Blenderのカラーランプのように
//    Inspector上のGradientフィールドを直接編集でき、
//    変更するたびに自動でグラデーションテクスチャを再生成して反映する
//
// 使い方:
// 1. このファイルと CandleFlame.shader を両方とも "Assets/Editor" フォルダに入れる
//    (CandleFlameShaderGUI.cs は UNITY_EDITOR 内のみコンパイルされるので Editor フォルダ推奨)
// 2. CandleFlame マテリアルを選択するとこのInspectorが自動的に使われる
//    (シェーダー側に CustomEditor "CandleFlameShaderGUI" を指定済み)

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class CandleFlameShaderGUI : ShaderGUI
{
    // 折りたたみの開閉状態
    bool showShape = true;
    bool showSway = false;
    bool showFlicker = false;
    bool showColor = true;
    bool showRenderSettings = false;

    MaterialEditor editor;
    MaterialProperty[] props;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        editor = materialEditor;
        props = properties;
        Material mat = materialEditor.target as Material;

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "各スライダーは0〜1で、0.5が基準(旧バージョンの見た目)です。\n" +
            "0側/1側に振ると基準値から大きく変化します。",
            MessageType.None);
        EditorGUILayout.Space(2);

        DrawSection("FlameShape", ref showShape, () =>
        {
            Prop("_UseTexture", "テクスチャマスクを使う");
            Prop("_MainTex", "マスクテクスチャ");
            Prop("_FlameWidth", "太さ");
            Prop("_TipSharpness", "先端の尖り具合");
            Prop("_EdgeSoftness", "輪郭のぼかし");
            Prop("_BaseWidth", "根本の絞り込み");

            DrawSubBox("大きいノイズ(全体のうねり)", () =>
            {
                Prop("_NoiseScale", "細かさ");
                Prop("_NoiseStretch", "縦方向の伸び");
                Prop("_DistortionStrength", "歪み強度");
            });
            DrawSubBox("細かいノイズ(ディテール)", () =>
            {
                Prop("_NoiseScale2", "細かさ");
                Prop("_DetailStrength", "歪み強度");
            });
            Prop("_NoiseSpeed", "スクロール速度");
        });

        DrawSection("Sway Motion", ref showSway, () =>
        {
            Prop("_SwayAmount", "揺れの大きさ");
            Prop("_SwaySpeed", "揺れの速さ");
            Prop("_SwayFrequency", "揺れの周波数");
        });

        DrawSection("Flicker", ref showFlicker, () =>
        {
            Prop("_FlickerSpeed", "速さ");
            Prop("_FlickerIntensity", "強さ");
            Prop("_FlickerScale", "ノイズのスケール");
        });

        DrawSection("Color", ref showColor, () =>
        {
            MaterialProperty useRamp = Find("_UseColorRamp");
            editor.ShaderProperty(useRamp, "Color Rampを使う");
            bool rampEnabled = useRamp.floatValue > 0.5f;

            EditorGUILayout.Space(4);

            if (rampEnabled)
            {
                DrawGradientEditor(mat);
                Prop("_RampOffset", "Rampのずらし");
                Prop("_RampNoiseInfluence", "Rampへのノイズ影響度");
            }
            else
            {
                Prop("_ColorBase", "根本の色");
                Prop("_ColorMid", "中間の色");
                Prop("_ColorTip", "先端の色");
            }

            EditorGUILayout.Space(4);
            Prop("_Brightness", "明るさ倍率 (Bloom用)");
            Prop("_AlphaPower", "透明度のカーブ");
        });

        DrawSection("Render Settings", ref showRenderSettings, () =>
        {
            DrawSubBox("位置 / 大きさ", () =>
            {
                EditorGUILayout.HelpBox("平面内で炎の表示位置と大きさを調整できます。", MessageType.None);
                Prop("_FlameOffsetX", "横位置オフセット");
                Prop("_FlameOffsetY", "縦位置オフセット");
                Prop("_FlameSize", "大きさ");
            });

            DrawSubBox("合成モード (Blend Mode)", () =>
            {
                EditorGUILayout.HelpBox(
                    "Alpha合成: Src=One, Dst=OneMinusSrcAlpha\n" +
                    "Additive(加算)合成: Src=One, Dst=One",
                    MessageType.None);
                if (GUILayout.Button("Alphaプリセットにする"))
                {
                    Find("_SrcBlend").floatValue = 1f; // One
                    Find("_DstBlend").floatValue = 6f; // OneMinusSrcAlpha
                }
                if (GUILayout.Button("Additive(加算)プリセットにする"))
                {
                    Find("_SrcBlend").floatValue = 1f; // One
                    Find("_DstBlend").floatValue = 1f; // One
                }
                EditorGUILayout.Space(2);
                Prop("_SrcBlend", "Src Blend");
                Prop("_DstBlend", "Dst Blend");
            });

            DrawSubBox("Render Queue / Instancing", () =>
            {
                editor.RenderQueueField();
                editor.EnableInstancingField();
            });
        });
    }

    // ---- ヘルパー ----

    GUIStyle sectionStyle;
    GUIStyle subBoxStyle;
    GUIStyle headerStyle;

    void EnsureStyles()
    {
        if (sectionStyle == null)
        {
            // GUI.skin.box はUnity標準のベベル(縁取り)が付いた箱スタイル
            sectionStyle = new GUIStyle(GUI.skin.box);
            sectionStyle.padding = new RectOffset(12, 12, 10, 12);
            sectionStyle.margin = new RectOffset(2, 2, 2, 8);
        }
        if (subBoxStyle == null)
        {
            subBoxStyle = new GUIStyle(GUI.skin.box);
            subBoxStyle.padding = new RectOffset(8, 8, 6, 8);
            subBoxStyle.margin = new RectOffset(0, 0, 2, 6);
        }
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.foldoutHeader);
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = EditorStyles.foldoutHeader.fontSize + 1;
        }
    }

    MaterialProperty Find(string name)
    {
        return FindProperty(name, props, false);
    }

    void Prop(string name, string label)
    {
        MaterialProperty p = Find(name);
        if (p != null) editor.ShaderProperty(p, label);
    }

    void DrawSection(string title, ref bool state, Action drawBody)
    {
        EnsureStyles();
        state = EditorGUILayout.Foldout(state, title, true, headerStyle);
        if (state)
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUI.indentLevel++;
            drawBody();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.Space(2);
    }

    // セクション内でさらにグループ分けしたい場合の小さいベベル枠(ノイズの大小レイヤーなど)
    void DrawSubBox(string title, Action drawBody)
    {
        EnsureStyles();
        EditorGUILayout.BeginVertical(subBoxStyle);
        EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        drawBody();
        EditorGUILayout.EndVertical();
    }

    // ---- カラーランプのリアルタイム編集 ----

    void DrawGradientEditor(Material mat)
    {
        MaterialProperty rampProp = Find("_ColorRamp");
        Texture2D tex = rampProp.textureValue as Texture2D;
        string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;

        Gradient gradient = LoadGradient(tex, path);

        EditorGUI.BeginChangeCheck();
        Gradient newGradient = EditorGUILayout.GradientField("Color Ramp", gradient);
        bool changed = EditorGUI.EndChangeCheck();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            bool forceBake = GUILayout.Button("再生成", GUILayout.Width(80));
            if (forceBake) changed = true;
        }

        if (changed)
        {
            string savePath = path;
            if (string.IsNullOrEmpty(savePath))
            {
                string matPath = AssetDatabase.GetAssetPath(mat);
                string dir = string.IsNullOrEmpty(matPath) ? "Assets" : Path.GetDirectoryName(matPath);
                savePath = (dir + "/" + mat.name + "_ColorRamp.png").Replace("\\", "/");
            }

            Texture2D newTex = BakeGradientToTexture(newGradient, savePath);
            rampProp.textureValue = newTex;
        }
    }

    Gradient LoadGradient(Texture2D tex, string path)
    {
        if (tex != null && !string.IsNullOrEmpty(path))
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && !string.IsNullOrEmpty(importer.userData))
            {
                try
                {
                    SerializableGradient sg = JsonUtility.FromJson<SerializableGradient>(importer.userData);
                    if (sg != null && sg.colors != null && sg.colors.Length > 0)
                    {
                        return sg.ToGradient();
                    }
                }
                catch { /* 読み込めなければデフォルトにフォールバック */ }
            }
        }
        return DefaultFlameGradient();
    }

    static Gradient DefaultFlameGradient()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0.0f),
                new GradientColorKey(new Color(1f, 0.75f, 0.15f), 0.35f),
                new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.65f),
                new GradientColorKey(new Color(0.4f, 0.08f, 0.25f), 1.0f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            });
        return g;
    }

    static Texture2D BakeGradientToTexture(Gradient gradient, string path)
    {
        const int width = 256;
        const int height = 4;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        Color[] pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / (width - 1);
            Color c = gradient.Evaluate(u);
            for (int y = 0; y < height; y++)
            {
                pixels[y * width + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.userData = JsonUtility.ToJson(new SerializableGradient(gradient));
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    [Serializable]
    class SerializableGradient
    {
        public Color[] colors;
        public float[] colorTimes;
        public float[] alphas;
        public float[] alphaTimes;

        public SerializableGradient(Gradient g)
        {
            colors = new Color[g.colorKeys.Length];
            colorTimes = new float[g.colorKeys.Length];
            for (int i = 0; i < g.colorKeys.Length; i++)
            {
                colors[i] = g.colorKeys[i].color;
                colorTimes[i] = g.colorKeys[i].time;
            }

            alphas = new float[g.alphaKeys.Length];
            alphaTimes = new float[g.alphaKeys.Length];
            for (int i = 0; i < g.alphaKeys.Length; i++)
            {
                alphas[i] = g.alphaKeys[i].alpha;
                alphaTimes[i] = g.alphaKeys[i].time;
            }
        }

        public Gradient ToGradient()
        {
            Gradient g = new Gradient();
            GradientColorKey[] ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                ck[i] = new GradientColorKey(colors[i], colorTimes[i]);
            }

            GradientAlphaKey[] ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                ak[i] = new GradientAlphaKey(alphas[i], alphaTimes[i]);
            }

            g.SetKeys(ck, ak);
            return g;
        }
    }
}
#endif
