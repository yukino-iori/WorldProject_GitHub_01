// Assets/Editor/ 以下に配置してください。
// 例: Assets/Editor/PhotoSettingManagerEditor.cs

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// PhotoSettingManager のカスタムエディタ。
/// インスペクター上の UI 描画と、マテリアル生成・割り当て処理を担います。
/// </summary>
[CustomEditor(typeof(PhotoSettingManager))]
public class PhotoSettingManagerEditor : Editor
{
    // ---- SerializedProperty ----
    private SerializedProperty _photoSettingsProp;
    private SerializedProperty _baseSaveFolderPathProp;
    private SerializedProperty _useGlobalEmissionProp;
    private SerializedProperty _globalEmissionIntensityProp;

    // ---- 折りたたみ状態（インデックスごとに bool を保持） ----
    // Editor インスタンスが生きている間だけ保持される一時データ。
    // シリアライズ不要なのでただの List<bool> で管理する。
    private List<bool> _foldouts = new List<bool>();

    private void OnEnable()
    {
        _photoSettingsProp            = serializedObject.FindProperty("photoSettings");
        _baseSaveFolderPathProp       = serializedObject.FindProperty("baseSaveFolderPath");
        _useGlobalEmissionProp        = serializedObject.FindProperty("useGlobalEmission");
        _globalEmissionIntensityProp  = serializedObject.FindProperty("globalEmissionIntensity");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // _foldouts のサイズをリストサイズに合わせる（要素追加・削除に追従）
        while (_foldouts.Count < _photoSettingsProp.arraySize) _foldouts.Add(true);
        while (_foldouts.Count > _photoSettingsProp.arraySize) _foldouts.RemoveAt(_foldouts.Count - 1);

        // ===== ヘッダー =====
        EditorGUILayout.Space(4);
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 13,
            alignment = TextAnchor.MiddleCenter
        };
        EditorGUILayout.LabelField("📷 Photo Setting Manager", titleStyle);
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "写真テクスチャを MeshRenderer に一括適用し、マテリアルを自動生成するツールです。\n" +
            "設定を登録して「Generate & Apply All」ボタンを押してください。",
            MessageType.Info
        );
        EditorGUILayout.Space(8);

        // ===== 保存先フォルダ設定 =====
        DrawFolderSettings();

        EditorGUILayout.Space(12);

        // ===== 共通エミッション設定 =====
        DrawGlobalEmissionSettings();

        EditorGUILayout.Space(12);

        // ===== 写真設定リスト =====
        EditorGUILayout.LabelField("■ 写真設定リスト", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        for (int i = 0; i < _photoSettingsProp.arraySize; i++)
        {
            bool deleted = DrawPhotoSettingEntry(i);
            if (deleted)
            {
                serializedObject.ApplyModifiedProperties();
                return; // 削除後は即リターンして再描画させる
            }
        }

        // ===== ドロップゾーン & 追加ボタン =====
        DrawDragAndDropArea();

        if (GUILayout.Button("＋ 設定を追加", GUILayout.Height(28)))
        {
            AddNewEntry();
        }

        EditorGUILayout.Space(16);

        // ===== 実行ボタン =====
        GUI.backgroundColor = new Color(0.2f, 0.5f, 1.0f);
        GUIStyle generateButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize    = 14,
            fontStyle   = FontStyle.Bold,
            fixedHeight = 48,
            normal      = { textColor = Color.white },
        };
        if (GUILayout.Button("▶  Generate & Apply All\n（マテリアル生成 & 一括割り当て）", generateButtonStyle))
        {
            serializedObject.ApplyModifiedProperties();
            GenerateAndApplyAll();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);
        serializedObject.ApplyModifiedProperties();
    }

    // =========================================================
    // UI パーツ
    // =========================================================

    /// <summary>保存先フォルダ設定セクションを描画します。</summary>
    private void DrawFolderSettings()
    {
        EditorGUILayout.LabelField("■ 保存先フォルダ設定", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(_baseSaveFolderPathProp, new GUIContent("ベースフォルダ"));
        if (GUILayout.Button("参照...", GUILayout.Width(60)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel(
                "保存先ベースフォルダを選択",
                _baseSaveFolderPathProp.stringValue, ""
            );
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (selectedPath.StartsWith(Application.dataPath))
                    selectedPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                _baseSaveFolderPathProp.stringValue = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        string previewPath = Path.Combine(_baseSaveFolderPathProp.stringValue, "PhotoMaterials")
                                 .Replace("\\", "/");
        EditorGUILayout.HelpBox($"マテリアル保存先: {previewPath}", MessageType.None);
    }

    /// <summary>
    /// 共通エミッション設定セクションを描画します。
    /// ライトベイクに関する説明もここに1箇所だけ表示します。
    /// </summary>
    private void DrawGlobalEmissionSettings()
    {
        EditorGUILayout.LabelField("■ 共通エミッション設定", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // --- 共通設定 ON/OFF チェックボックス ---
        EditorGUI.BeginChangeCheck();
        bool newUseGlobal = EditorGUILayout.Toggle(
            new GUIContent(
                "💡 共通エミッション設定を使用する",
                "ON にすると、下の強度が全エントリに一括適用されます。\n" +
                "各エントリ個別のエミッション設定は無効になります。"
            ),
            _useGlobalEmissionProp.boolValue
        );
        if (EditorGUI.EndChangeCheck())
            _useGlobalEmissionProp.boolValue = newUseGlobal;

        if (_useGlobalEmissionProp.boolValue)
        {
            EditorGUI.indentLevel++;

            // 共通強度スライダー
            EditorGUILayout.Slider(
                _globalEmissionIntensityProp,
                0f, 5f,
                new GUIContent("共通発光強度", "すべてのエントリに適用される発光強度です。")
            );

            // 強度ガイドメッセージ
            float gIntensity = _globalEmissionIntensityProp.floatValue;
            string gGuide;
            MessageType gMsgType;
            if      (gIntensity <= 0f) { gGuide = "強度 0：実質的に発光しません。";                          gMsgType = MessageType.Warning; }
            else if (gIntensity <= 1f) { gGuide = $"強度 {gIntensity:F2}：控えめな発光です。";               gMsgType = MessageType.None;    }
            else if (gIntensity <= 2f) { gGuide = $"強度 {gIntensity:F2}：明るめの発光です。";               gMsgType = MessageType.None;    }
            else                       { gGuide = $"強度 {gIntensity:F2}：非常に強い発光。HDRで飛びやすいので注意。"; gMsgType = MessageType.Warning; }
            EditorGUILayout.HelpBox(gGuide, gMsgType);

            EditorGUI.indentLevel--;
        }
        else
        {
            // OFF のとき：個別設定を使う旨を表示
            EditorGUILayout.HelpBox(
                "現在は各エントリ個別のエミッション設定が使用されます。",
                MessageType.None
            );
        }

        EditorGUILayout.Space(4);

        // ライトベイク説明（ここに1箇所だけ）
        EditorGUILayout.HelpBox(
            "🔒 ライトベイク設定について：\n" +
            "このツールで生成したマテリアルのエミッションは「他オブジェクトへの GI 影響なし」に\n" +
            "自動設定されます（GlobalIlluminationFlags = None）。\n" +
            "写真の発光が周囲のオブジェクトにライトベイクで光を落とすことはありません。",
            MessageType.Info
        );

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 写真設定エントリ1件を描画します。
    /// </summary>
    /// <param name="i">リストのインデックス</param>
    /// <returns>削除ボタンが押されたら true</returns>
    private bool DrawPhotoSettingEntry(int i)
    {
        SerializedProperty element = _photoSettingsProp.GetArrayElementAtIndex(i);

        SerializedProperty targetRendererProp    = element.FindPropertyRelative("targetRenderer");
        SerializedProperty photoTextureProp      = element.FindPropertyRelative("photoTexture");
        SerializedProperty materialSlotIndexProp = element.FindPropertyRelative("materialSlotIndex");
        SerializedProperty useEmissionProp       = element.FindPropertyRelative("useEmission");
        SerializedProperty emissionIntensityProp = element.FindPropertyRelative("emissionIntensity");

        // Renderer 名をヘッダーに表示（未設定なら「未設定」）
        string rendererName = targetRendererProp.objectReferenceValue != null
            ? targetRendererProp.objectReferenceValue.name
            : "未設定";

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // ===== ヘッダー行：折りたたみ三角 + 名前 + 削除ボタン =====
        EditorGUILayout.BeginHorizontal();

        // Foldout 三角ボタン（ラベルクリックでも開閉）
        _foldouts[i] = EditorGUILayout.Foldout(
            _foldouts[i],
            $"設定 [{i}]  —  {rendererName}",
            toggleOnLabelClick: true,   // ラベル全体をクリック可能に
            EditorStyles.foldoutHeader
        );

        // 削除ボタン
        GUIStyle deleteStyle = new GUIStyle(GUI.skin.button)
        {
            normal = { textColor = new Color(0.9f, 0.3f, 0.3f) }
        };
        if (GUILayout.Button("削除", deleteStyle, GUILayout.Width(50)))
        {
            _photoSettingsProp.DeleteArrayElementAtIndex(i);
            _foldouts.RemoveAt(i);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return true; // 削除したことを呼び出し元に通知
        }
        EditorGUILayout.EndHorizontal();

        // ===== 折りたたまれていたらここで終了 =====
        if (!_foldouts[i])
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
            return false;
        }

        EditorGUILayout.Space(2);

        // ===== 基本フィールド =====
        EditorGUILayout.PropertyField(targetRendererProp,    new GUIContent("対象 Renderer"));
        EditorGUILayout.PropertyField(photoTextureProp,      new GUIContent("写真テクスチャ"));
        EditorGUILayout.PropertyField(materialSlotIndexProp, new GUIContent("マテリアルスロット番号"));

        if (materialSlotIndexProp.intValue < 0)
            EditorGUILayout.HelpBox("スロット番号は 0 以上にしてください。", MessageType.Warning);

        EditorGUILayout.Space(4);

        // ===== 個別エミッション設定 =====
        // 共通設定 ON のときはグレーアウトして編集不可にする
        bool globalOn = _useGlobalEmissionProp.boolValue;

        using (new EditorGUI.DisabledGroupScope(globalOn))
        {
            if (globalOn)
            {
                // 共通設定 ON 時：共通設定が使われる旨を明示
                EditorGUILayout.HelpBox(
                    $"💡 共通エミッション設定が適用されます（強度: {_globalEmissionIntensityProp.floatValue:F2}）\n" +
                    "個別設定を使うには、上の「共通エミッション設定を使用する」をOFFにしてください。",
                    MessageType.None
                );
            }
            else
            {
                // 共通設定 OFF 時：個別設定を表示・編集可能
                EditorGUI.BeginChangeCheck();
                bool newUseEmission = EditorGUILayout.Toggle(
                    new GUIContent("💡 エミッション（自己発光）を有効にする"),
                    useEmissionProp.boolValue
                );
                if (EditorGUI.EndChangeCheck())
                    useEmissionProp.boolValue = newUseEmission;

                if (useEmissionProp.boolValue)
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.Slider(
                        emissionIntensityProp, 0f, 5f,
                        new GUIContent("発光強度", "値が大きいほど明るく発光します。\n1.0 = テクスチャそのままの明るさ。")
                    );

                    float intensity = emissionIntensityProp.floatValue;
                    string guide;
                    MessageType mt;
                    if      (intensity <= 0f) { guide = "強度 0：実質的に発光しません。";                          mt = MessageType.Warning; }
                    else if (intensity <= 1f) { guide = $"強度 {intensity:F2}：控えめな発光です。";               mt = MessageType.None;    }
                    else if (intensity <= 2f) { guide = $"強度 {intensity:F2}：明るめの発光です。";               mt = MessageType.None;    }
                    else                      { guide = $"強度 {intensity:F2}：非常に強い発光。HDRで飛びやすいので注意。"; mt = MessageType.Warning; }
                    EditorGUILayout.HelpBox(guide, mt);

                    EditorGUI.indentLevel--;
                }
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
        return false;
    }

    /// <summary>新しいエントリをデフォルト値でリストに追加します。</summary>
    private void AddNewEntry()
    {
        _photoSettingsProp.arraySize++;
        SerializedProperty newElement = _photoSettingsProp.GetArrayElementAtIndex(
            _photoSettingsProp.arraySize - 1
        );
        newElement.FindPropertyRelative("targetRenderer").objectReferenceValue = null;
        newElement.FindPropertyRelative("photoTexture").objectReferenceValue   = null;
        newElement.FindPropertyRelative("materialSlotIndex").intValue          = 0;
        newElement.FindPropertyRelative("useEmission").boolValue               = false;
        newElement.FindPropertyRelative("emissionIntensity").floatValue        = 1.0f;
        _foldouts.Add(true); // 新規追加は展開状態で追加
    }

    // =========================================================
    // メイン処理
    // =========================================================

    /// <summary>
    /// リストに登録されたすべての設定に対してマテリアルを生成・適用するメイン処理。
    /// </summary>
    private void GenerateAndApplyAll()
    {
        PhotoSettingManager manager = (PhotoSettingManager)target;

        if (manager.photoSettings == null || manager.photoSettings.Count == 0)
        {
            Debug.LogWarning("[PhotoSettingManager] 写真設定リストが空です。設定を追加してください。");
            EditorUtility.DisplayDialog("PhotoSettingManager",
                "写真設定リストが空です。\n設定を追加してから実行してください。", "OK");
            return;
        }

        string materialsFolderPath = EnsureMaterialsFolder(manager.baseSaveFolderPath);
        if (materialsFolderPath == null) return;

        int successCount = 0;
        int skipCount    = 0;

        for (int i = 0; i < manager.photoSettings.Count; i++)
        {
            PhotoSetting setting = manager.photoSettings[i];

            if (setting.targetRenderer == null)
            {
                Debug.LogWarning($"[PhotoSettingManager] 設定[{i}]: MeshRenderer が未設定のためスキップします。");
                skipCount++; continue;
            }
            if (setting.photoTexture == null)
            {
                Debug.LogWarning($"[PhotoSettingManager] 設定[{i}]: テクスチャが未設定のためスキップします。（Renderer: {setting.targetRenderer.name}）");
                skipCount++; continue;
            }

            Material[] currentMaterials = setting.targetRenderer.sharedMaterials;
            if (setting.materialSlotIndex < 0 || setting.materialSlotIndex >= currentMaterials.Length)
            {
                Debug.LogError(
                    $"[PhotoSettingManager] 設定[{i}]: マテリアルスロット番号 ({setting.materialSlotIndex}) が範囲外です。" +
                    $"（Renderer: {setting.targetRenderer.name}, マテリアル数: {currentMaterials.Length}）スキップします。"
                );
                skipCount++; continue;
            }

            Material material = GetOrCreateMaterial(setting.photoTexture, materialsFolderPath);
            if (material == null)
            {
                Debug.LogError($"[PhotoSettingManager] 設定[{i}]: マテリアルの生成・ロードに失敗しました。スキップします。");
                skipCount++; continue;
            }

            material.SetTexture("_MainTex", setting.photoTexture);

            // 共通設定 ON の場合は共通値を使い、OFF の場合は個別値を使う
            bool  applyEmission = manager.useGlobalEmission ? true              : setting.useEmission;
            float applyIntensity = manager.useGlobalEmission ? manager.globalEmissionIntensity : setting.emissionIntensity;
            ApplyEmissionSettings(material, setting.photoTexture, applyEmission, applyIntensity);

            EditorUtility.SetDirty(material);

            Material[] updatedMaterials = (Material[])currentMaterials.Clone();
            updatedMaterials[setting.materialSlotIndex] = material;
            setting.targetRenderer.sharedMaterials = updatedMaterials;
            EditorUtility.SetDirty(setting.targetRenderer);

            Debug.Log(
                $"[PhotoSettingManager] 設定[{i}]: 完了 ✔ " +
                $"Renderer={setting.targetRenderer.name}, スロット={setting.materialSlotIndex}, " +
                $"マテリアル={material.name}, エミッション={applyEmission}（強度: {applyIntensity:F2}）"
            );
            successCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string message = $"処理が完了しました。\n\n✔ 成功: {successCount} 件\n⏭ スキップ: {skipCount} 件";
        Debug.Log($"[PhotoSettingManager] {message}");
        EditorUtility.DisplayDialog("PhotoSettingManager - 完了", message, "OK");
    }

    /// <summary>
    /// マテリアルにエミッション設定を適用します。
    /// GlobalIlluminationFlags.None を設定し、ライトベイク時に他オブジェクトへ
    /// 光が伝播しないようにします。
    /// </summary>
    private void ApplyEmissionSettings(Material material, Texture2D texture, bool useEmission, float intensity)
    {
        if (useEmission)
        {
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", texture);

            // ガンマ補正でより直感的な明るさに変換
            float linearIntensity = Mathf.Pow(intensity, 2.2f);
            material.SetColor("_EmissionColor", new Color(linearIntensity, linearIntensity, linearIntensity, 1f));

            // ライトベイク時に他オブジェクトへ光を伝播させない
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
    }

    // =========================================================
    // ユーティリティ
    // =========================================================

    /// <summary>
    /// 保存先の "PhotoMaterials" フォルダが存在するか確認し、なければ作成します。
    /// </summary>
    private string EnsureMaterialsFolder(string baseFolderPath)
    {
        if (!AssetDatabase.IsValidFolder(baseFolderPath))
        {
            Debug.LogError($"[PhotoSettingManager] ベースフォルダが見つかりません: {baseFolderPath}");
            EditorUtility.DisplayDialog("エラー",
                $"指定されたベースフォルダが存在しません:\n{baseFolderPath}\n\n正しいフォルダパスを設定してください。", "OK");
            return null;
        }

        string materialsFolderPath = baseFolderPath.TrimEnd('/') + "/PhotoMaterials";
        if (!AssetDatabase.IsValidFolder(materialsFolderPath))
        {
            string guid = AssetDatabase.CreateFolder(baseFolderPath, "PhotoMaterials");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"[PhotoSettingManager] PhotoMaterials フォルダの作成に失敗しました。パス: {materialsFolderPath}");
                return null;
            }
            Debug.Log($"[PhotoSettingManager] フォルダを作成しました: {materialsFolderPath}");
        }

        return materialsFolderPath;
    }

    /// <summary>
    /// テクスチャのファイル名をベースにマテリアルを生成、または既存マテリアルをロードします。
    /// 例: "my_photo.png" → "mat_my_photo.mat"
    /// </summary>
    private Material GetOrCreateMaterial(Texture2D texture, string folderPath)
    {
        string texturePath          = AssetDatabase.GetAssetPath(texture);
        string textureFileNameNoExt = Path.GetFileNameWithoutExtension(texturePath);

        string materialName = "mat_" + textureFileNameNoExt;
        string materialPath = folderPath.TrimEnd('/') + "/" + materialName + ".mat";

        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (existingMaterial != null)
        {
            Debug.Log($"[PhotoSettingManager] 既存のマテリアルを再利用します: {materialPath}");
            return existingMaterial;
        }

        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null)
        {
            Debug.LogError("[PhotoSettingManager] \"Standard\" シェーダーが見つかりません。");
            return null;
        }

        Material newMaterial = new Material(standardShader) { name = materialName };
        AssetDatabase.CreateAsset(newMaterial, materialPath);
        Debug.Log($"[PhotoSettingManager] 新規マテリアルを作成しました: {materialPath}");

        return newMaterial;
    }

    // =========================================================
    // ドラッグ＆ドロップ
    // =========================================================

    /// <summary>
    /// 複数メッシュを一括登録できるドラッグ＆ドロップ受け取りエリアを描画します。
    /// </summary>
    private void DrawDragAndDropArea()
    {
        EditorGUILayout.Space(4);

        // ===== Inspectorロック手順ガイド =====
        GUIStyle guideBoxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
        EditorGUILayout.BeginVertical(guideBoxStyle);

        GUIStyle guideTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
            normal   = { textColor = new Color(1.0f, 0.85f, 0.3f) }
        };
        EditorGUILayout.LabelField("⚠ 複数選択でドロップする前に：Inspectorをロックしてください", guideTitleStyle);
        EditorGUILayout.Space(2);

        GUIStyle guideStepStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            richText = true,
            fontSize = 11,
            normal   = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };
        EditorGUILayout.LabelField(
            "① このInspectorウィンドウ右上の <b>南京錠アイコン（🔒）</b> をクリックしてロック\n" +
            "② Hierarchyで登録したいメッシュを <b>複数選択</b>（Ctrl / Cmd + クリック）\n" +
            "③ 選択したメッシュを下のドロップゾーンにドラッグ＆ドロップ\n" +
            "④ 完了したら南京錠をクリックして <b>ロック解除</b>",
            guideStepStyle
        );
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);

        // ===== ドロップゾーン =====
        Rect dropRect = GUILayoutUtility.GetRect(0f, 52f, GUILayout.ExpandWidth(true));

        bool isDraggingOver = dropRect.Contains(Event.current.mousePosition)
                              && (DragAndDrop.visualMode == DragAndDropVisualMode.Copy
                                  || DragAndDrop.visualMode == DragAndDropVisualMode.Link
                                  || DragAndDrop.visualMode == DragAndDropVisualMode.Generic);

        Color bgColor     = isDraggingOver ? new Color(0.2f, 0.5f, 1.0f, 0.25f) : new Color(0.5f, 0.5f, 0.5f, 0.1f);
        Color borderColor = isDraggingOver ? new Color(0.2f, 0.5f, 1.0f, 0.9f)  : new Color(0.5f, 0.5f, 0.5f, 0.5f);

        EditorGUI.DrawRect(dropRect, bgColor);
        EditorGUI.DrawRect(new Rect(dropRect.x,                       dropRect.y,                        dropRect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(dropRect.x,                       dropRect.y + dropRect.height - 1f, dropRect.width, 1f), borderColor);
        EditorGUI.DrawRect(new Rect(dropRect.x,                       dropRect.y,                        1f, dropRect.height), borderColor);
        EditorGUI.DrawRect(new Rect(dropRect.x + dropRect.width - 1f, dropRect.y,                        1f, dropRect.height), borderColor);

        GUIStyle dropLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = true,
            normal    = { textColor = isDraggingOver ? new Color(0.3f, 0.6f, 1.0f) : Color.gray }
        };
        GUI.Label(dropRect, "ここに MeshRenderer を持つ GameObject を\nドラッグ＆ドロップして一括追加", dropLabelStyle);

        Event evt = Event.current;
        switch (evt.type)
        {
            case EventType.DragUpdated:
                if (dropRect.Contains(evt.mousePosition) && HasValidDragObjects())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.Use();
                }
                break;

            case EventType.DragPerform:
                if (dropRect.Contains(evt.mousePosition) && HasValidDragObjects())
                {
                    DragAndDrop.AcceptDrag();
                    RegisterDraggedObjects();
                    evt.Use();
                    Repaint();
                }
                break;
        }
    }

    /// <summary>ドラッグ中のオブジェクトに有効な MeshRenderer が含まれているか確認します。</summary>
    private bool HasValidDragObjects()
    {
        foreach (Object dragObj in DragAndDrop.objectReferences)
        {
            if (dragObj is GameObject go && go.GetComponent<MeshRenderer>() != null) return true;
            if (dragObj is MeshRenderer) return true;
        }
        return false;
    }

    /// <summary>ドロップされた GameObject / MeshRenderer をリストに一括登録します。</summary>
    private void RegisterDraggedObjects()
    {
        serializedObject.Update();

        int addedCount = 0, skippedCount = 0;

        foreach (Object dragObj in DragAndDrop.objectReferences)
        {
            MeshRenderer renderer = null;

            if (dragObj is GameObject go)
            {
                renderer = go.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    Debug.LogWarning($"[PhotoSettingManager] 「{go.name}」は MeshRenderer を持っていないためスキップします。");
                    skippedCount++; continue;
                }
            }
            else if (dragObj is MeshRenderer mr)
            {
                renderer = mr;
            }
            else
            {
                skippedCount++; continue;
            }

            if (IsAlreadyRegistered(renderer))
            {
                Debug.LogWarning($"[PhotoSettingManager] 「{renderer.name}」はすでにリストに登録されているためスキップします。");
                skippedCount++; continue;
            }

            _photoSettingsProp.arraySize++;
            SerializedProperty newEntry = _photoSettingsProp.GetArrayElementAtIndex(_photoSettingsProp.arraySize - 1);
            newEntry.FindPropertyRelative("targetRenderer").objectReferenceValue = renderer;
            newEntry.FindPropertyRelative("photoTexture").objectReferenceValue   = null;
            newEntry.FindPropertyRelative("materialSlotIndex").intValue          = 0;
            newEntry.FindPropertyRelative("useEmission").boolValue               = false;
            newEntry.FindPropertyRelative("emissionIntensity").floatValue        = 1.0f;
            _foldouts.Add(true);

            Debug.Log($"[PhotoSettingManager] 追加: {renderer.name}");
            addedCount++;
        }

        serializedObject.ApplyModifiedProperties();

        if (addedCount > 0 || skippedCount > 0)
            Debug.Log($"[PhotoSettingManager] ドロップ登録完了 — 追加: {addedCount} 件 / スキップ: {skippedCount} 件");
    }

    /// <summary>指定した MeshRenderer がすでにリストに登録されているか確認します。</summary>
    private bool IsAlreadyRegistered(MeshRenderer renderer)
    {
        for (int i = 0; i < _photoSettingsProp.arraySize; i++)
        {
            Object existing = _photoSettingsProp.GetArrayElementAtIndex(i)
                                               .FindPropertyRelative("targetRenderer").objectReferenceValue;
            if (existing == renderer) return true;
        }
        return false;
    }
}
