// LightShaftCone.shader
// VRChat向け スポットライト光筋(ライトシャフト)シェーダー ver7
//
// 変更点(ver6 -> ver7):
//  - ノイズ機能(NoiseStreaks関連)を全て削除
//  - Fresnelの Power / Strength を削除(効果がほぼ無かったため)。固定値をシェーダー内に埋め込み、
//    EdgeFade と EdgeBlur の2つだけ調整できるようにした
//  - EdgeFade の効き幅を広げた(より強く効かせられるように)
//  - Length の向きのバグを修正(smoothstepの引数の順序が逆になっていたのが原因)
//
// BrightnessGradientについて:
//   これは「先端(光源に近い上側)は明るく、末端(照射面に近い下側)は暗く」という
//   自然なグラデーションをつけるための項目です。
//   ・Bottom Brightness: 末端側(下側)の明るさ。上げるほど下まで明るいまま、下げるほど下側が暗くなる
//   ・Gradient Power: 明暗が切り替わるカーブの形。上げるほど先端付近だけ明るくパキッと落ち、
//                     下げるほどなだらかに変化する
//
// 旧バージョンの値 -> 0.5のときにこの値になるようリマップ:
//   Intensity            = 0.07
//   TipFadeRange         = 1
//   BottomBrightness     = 1
//   GradientPower        = 6
//   FresnelEdgeFade      = 1
//   EdgeBlur             = 1
//   DepthFadeDistance    = 5
//
// メッシュの前提:
//  - 円錐(or 円柱)、先端(光源側)頂点 UV.v = 0、末端(照射面側)頂点 UV.v = 1
//  - 円周方向 UV.u = 0〜1
//  - ローカル軸は +Y 方向
//  - SmoothNormalを使う場合は、先端(頂点)がローカル原点(0,0,0)にあること

