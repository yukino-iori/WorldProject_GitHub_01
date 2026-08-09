#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// ===================================================================
// LightmapSwitcher 登録ツール
// ---------------------------------------------------------------
// 機能:
//  ① 検索ルート配下のRenderer/ReflectionProbeを自動検出してLightmapSwitcherへ登録
//  ② ベイク結果(Lightmap / ReflectionProbeのCubemap)を指定フォルダへ複製保存
//     - 指定フォルダ内に "DayNightLightTextures" を作成(あれば再利用)
//     - その中に "Day" / "Night" フォルダを作成(あれば再利用)し、
//       それぞれの保存先へ①の順番通りに複製保存する
//  ③ "DayNightLightTextures" フォルダの中身を読み取り、
//     LightmapSwitcherのDay/Night用テクスチャ配列へ自動割り当て
//
// 想定ワークフロー:
//  1. Target Switcher と 検索ルート を指定して「①自動検出して登録」
//  2. 夜設定でLightingベイクを実行 →「夜用として複製保存」
//  3. 昼設定でLightingベイクを実行 →「昼用として複製保存」
//     (①のRenderer/Probeの並び順を変えないよう、途中で自動検出をやり直さないこと)
//  4. 「DayNightLightTexturesから自動割り当て」を押す
//     → LightmapSwitcherのday/nightLightmaps, day/nightReflectionCubemapsが
//       自動で埋まる。ReflectionProbeはこのタイミングで Mode = Custom に
//       切り替えられる(切り替え前にベイクを済ませておくこと)
// ===================================================================
public class LightmapSwitcherSetupTool : EditorWindow
{
    // ウィンドウを閉じても消えないよう、登録内容はアセットとして保持する
    public class SetupToolState : ScriptableObject
    {
        public LightmapSwitcher targetSwitcher;
        public GameObject[] searchRoots = new GameObject[0];
        public string baseFolderPath = "Assets";
    }

    private const string StateFolder = "Assets/Editor/LightmapSwitcherSetupToolData";
    private const string StateAssetPath = StateFolder + "/SetupToolState.asset";

    private const string RootFolderName = "DayNightLightTextures";
    private const string DayFolderName = "Day";
    private const string NightFolderName = "Night";

    private SetupToolState state;
    private Vector2 scrollPos;
    private bool foldSearchRoots = true;

    [MenuItem("Window/DayNight/Lightmap Switcher Setup Tool")]
    public static void ShowWindow()
    {
        GetWindow<LightmapSwitcherSetupTool>("Switcher Setup");
    }

    private void OnEnable()
    {
        LoadOrCreateState();
    }

    private void LoadOrCreateState()
    {
        state = AssetDatabase.LoadAssetAtPath<SetupToolState>(StateAssetPath);
        if (state == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Editor"))
                AssetDatabase.CreateFolder("Assets", "Editor");
            if (!AssetDatabase.IsValidFolder(StateFolder))
                AssetDatabase.CreateFolder("Assets/Editor", "LightmapSwitcherSetupToolData");

            state = ScriptableObject.CreateInstance<SetupToolState>();
            AssetDatabase.CreateAsset(state, StateAssetPath);
            AssetDatabase.SaveAssets();
        }
    }

    private void SaveState()
    {
        EditorUtility.SetDirty(state);
        AssetDatabase.SaveAssets();
    }

