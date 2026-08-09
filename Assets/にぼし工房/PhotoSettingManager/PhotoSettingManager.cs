using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 1枚の写真（テクスチャ）に対する設定を保持するデータクラス。
/// MeshRenderer、テクスチャ、マテリアルスロットのインデックスをまとめて管理します。
/// </summary>
[Serializable]
public class PhotoSetting
{
    /// <summary>テクスチャを適用する対象の MeshRenderer</summary>
    public MeshRenderer targetRenderer;

    /// <summary>割り当てる写真のテクスチャ</summary>
    public Texture2D photoTexture;

    /// <summary>
    /// 対象メッシュの何番目のマテリアルスロットに適用するか（0始まり）。
    /// 初期値は 0。
    /// </summary>
    public int materialSlotIndex = 0;

    /// <summary>
    /// エミッション（自己発光）を有効にするかどうか。
    /// true の場合、Emission カラーに同じ写真テクスチャを設定し、明るさを調整できます。
    /// </summary>
    public bool useEmission = false;

    /// <summary>
    /// エミッションの強度（明るさ）。
    /// 0.0 = 発光なし、1.0 = 等倍、2.0 以上 = 明るく発光。
    /// HDR 乗算として _EmissionColor に適用されます。
    /// </summary>
    [Range(0f, 5f)]
    public float emissionIntensity = 1.0f;
}

/// <summary>
/// VRChatワールド制作用 写真（テクスチャ）一括設定・マテリアル自動生成ツール。
/// このコンポーネントはデータの保持に徹します。
/// 実際の処理・UIは PhotoSettingManagerEditor（Editorクラス）が担います。
/// </summary>
public class PhotoSettingManager : MonoBehaviour
{
    /// <summary>
    /// 写真設定のリスト。
    /// インスペクター上でカスタムエディタにより表示・編集されます。
    /// </summary>
    public List<PhotoSetting> photoSettings = new List<PhotoSetting>();

    /// <summary>
    /// マテリアルの保存先となるベースフォルダパス。
    /// 例: "Assets/MyWorld"
    /// 実際には、このフォルダ直下の "PhotoMaterials" フォルダに保存されます。
    /// </summary>
    public string baseSaveFolderPath = "Assets";

    // ===== 共通エミッション設定 =====
    // useGlobalEmission が true のとき、リスト内すべての設定に
    // globalEmissionIntensity が一括適用されます。
    // 各エントリ個別の useEmission / emissionIntensity は無視されます。

    /// <summary>
    /// 共通エミッション設定を有効にするかどうか。
    /// true の場合、全エントリに globalEmissionIntensity が適用されます。
    /// </summary>
    public bool useGlobalEmission = false;

    /// <summary>
    /// 共通エミッション強度（0〜5）。
    /// useGlobalEmission が true のときにすべてのエントリへ一括適用されます。
    /// </summary>
    [Range(0f, 5f)]
    public float globalEmissionIntensity = 1.0f;
}
