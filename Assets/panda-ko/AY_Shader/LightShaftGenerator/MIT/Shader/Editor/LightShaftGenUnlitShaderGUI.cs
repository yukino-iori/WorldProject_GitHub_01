// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: Copyright (c) 2026 AYANO_TFT.

using UnityEditor;
using UnityEngine;

namespace AY_Shader
{
    public class LightShaftGenUnlitShaderGUI : ShaderGUI
    {
        private const string LanguagePrefKey = "AY_Tools.LightShaftMeshComponent.Language";
        private const string FoldoutPrefPrefix = "AY_Shader.LightShaftGenUnlitShaderGUI.Foldout.";

        private static EditorLanguage currentLanguage = EditorLanguage.Japanese;
        private static bool languageInitialized;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            EnsureLanguage();

            DrawLanguageSelector();
            EditorGUILayout.Space();

            DrawGroup(
                "Main",
                L(LightShaftGenUnlitTextKey.SectionMain),
                materialEditor,
                props,
                new[]
                {
                    Prop("_MainTex", LightShaftGenUnlitTextKey.LabelMainTex),
                    Prop("_Color", LightShaftGenUnlitTextKey.LabelColor),
                    Prop("_Intensity", LightShaftGenUnlitTextKey.LabelIntensity),
                });

            DrawGroup(
                "Fade",
                L(LightShaftGenUnlitTextKey.SectionFade),
                materialEditor,
                props,
                new[]
                {
                    Prop("_CameraFadeMin", LightShaftGenUnlitTextKey.LabelCameraFadeEnd),
                    Prop("_CameraFadeMax", LightShaftGenUnlitTextKey.LabelCameraFadeStart),
                    Prop("_EdgeFade", LightShaftGenUnlitTextKey.LabelEdgeFade),
                    Prop("_CornerFade", LightShaftGenUnlitTextKey.LabelRadialFade),
                    Prop("_TopAnchor", LightShaftGenUnlitTextKey.LabelTopAnchor),
                });

            DrawGroup(
                "Atten",
                L(LightShaftGenUnlitTextKey.SectionAtten),
                materialEditor,
                props,
                new[]
                {
                    Prop("_AttenRate", LightShaftGenUnlitTextKey.LabelAttenStrength),
                    Prop("_AttenSharpness", LightShaftGenUnlitTextKey.LabelAttenSharpness),
                });

            DrawGroup(
                "Occlusion",
                L(LightShaftGenUnlitTextKey.SectionOcclusion),
                materialEditor,
                props,
                new[]
                {
                    Prop("_NoiseTex", LightShaftGenUnlitTextKey.LabelOcclusionTex),
                    Prop("_NoiseTexScale", LightShaftGenUnlitTextKey.LabelOcclusionScale),
                    Prop("_NoiseSpeedX", LightShaftGenUnlitTextKey.LabelScrollSpeedX),
                    Prop("_NoiseSpeedY", LightShaftGenUnlitTextKey.LabelScrollSpeedY),
                    Prop("_NoiseMin", LightShaftGenUnlitTextKey.LabelOcclusionMin),
                    Prop("_NoiseMax", LightShaftGenUnlitTextKey.LabelOcclusionMax),
                });
        }

        private struct PropDef
        {
            public string name;
            public LightShaftGenUnlitTextKey labelKey;
        }

        private static PropDef Prop(string name, LightShaftGenUnlitTextKey labelKey)
        {
            return new PropDef { name = name, labelKey = labelKey };
        }

        private static void DrawGroup(
            string prefKey,
            string title,
            MaterialEditor materialEditor,
            MaterialProperty[] props,
            PropDef[] propDefs)
        {
            string key = FoldoutPrefPrefix + prefKey;
            bool open = EditorPrefs.GetBool(key, true);

            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            EditorPrefs.SetBool(key, open);

            if (open)
            {
                EditorGUI.indentLevel++;
                foreach (var def in propDefs)
                {
                    DrawProperty(materialEditor, props, def.name, L(def.labelKey));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();
        }

        private static void DrawProperty(
            MaterialEditor materialEditor,
            MaterialProperty[] props,
            string propertyName,
            string label)
        {
            var prop = FindProperty(propertyName, props, false);
            if (prop == null)
            {
                return;
            }

            var content = new GUIContent(label);
            if (prop.type == MaterialProperty.PropType.Texture)
            {
                materialEditor.TexturePropertySingleLine(content, prop);
                if (propertyName == "_MainTex")
                {
                    materialEditor.TextureScaleOffsetProperty(prop);
                }
            }
            else
            {
                materialEditor.ShaderProperty(prop, content);
            }
        }

        private static void EnsureLanguage()
        {
            int lang = EditorPrefs.GetInt(LanguagePrefKey, (int)EditorLanguage.Japanese);
            var prefLanguage = (EditorLanguage)Mathf.Clamp(lang, 0, 1);
            if (!languageInitialized || prefLanguage != currentLanguage)
            {
                currentLanguage = prefLanguage;
                languageInitialized = true;
            }
        }

        private static void DrawLanguageSelector()
        {
            int current = (int)currentLanguage;
            string[] displayNames = { "日本語", "English" };
            int[] values = { (int)EditorLanguage.Japanese, (int)EditorLanguage.English };

            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.IntPopup(
                "Language",
                current,
                displayNames,
                values);
            if (EditorGUI.EndChangeCheck())
            {
                currentLanguage = (EditorLanguage)Mathf.Clamp(selected, 0, values.Length - 1);
                EditorPrefs.SetInt(LanguagePrefKey, (int)currentLanguage);
            }
        }

        private static string L(LightShaftGenUnlitTextKey key)
        {
            return LightShaftGenUnlitLocalization.Get(currentLanguage, key);
        }
    }
}