    private void OnGUI()
    {
        if (state == null) LoadOrCreateState();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Lightmap Switcher 登録ツール", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 【重要】state はScriptableObjectアセットなので、SerializedProperty経由の
        // PropertyFieldではシーン上のオブジェクトをドラッグ&ドロップで代入できない
        // (allowSceneObjects=falseに強制される)。ObjectFieldを直接呼んで回避する。
        LightmapSwitcher newSwitcher = (LightmapSwitcher)EditorGUILayout.ObjectField(
            "Target Switcher", state.targetSwitcher, typeof(LightmapSwitcher), true);
        if (newSwitcher != state.targetSwitcher)
        {
            state.targetSwitcher = newSwitcher;
            SaveState();
        }

        EditorGUILayout.Space();
        GUILayout.Label("① 検索対象のルートオブジェクト (複数指定可)", EditorStyles.boldLabel);
        GameObject[] newRoots = DrawSceneObjectArray<GameObject>(
            "Search Roots", ref foldSearchRoots, state.searchRoots);
        if (!ArraysEqual(newRoots, state.searchRoots))
        {
            state.searchRoots = newRoots;
            SaveState();
        }

        if (GUILayout.Button("RendererとProbeを自動検出して登録", GUILayout.Height(30)))
        {
            AutoDetectAndAssignTargets();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "注意: ②の保存を昼夜それぞれで行う間は、上の「自動検出」を再実行しないでください。" +
            "Renderer/Probeの並び順が変わると、昼夜のテクスチャが対応する相手を取り違えます。",
            MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.Space();
        GUILayout.Label("② 保存先フォルダ", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        state.baseFolderPath = EditorGUILayout.TextField(state.baseFolderPath);
        if (GUI.changed) SaveState();
        if (GUILayout.Button("参照...", GUILayout.Width(60)))
        {
            string selected = EditorUtility.OpenFolderPanel("保存先フォルダを選択", "Assets", "");
            if (!string.IsNullOrEmpty(selected))
            {
                string rel = ConvertToProjectRelativePath(selected);
                if (rel == null)
                {
                    EditorUtility.DisplayDialog("エラー", "プロジェクトの Assets フォルダ内を選択してください。", "OK");
                }
                else
                {
                    state.baseFolderPath = rel;
                    SaveState();
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.HelpBox(
            "指定フォルダ内に \"" + RootFolderName + "/" + DayFolderName + "\" と \"" +
            RootFolderName + "/" + NightFolderName + "\" が作成されます(既にある場合は再利用)。",
            MessageType.Info);

        EditorGUILayout.Space();
        GUILayout.Label("③ ベイク結果を複製保存", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("夜設定でLightingベイクを実行した直後に押してください", MessageType.None);
        if (GUILayout.Button("夜用として複製保存 (Capture Night)", GUILayout.Height(26)))
        {
            CaptureAndSave(NightFolderName);
        }
        EditorGUILayout.HelpBox("昼設定でLightingベイクを実行した直後に押してください", MessageType.None);
        if (GUILayout.Button("昼用として複製保存 (Capture Day)", GUILayout.Height(26)))
        {
            CaptureAndSave(DayFolderName);
        }

        EditorGUILayout.Space();
        GUILayout.Label("④ フォルダ内を参照して自動割り当て", EditorStyles.boldLabel);
        if (GUILayout.Button(RootFolderName + " から自動割り当て (Auto Assign)", GUILayout.Height(30)))
        {
            AutoAssignFromFolder();
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("登録内容をリセット"))
        {
            if (EditorUtility.DisplayDialog(
                "確認",
                "検索ルート/保存先フォルダ設定をリセットします。よろしいですか?\n(保存済みのテクスチャファイル自体は消えません)",
                "リセットする", "キャンセル"))
            {
                state.searchRoots = new GameObject[0];
                state.targetSwitcher = null;
                state.baseFolderPath = "Assets";
                SaveState();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
    }

    private string ConvertToProjectRelativePath(string absolutePath)
    {
        absolutePath = absolutePath.Replace("\\", "/");
        string projectPath = Application.dataPath.Replace("\\", "/"); // .../Assets
        if (!absolutePath.StartsWith(projectPath)) return null;
        return "Assets" + absolutePath.Substring(projectPath.Length);
    }

    /// <summary>
    /// シーン上のオブジェクトへの参照配列を手動描画するヘルパー。
    /// ScriptableObjectアセットのSerializedPropertyはallowSceneObjects=falseになるため、
    /// EditorGUILayout.ObjectFieldを直接使ってドラッグ&ドロップを受け付ける。
    /// </summary>
    private T[] DrawSceneObjectArray<T>(string label, ref bool foldout, T[] array) where T : Object
    {
        if (array == null) array = new T[0];

        foldout = EditorGUILayout.Foldout(foldout, label + " [" + array.Length + "]", true);
        if (!foldout) return array;

        EditorGUI.indentLevel++;

        int newSize = EditorGUILayout.IntField("Size", array.Length);
        if (newSize != array.Length)
        {
            T[] resized = new T[newSize];
            for (int i = 0; i < Mathf.Min(newSize, array.Length); i++)
                resized[i] = array[i];
            array = resized;
        }

        for (int i = 0; i < array.Length; i++)
        {
            EditorGUILayout.BeginHorizontal();
            array[i] = (T)EditorGUILayout.ObjectField("Element " + i, array[i], typeof(T), true);

            if (GUILayout.Button("×", GUILayout.Width(22)))
            {
                var list = new List<T>(array);
                list.RemoveAt(i);
                array = list.ToArray();
                EditorGUILayout.EndHorizontal();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.indentLevel--;
        return array;
    }

    private bool ArraysEqual<T>(T[] a, T[] b) where T : Object
    {
        if (a == null) a = new T[0];
        if (b == null) b = new T[0];
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    // ============================================================
    // ① 自動検出
    // ============================================================
    private void AutoDetectAndAssignTargets()
    {
        if (state.targetSwitcher == null)
        {
            EditorUtility.DisplayDialog("エラー", "Target Switcher が指定されていません。", "OK");
            return;
        }
        if (state.searchRoots == null || state.searchRoots.Length == 0)
        {
            EditorUtility.DisplayDialog("エラー", "検索対象のルートオブジェクトが指定されていません。", "OK");
            return;
        }

        List<Renderer> renderersList = new List<Renderer>();
        List<ReflectionProbe> probesList = new List<ReflectionProbe>();

        foreach (GameObject root in state.searchRoots)
        {
            if (root == null) continue;

            Renderer[] childRenderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in childRenderers)
            {
                if (!renderersList.Contains(r)) renderersList.Add(r);
            }

            ReflectionProbe[] childProbes = root.GetComponentsInChildren<ReflectionProbe>(true);
            foreach (ReflectionProbe p in childProbes)
            {
                if (!probesList.Contains(p)) probesList.Add(p);
            }
        }

        Undo.RecordObject(state.targetSwitcher, "Auto Detect Switcher Targets");
        state.targetSwitcher.targetRenderers = renderersList.ToArray();
        state.targetSwitcher.targetProbes = probesList.ToArray();
        EditorUtility.SetDirty(state.targetSwitcher);

        EditorUtility.DisplayDialog("登録完了",
            $"対象の自動登録が完了しました。\n\nRenderers: {state.targetSwitcher.targetRenderers.Length}\nReflectionProbes: {state.targetSwitcher.targetProbes.Length}",
            "OK");
    }

    // ============================================================
    // ③ ベイク結果を複製保存
    // ============================================================
    private void CaptureAndSave(string subFolderName)
    {
        if (state.targetSwitcher == null)
        {
            EditorUtility.DisplayDialog("エラー", "Target Switcher が指定されていません。", "OK");
            return;
        }

        Renderer[] renderers = state.targetSwitcher.targetRenderers;
        ReflectionProbe[] probes = state.targetSwitcher.targetProbes;
        if ((renderers == null || renderers.Length == 0) && (probes == null || probes.Length == 0))
        {
            EditorUtility.DisplayDialog("エラー", "対象のRenderer/Probeが登録されていません。先に①を実行してください。", "OK");
            return;
        }

        string rootFolder = EnsureFolder(state.baseFolderPath, RootFolderName);
        string targetFolder = EnsureFolder(rootFolder, subFolderName);

        // 前回このフォルダに保存したものは消してから保存し直す(インデックスの整合性を保つため)
        ClearFolderContents(targetFolder);

        int savedLightmaps = 0;
        int savedProbes = 0;

        // ---- Lightmap ----
        if (renderers != null && renderers.Length > 0)
        {
            Texture2D[] sourcePerRenderer = new Texture2D[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r.lightmapIndex < 0 || r.lightmapIndex >= LightmapSettings.lightmaps.Length)
                {
                    sourcePerRenderer[i] = null;
                    continue;
                }
                sourcePerRenderer[i] = LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor;
            }

            string lightmapPrefix = "Lightmap_" + subFolderName + "_";
            string padFormat = GetPadFormat(sourcePerRenderer.Length);

            if (AllSameNonNull(sourcePerRenderer))
            {
                Texture2D copy = DuplicateAsset(sourcePerRenderer.First(t => t != null), targetFolder, lightmapPrefix + "Shared");
                savedLightmaps = copy != null ? 1 : 0;
            }
            else
            {
                for (int i = 0; i < sourcePerRenderer.Length; i++)
                {
                    if (sourcePerRenderer[i] == null) continue;
                    Texture2D copy = DuplicateAsset(sourcePerRenderer[i], targetFolder, lightmapPrefix + (i + 1).ToString(padFormat));
                    if (copy != null) savedLightmaps++;
                }
            }
        }

        // ---- ReflectionProbe ----
        if (probes != null && probes.Length > 0)
        {
            Cubemap[] sourcePerProbe = new Cubemap[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbe p = probes[i];
                if (p == null) { sourcePerProbe[i] = null; continue; }
                Texture tex = p.bakedTexture != null ? p.bakedTexture : p.customBakedTexture;
                sourcePerProbe[i] = tex as Cubemap;
            }

            string probePrefix = "ReflectionProbe_" + subFolderName + "_";
            string probePadFormat = GetPadFormat(sourcePerProbe.Length);

            if (AllSameNonNull(sourcePerProbe))
            {
                Cubemap copy = DuplicateAsset(sourcePerProbe.First(t => t != null), targetFolder, probePrefix + "Shared");
                savedProbes = copy != null ? 1 : 0;
            }
            else
            {
                for (int i = 0; i < sourcePerProbe.Length; i++)
                {
                    if (sourcePerProbe[i] == null) continue;
                    Cubemap copy = DuplicateAsset(sourcePerProbe[i], targetFolder, probePrefix + (i + 1).ToString(probePadFormat));
                    if (copy != null) savedProbes++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("保存完了",
            $"{subFolderName} フォルダへ保存しました。\n\nLightmap: {savedLightmaps} 枚\nReflectionProbe Cubemap: {savedProbes} 枚\n\n保存先: {targetFolder}",
            "OK");
    }

    // 連番の桁数を決める。最低2桁("01"形式)、101個以上ある場合は自動で桁を増やす
    private string GetPadFormat(int count)
    {
        int digits = Mathf.Max(2, count.ToString().Length);
        return "D" + digits;
    }

    private bool AllSameNonNull<T>(T[] arr) where T : Object
    {
        T first = null;
        foreach (T item in arr)
        {
            if (item == null) continue;
            if (first == null) { first = item; continue; }
            if (item != first) return false;
        }
        return first != null;
    }

    private T DuplicateAsset<T>(T src, string folder, string baseName) where T : Object
    {
        if (src == null) return null;
        string srcPath = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(srcPath)) return src; // すでに恒久アセットの場合はそのまま使う

        string ext = Path.GetExtension(srcPath);
        string destPath = folder + "/" + baseName + ext;
        destPath = AssetDatabase.GenerateUniqueAssetPath(destPath);

        AssetDatabase.CopyAsset(srcPath, destPath);
        return AssetDatabase.LoadAssetAtPath<T>(destPath);
    }

    private string EnsureFolder(string parentFolder, string folderName)
    {
        parentFolder = parentFolder.TrimEnd('/');
        string fullPath = parentFolder + "/" + folderName;
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }
        return fullPath;
    }

    private void ClearFolderContents(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string dir = Path.GetDirectoryName(path).Replace("\\", "/");
            // フォルダ自体・サブフォルダには触れず、直下のファイルのみ削除する
            if (dir == folder)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }

    // ============================================================
    // ④ フォルダから自動割り当て
    // ============================================================
    private void AutoAssignFromFolder()
    {
        if (state.targetSwitcher == null)
        {
            EditorUtility.DisplayDialog("エラー", "Target Switcher が指定されていません。", "OK");
            return;
        }

        string rootFolder = state.baseFolderPath.TrimEnd('/') + "/" + RootFolderName;
        string dayFolder = rootFolder + "/" + DayFolderName;
        string nightFolder = rootFolder + "/" + NightFolderName;

        if (!AssetDatabase.IsValidFolder(dayFolder) || !AssetDatabase.IsValidFolder(nightFolder))
        {
            EditorUtility.DisplayDialog("エラー",
                $"フォルダが見つかりません。\n{dayFolder}\n{nightFolder}\n\n先に③で保存を行うか、保存先フォルダの指定を確認してください。", "OK");
            return;
        }

        int rendererCount = state.targetSwitcher.targetRenderers != null ? state.targetSwitcher.targetRenderers.Length : 0;
        int probeCount = state.targetSwitcher.targetProbes != null ? state.targetSwitcher.targetProbes.Length : 0;

        Texture2D[] dayLightmaps = LoadOrderedAssets<Texture2D>(dayFolder, "Lightmap_" + DayFolderName + "_", rendererCount);
        Texture2D[] nightLightmaps = LoadOrderedAssets<Texture2D>(nightFolder, "Lightmap_" + NightFolderName + "_", rendererCount);
        Cubemap[] dayProbes = LoadOrderedAssets<Cubemap>(dayFolder, "ReflectionProbe_" + DayFolderName + "_", probeCount);
        Cubemap[] nightProbes = LoadOrderedAssets<Cubemap>(nightFolder, "ReflectionProbe_" + NightFolderName + "_", probeCount);

        Undo.RecordObject(state.targetSwitcher, "Auto Assign From Folder");
        state.targetSwitcher.dayLightmaps = dayLightmaps;
        state.targetSwitcher.nightLightmaps = nightLightmaps;
        state.targetSwitcher.dayReflectionCubemaps = dayProbes;
        state.targetSwitcher.nightReflectionCubemaps = nightProbes;
        EditorUtility.SetDirty(state.targetSwitcher);

        // ReflectionProbeはCustomモードでないとcustomBakedTextureが反映されないため、ここで切り替える
        if (state.targetSwitcher.targetProbes != null)
        {
            foreach (ReflectionProbe p in state.targetSwitcher.targetProbes)
            {
                if (p == null) continue;
                Undo.RecordObject(p, "Set Reflection Probe To Custom");
                p.mode = ReflectionProbeMode.Custom;
                EditorUtility.SetDirty(p);
            }
        }

        AssetDatabase.SaveAssets();

        string warning = "";
        if (rendererCount > 0 && dayLightmaps.Length != 1 && dayLightmaps.Length != rendererCount)
            warning += "⚠ Day Lightmapの枚数がRenderer数と一致していません\n";
        if (rendererCount > 0 && nightLightmaps.Length != 1 && nightLightmaps.Length != rendererCount)
            warning += "⚠ Night Lightmapの枚数がRenderer数と一致していません\n";
        if (probeCount > 0 && dayProbes.Length != 1 && dayProbes.Length != probeCount)
            warning += "⚠ Day Probeの枚数がProbe数と一致していません\n";
        if (probeCount > 0 && nightProbes.Length != 1 && nightProbes.Length != probeCount)
            warning += "⚠ Night Probeの枚数がProbe数と一致していません\n";

        EditorUtility.DisplayDialog("割り当て完了",
            $"自動割り当てが完了しました。\n\n" +
            $"Day Lightmap: {dayLightmaps.Length} 枚 / Night Lightmap: {nightLightmaps.Length} 枚\n" +
            $"Day Probe: {dayProbes.Length} 枚 / Night Probe: {nightProbes.Length} 枚\n" +
            (string.IsNullOrEmpty(warning) ? "" : "\n" + warning + "\n③をやり直すか、①の並び順を確認してください。"),
            "OK");
    }

    // "{prefix}Shared" が1つだけあればそれを共有テクスチャとして返す。
    // なければ "{prefix}01", "{prefix}02"... を番号順に並べて配列にする。
    // (例: prefix="Lightmap_Day_" → "Lightmap_Day_01", "Lightmap_Day_Shared")
    private T[] LoadOrderedAssets<T>(string folder, string prefix, int expectedCount) where T : Object
    {
        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        Regex indexRegex = new Regex("^" + Regex.Escape(prefix) + "(\\d+)$");
        string sharedName = prefix + "Shared";

        T sharedAsset = null;
        List<KeyValuePair<int, T>> indexed = new List<KeyValuePair<int, T>>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);

            if (fileName == sharedName)
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) sharedAsset = asset;
                continue;
            }

            Match m = indexRegex.Match(fileName);
            if (!m.Success) continue;

            T indexedAsset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (indexedAsset != null)
            {
                // ファイル名は1始まり(01, 02...)なので、配列インデックス(0始まり)に変換する
                int fileNumber = int.Parse(m.Groups[1].Value);
                indexed.Add(new KeyValuePair<int, T>(fileNumber - 1, indexedAsset));
            }
        }

        if (sharedAsset != null)
        {
            return new T[] { sharedAsset };
        }

        if (indexed.Count == 0) return new T[0];

        indexed.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = expectedCount > 0 ? expectedCount : indexed[indexed.Count - 1].Key + 1;
        T[] result = new T[count];
        foreach (var kv in indexed)
        {
            if (kv.Key >= 0 && kv.Key < count)
            {
                result[kv.Key] = kv.Value;
            }
        }
        return result;
    }
}
#endif
