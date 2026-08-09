using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 昼夜のライトマップとリフレクションプローブを切り替えるシステム (v2)
/// MaterialPropertyBlockを利用して直接テクスチャを上書きする方式。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class LightmapSwitcher : UdonSharpBehaviour
{
    [Header("Targets (自動登録ツールから登録)")]
    [Tooltip("ライトマップを切り替える対象のRenderer配列")]
    public Renderer[] targetRenderers;
    [Tooltip("切り替える対象のReflectionProbe配列")]
    public ReflectionProbe[] targetProbes;

    [Header("Lights & PostProcess")]
    [Tooltip("昼用のDirectional Lightオブジェクト")]
    public GameObject dayDirectionalLight;
    [Tooltip("夜用のDirectional Lightオブジェクト")]
    public GameObject nightDirectionalLight;

    [Tooltip("昼用のAvatarDirectional Lightオブジェクト")]
    public GameObject dayAvatarDirectionalLight;
    [Tooltip("夜用のAvatarDirectional Lightオブジェクト")]
    public GameObject nightAvatarDirectionalLight;

    [Tooltip("昼用のPostProcessVolumeオブジェクト")]
    public GameObject dayPostProcess;
    [Tooltip("夜用のPostProcessVolumeオブジェクト")]
    public GameObject nightPostProcess;

    [Header("Skybox")]
    public Material daySkybox;
    public Material nightSkybox;

    [Header("Daytime Textures (手動登録)")]
    [Tooltip("昼のライトマップ用テクスチャ。1つだけ登録した場合は全てのRendererに同じテクスチャが適用されます。")]
    public Texture2D[] dayLightmaps;
    [Tooltip("昼のReflectionProbe用Cubemap。1つだけ登録した場合は全てのProbeに適用されます。")]
    public Cubemap[] dayReflectionCubemaps;

    [Header("Nighttime Textures (手動登録)")]
    [Tooltip("夜のライトマップ用テクスチャ。1つだけ登録した場合は全てのRendererに同じテクスチャが適用されます。")]
    public Texture2D[] nightLightmaps;
    [Tooltip("夜のReflectionProbe用Cubemap。1つだけ登録した場合は全てのProbeに適用されます。")]
    public Cubemap[] nightReflectionCubemaps;

    [Header("Settings")]
    public bool startAsDay = true;
    public bool syncAcrossNetwork = true;

    [UdonSynced]
    private bool isDay = true;

    // MaterialPropertyBlockのキャッシュ
    private MaterialPropertyBlock _propBlock;

    void Start()
    {
        _propBlock = new MaterialPropertyBlock();
        isDay = startAsDay;
        Apply(isDay);
    }

    public override void OnDeserialization()
    {
        Apply(isDay);
    }

    public void Toggle()
    {
        if (isDay) SwitchToNight();
        else SwitchToDay();
    }

    public void DAYNIGHTSWAP()
    {
        Toggle();
    }

    public void SwitchToDay()
    {
        if (syncAcrossNetwork) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        isDay = true;
        if (syncAcrossNetwork) RequestSerialization();
        Apply(true);
    }

    public void SwitchToNight()
    {
        if (syncAcrossNetwork) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        isDay = false;
        if (syncAcrossNetwork) RequestSerialization();
        Apply(false);
    }

    private void Apply(bool day)
    {
        if (_propBlock == null)
        {
            _propBlock = new MaterialPropertyBlock();
        }

        // ---- RendererへのMaterialPropertyBlock適用 ----
        if (targetRenderers != null)
        {
            Texture2D[] currentLightmaps = day ? dayLightmaps : nightLightmaps;
            bool hasLightmaps = currentLightmaps != null && currentLightmaps.Length > 0;
            bool isSingleLightmap = hasLightmaps && currentLightmaps.Length == 1;

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer r = targetRenderers[i];
                if (r == null) continue;

                Texture2D tex = null;
                if (hasLightmaps)
                {
                    tex = isSingleLightmap ? currentLightmaps[0] : (i < currentLightmaps.Length ? currentLightmaps[i] : null);
                }

                if (tex != null)
                {
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetTexture("unity_Lightmap", tex);
                    r.SetPropertyBlock(_propBlock);
                }
            }
        }

        // ---- Reflection Probeの差し替え ----
        if (targetProbes != null)
        {
            Cubemap[] currentCubemaps = day ? dayReflectionCubemaps : nightReflectionCubemaps;
            bool hasCubemaps = currentCubemaps != null && currentCubemaps.Length > 0;
            bool isSingleCubemap = hasCubemaps && currentCubemaps.Length == 1;

            for (int i = 0; i < targetProbes.Length; i++)
            {
                ReflectionProbe p = targetProbes[i];
                if (p == null) continue;

                Cubemap cube = null;
                if (hasCubemaps)
                {
                    cube = isSingleCubemap ? currentCubemaps[0] : (i < currentCubemaps.Length ? currentCubemaps[i] : null);
                }

                if (cube != null)
                {
                    p.customBakedTexture = cube;
                }
            }
        }

        // ---- Directional Light と PostProcess の切り替え ----
        if (dayDirectionalLight != null) dayDirectionalLight.SetActive(day);
        if (nightDirectionalLight != null) nightDirectionalLight.SetActive(!day);

        if (dayAvatarDirectionalLight != null) dayAvatarDirectionalLight.SetActive(day);
        if (nightAvatarDirectionalLight != null) nightAvatarDirectionalLight.SetActive(!day);

        if (dayPostProcess != null) dayPostProcess.SetActive(day);
        if (nightPostProcess != null) nightPostProcess.SetActive(!day);

        // ---- Skybox の切り替え ----
        Material sky = day ? daySkybox : nightSkybox;
        if (sky != null)
        {
            RenderSettings.skybox = sky;
        }
    }

}