Shader "VRChat/LightShaftCone"
{
    Properties
    {
        [HDR]_Color ("Light Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,1)) = 0.5

        [Header(TipFade)]
        _TipFadeRange ("Tip Fade Range", Range(0,1)) = 0.5

        [Header(BrightnessGradient)]
        _BottomBrightness ("Bottom Brightness (末端側の明るさ)", Range(0,1)) = 0.5
        _GradientPower ("Gradient Power (明暗の切り替わり方)", Range(0,1)) = 0.5

        [Header(FresnelRim)]
        _FresnelEdgeFade ("Fresnel Edge Fade (先端/末端でフレネルを消す強さ)", Range(0,1)) = 0.5
        _EdgeBlur ("Edge Blur (シルエットのぼかし)", Range(0,1)) = 0.5

        [Header(SmoothNormal)]
        [Toggle]_SmoothNormal ("Use Slant Aware Smooth Normal", Float) = 0

        [Header(DepthFade)]
        _DepthFadeDistance ("Depth Fade Distance", Range(0,1)) = 0.5

        [Header(Length)]
        _Length ("Length (shorten the shaft)", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "VRCFallback" = "Hidden"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One One // 加算合成
        Fog { Color (0,0,0,0) }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 localPos    : TEXCOORD3;
                float4 screenPos   : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };

            fixed4 _Color;
            float _Intensity;

            float _TipFadeRange;

            float _BottomBrightness;
            float _GradientPower;

            float _FresnelEdgeFade;
            float _EdgeBlur;

            float _SmoothNormal;

            float _DepthFadeDistance;

            float _Length;

            // Fresnelの見た目にほぼ効かなかったので固定値に(必要ならここの数値を直接書き換えてください)
            static const float FRESNEL_POWER = 8.0;
            static const float FRESNEL_STRENGTH = 5.0;

            // --- 0〜1スライダーを実際の値にリマップするヘルパー ---
            // s01=0.5 のとき center の値になり、0.5から離れるほど指数的に(倍々に)変化する
            float RemapExp(float center, float s01, float stops)
            {
                return center * exp2((s01 - 0.5) * 2.0 * stops);
            }

            v2f vert (appdata v)
            {
                v2f o;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.localPos = v.vertex.xyz;

                o.screenPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.screenPos.z);

                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                // --- スライダー値を実際の値にリマップ ---
                float intensity          = RemapExp(0.07,  _Intensity, 3.0);
                float tipFadeRange       = RemapExp(1.0,   _TipFadeRange, 2.0);
                float bottomBrightness   = RemapExp(1.0,   _BottomBrightness, 2.0);
                float gradientPower      = RemapExp(6.0,   _GradientPower, 2.0);
                float fresnelEdgeFade    = RemapExp(1.0,   _FresnelEdgeFade, 4.0); // 前より広めのレンジ
                float edgeBlur           = RemapExp(1.0,   _EdgeBlur, 2.0);
                float depthFadeDistance  = RemapExp(5.0,   _DepthFadeDistance, 2.0);

                // v01(元): 0=先端(光源側) -> 1=末端(照射面側)
                float v01raw = saturate(i.uv.y);

                // --- Length: 光源側に圧縮していく処理 ---
                // 単純にアルファを切るのではなく、先端フェード/明暗グラデーション/フレネルが
                // 使う座標(v01)自体を圧縮することで、シャフト全体がそのまま短くなって見えるようにする。
                // 反転済み: 光源側を固定点として、そこから縮んでいくようにしている。
                // もしまだ向きが逆であれば、次の1行の "1.0 - v01raw" と "v01raw" を入れ替えてください。
                float distFromLight = 1.0 - v01raw;
                float lengthSafe = max(_Length, 0.0001);
                float compressedDist = distFromLight / lengthSafe;
                float v01 = 1.0 - saturate(compressedDist); // 圧縮後の座標。_Length=1のとき元のv01と一致する

                // 圧縮した範囲より先(元のジオメトリの余った部分)は完全に隠す
                float lengthVisible = 1.0 - smoothstep(1.0, 1.05, compressedDist);

                // --- 先端フェード ---
                float tipFade = smoothstep(0.0, max(tipFadeRange, 0.0001), v01);

                // --- 明るさのグラデーション(先端上=明るい -> 末端下=暗い) ---
                float gradT = pow(saturate(1.0 - v01), max(gradientPower, 0.0001));
                float brightnessGradient = lerp(bottomBrightness, 1.0, gradT);

                // --- 法線の準備(頂点法線 or スラントを考慮した滑らかな法線) ---
                float3 vertexNormalWorld = normalize(i.worldNormal);

                float3 radialLocal = normalize(float3(i.localPos.x, 0.0, i.localPos.z) + 1e-5);
                float3 tangentCircum = normalize(cross(float3(0,1,0), radialLocal));
                float3 slantDir = normalize(i.localPos + 1e-5);
                float3 coneNormalLocal = normalize(cross(tangentCircum, slantDir));
                float3 coneNormalWorld = normalize(mul((float3x3)unity_ObjectToWorld, coneNormalLocal));

                float3 normal = lerp(vertexNormalWorld, coneNormalWorld, _SmoothNormal);
                normal *= (facing > 0.0) ? 1.0 : -1.0;

                // --- フレネル(輪郭のハイライト) ---
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndotv = saturate(dot(normal, viewDir));
                float fresnel = pow(1.0 - ndotv, FRESNEL_POWER);
                float fresnelRaw = 1.0 + fresnel * FRESNEL_STRENGTH;

                float edgeDist = min(v01, 1.0 - v01);
                float fresnelPositionMask = smoothstep(0.0, max(fresnelEdgeFade, 0.0001), edgeDist);
                float fresnelMask = lerp(1.0, fresnelRaw, fresnelPositionMask);

                // --- 輪郭(シルエット)のぼかし ---
                float edgeBlurMask = smoothstep(0.0, max(edgeBlur, 0.0001), ndotv);

                // --- Depthを使った、他オブジェクトとの交差フェード ---
                float sceneEyeDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                float fragEyeDepth = i.screenPos.z;
                float invFade = 1.0 / max(depthFadeDistance, 0.0001);
                float depthFade = saturate((sceneEyeDepth - fragEyeDepth) * invFade);

                float finalMask = tipFade * fresnelMask * edgeBlurMask * depthFade * lengthVisible;

                fixed3 col = _Color.rgb * intensity * brightnessGradient * finalMask;

                fixed4 result = fixed4(col, 1.0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, result, fixed4(0,0,0,0));
                return result;
            }
            ENDCG
        }
    }

    FallBack Off
}
