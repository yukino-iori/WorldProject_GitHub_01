// CandleEmissionTarget.cs
//
// 1つの「ろうそく」に対応するシンプルなちらつき同期コンポーネント。
// 基本的に入力するのは以下の4つだけ:
//   1. Flame Material  : CandleFlameのマテリアル
//   2. Target Renderers: Emissionをちらつかせたい対象のRenderer(複数可)
//   3. Emission Min     : ちらつきの下限の色
//   4. Emission Max     : ちらつきの上限の色
//
// ろうそくが複数ある場合は、このコンポーネントを対象の数だけ
// (同じGameObjectに複数アタッチしてもOK)追加してください。
// インデックス番号などを管理する必要はありません。
//
// ---- 導入方法 ----
// 1. このスクリプトをUdonSharpスクリプトとして追加
// 2. ろうそくの炎のGameObject(またはその近く)にアタッチ
//    → 何も指定しなければ、このGameObjectの位置がそのまま
//      ちらつきパターンの「個体差」の元になります(ろうそくごとに自動でズレる)
// 3. Flame Material / Target Renderers / Emission Min / Emission Max を設定するだけ
//
// 複数のろうそくを1つの管理用GameObjectにまとめたい場合は、
// 同じGameObjectにこのコンポーネントを複数追加し、
// 必要なら各コンポーネントの「Flame Origin (詳細設定)」に
// それぞれの炎のTransformを個別に指定してください
// (指定しないと全部同じ位置になり、ちらつきが同期してしまいます)

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class CandleEmissionTarget : UdonSharpBehaviour
{
    [Header("炎のマテリアル")]
    [Tooltip("CandleFlameのマテリアル。_FlickerSpeed/_FlickerScaleを自動で読み取ります")]
    public Material flameMaterial;

    [Header("対象Renderer")]
    [Tooltip("EmissionをちらつかせたいRenderer(複数指定可)")]
    public Renderer[] targetRenderers;

    [Header("Emissionの範囲")]
    public Color emissionMin = new Color(0.6f, 0.2f, 0f, 1f);
    public Color emissionMax = new Color(1.5f, 0.9f, 0.3f, 1f);

    [Header("詳細設定 (通常は変更不要)")]
    [Tooltip("空欄の場合、このGameObject自身の位置を炎の位置として使います")]
    public Transform flameOriginOverride;
    public string emissionPropertyName = "_EmissionColor";
    public float defaultFlickerSpeed = 3f;
    public float defaultFlickerScale = 1f;

    float flickerSpeed;
    float flickerScale;
    Material[] matInstances;
    Transform flameOrigin;

    void Start()
    {
        flickerSpeed = defaultFlickerSpeed;
        flickerScale = defaultFlickerScale;

        if (flameMaterial != null)
        {
            if (flameMaterial.HasProperty("_FlickerSpeed"))
            {
                flickerSpeed = flameMaterial.GetFloat("_FlickerSpeed");
            }
            if (flameMaterial.HasProperty("_FlickerScale"))
            {
                flickerScale = flameMaterial.GetFloat("_FlickerScale");
            }
        }

        flameOrigin = (flameOriginOverride != null) ? flameOriginOverride : transform;

        if (targetRenderers != null && targetRenderers.Length > 0)
        {
            matInstances = new Material[targetRenderers.Length];
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                if (targetRenderers[i] != null)
                {
                    matInstances[i] = targetRenderers[i].material;
                }
            }
        }
    }

    void Update()
    {
        if (matInstances == null || flameOrigin == null) return;

        float instOffset = GetInstanceOffset(flameOrigin.position);
        float t = Time.time + instOffset;

        // -1~1のノイズを0~1に正規化し、Min~Maxの間を補間する
        float noise = PNoise(new Vector2(t * flickerSpeed, instOffset * 0.05f) * flickerScale);
        float flicker01 = Mathf.Clamp01(0.5f + 0.5f * noise);

        Color result = Color.Lerp(emissionMin, emissionMax, flicker01);

        for (int i = 0; i < matInstances.Length; i++)
        {
            if (matInstances[i] != null)
            {
                matInstances[i].SetColor(emissionPropertyName, result);
            }
        }
    }

    // ---- CandleFlame.shader と同じPerlinノイズ計算(C#移植版) ----

    float Frac(float x)
    {
        return x - Mathf.Floor(x);
    }

    float GetInstanceOffset(Vector3 worldPos)
    {
        float d = worldPos.x * 12.9898f + worldPos.z * 78.233f;
        float s = Mathf.Sin(d) * 43758.5453f;
        return Frac(s) * 100f;
    }

    Vector2 Hash2(Vector2 p)
    {
        float x = p.x * 127.1f + p.y * 311.7f;
        float y = p.x * 269.5f + p.y * 183.3f;
        float rx = Frac(Mathf.Sin(x) * 43758.5453123f) * 2f - 1f;
        float ry = Frac(Mathf.Sin(y) * 43758.5453123f) * 2f - 1f;
        return new Vector2(rx, ry);
    }

    float PNoise(Vector2 p)
    {
        float ix = Mathf.Floor(p.x);
        float iy = Mathf.Floor(p.y);
        float fx = p.x - ix;
        float fy = p.y - iy;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);

        Vector2 h00 = Hash2(new Vector2(ix + 0f, iy + 0f));
        Vector2 h10 = Hash2(new Vector2(ix + 1f, iy + 0f));
        Vector2 h01 = Hash2(new Vector2(ix + 0f, iy + 1f));
        Vector2 h11 = Hash2(new Vector2(ix + 1f, iy + 1f));

        float a = h00.x * (fx - 0f) + h00.y * (fy - 0f);
        float b = h10.x * (fx - 1f) + h10.y * (fy - 0f);
        float c = h01.x * (fx - 0f) + h01.y * (fy - 1f);
        float d = h11.x * (fx - 1f) + h11.y * (fy - 1f);

        float ab = Mathf.Lerp(a, b, ux);
        float cd = Mathf.Lerp(c, d, ux);
        return Mathf.Lerp(ab, cd, uy);
    }
}
