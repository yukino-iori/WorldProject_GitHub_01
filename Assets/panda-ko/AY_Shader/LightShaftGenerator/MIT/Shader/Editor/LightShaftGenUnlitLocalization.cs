// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: Copyright (c) 2026 AYANO_TFT.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AY_Shader
{
    internal enum EditorLanguage
    {
        Japanese = 0,
        English = 1,
    }

    internal enum LightShaftGenUnlitTextKey
    {
        SectionMain,
        SectionFade,
        SectionAtten,
        SectionOcclusion,

        LabelMainTex,
        LabelColor,
        LabelIntensity,
        LabelCameraFadeEnd,
        LabelCameraFadeStart,
        LabelEdgeFade,
        LabelRadialFade,
        LabelTopAnchor,
        LabelAttenStrength,
        LabelAttenSharpness,
        LabelOcclusionTex,
        LabelOcclusionScale,
        LabelScrollSpeedX,
        LabelScrollSpeedY,
        LabelOcclusionMin,
        LabelOcclusionMax,
    }

    internal sealed class LightShaftGenUnlitLocalizationAnchor : ScriptableObject { }

    internal static class LightShaftGenUnlitLocalization
    {
        private static bool _isLoaded;
        private static Dictionary<LightShaftGenUnlitTextKey, string[]> _table;

        internal static string Get(EditorLanguage language, LightShaftGenUnlitTextKey key)
        {
            EnsureLoaded();

            if (_table != null && _table.TryGetValue(key, out var values))
            {
                int idx = (int)language;
                if (idx >= 0 && idx < values.Length && !string.IsNullOrEmpty(values[idx]))
                {
                    return values[idx];
                }
            }

            return key.ToString();
        }

        private static void EnsureLoaded()
        {
            if (_isLoaded) return;

            TextAsset csv = LoadCsvAsset();
            if (csv == null)
            {
                return;
            }

            string[] lines = csv.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1)
            {
                return;
            }

            var table = new Dictionary<LightShaftGenUnlitTextKey, string[]>();

            for (int i = 1; i < lines.Length; ++i)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                List<string> cols = SplitCsvLine(line);
                if (cols.Count < 3) continue;

                string keyStr = cols[0].Trim();
                if (!Enum.TryParse(keyStr, out LightShaftGenUnlitTextKey key)) continue;

                var values = new string[2];
                values[(int)EditorLanguage.Japanese] = cols[1].Trim();
                values[(int)EditorLanguage.English] = cols[2].Trim();
                table[key] = values;
            }

            _table = table;
            _isLoaded = true;
        }

        private static TextAsset LoadCsvAsset()
        {
            var anchor = ScriptableObject.CreateInstance<LightShaftGenUnlitLocalizationAnchor>();
            var script = MonoScript.FromScriptableObject(anchor);
            string scriptPath = AssetDatabase.GetAssetPath(script);
            ScriptableObject.DestroyImmediate(anchor);

            if (!string.IsNullOrEmpty(scriptPath))
            {
                string folder = Path.GetDirectoryName(scriptPath)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(folder))
                {
                    string csvPath = Path.Combine(folder, "LightShaftGenUnlitLocalization.csv").Replace("\\", "/");
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(csvPath);
                    if (asset != null)
                    {
                        return asset;
                    }
                }
            }

            var guids = AssetDatabase.FindAssets("LightShaftGenUnlitLocalization t:TextAsset");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("LightShaftGenUnlitLocalization.csv", StringComparison.OrdinalIgnoreCase))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (asset != null)
                    {
                        return asset;
                    }
                }
            }

            Debug.LogWarning("LightShaftGenUnlitLocalization: CSV asset could not be loaded.");
            return null;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line))
            {
                result.Add(string.Empty);
                return result;
            }

            bool inQuotes = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; ++i)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString());
            return result;
        }
    }
}
