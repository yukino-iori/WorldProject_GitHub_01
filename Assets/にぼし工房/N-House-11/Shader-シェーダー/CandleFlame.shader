Shader "VRC/CandleFlame"
{
    Properties
    {
        [Header(Mask Texture Optional)]
        _MainTex ("マスクテクスチャ(白黒/任意)", 2D) = "white" {}
        _UseTexture ("テクスチャマスクを使う(1)/プロシージャル形状(0)", Range(0,1)) = 0

        // ------------------------------------------------------------
        // 以下の 0〜1 スライダーはすべて「正規化ノブ」です。
        // 0.5 のとき、旧バージョンで見た目を合わせ込んだ基準値になります。
        // 0 / 1 に振るとそこから大きく変化するように、
        // シェーダー内部で下限・基準値・上限の3点をなめらかに補間しています。
        // ------------------------------------------------------------

        [Header(FlameShape)]
        _FlameWidth ("太さ", Range(0, 1)) = 0.5
        _TipSharpness ("先端の尖り具合", Range(0, 1)) = 0.5
        _EdgeSoftness ("輪郭のぼかし", Range(0, 1)) = 0.5
        _BaseWidth ("根本の絞り込み", Range(0, 1)) = 0.5
        _NoiseScale ("大きいノイズの細かさ", Range(0, 1)) = 0.5
        _NoiseStretch ("大きいノイズの縦方向の伸び", Range(0, 1)) = 0.5
        _DistortionStrength ("大きいノイズの歪み強度", Range(0, 1)) = 0.5
        _NoiseScale2 ("細かいノイズの細かさ", Range(0, 1)) = 0.5
        _DetailStrength ("細かいノイズの歪み強度", Range(0, 1)) = 0.5
        _NoiseSpeed ("ノイズのスクロール速度", Range(0, 1)) = 0.5

        [Header(Sway Motion)]
        _SwayAmount ("揺れの大きさ", Range(0, 1)) = 0.5
        _SwaySpeed ("揺れの速さ", Range(0, 1)) = 0.5
        _SwayFrequency ("揺れの周波数", Range(0, 1)) = 0.5

        [Header(Flicker Perlin Brightness)]
        _FlickerSpeed ("ちらつきの速さ", Range(0, 1)) = 0.5
        _FlickerIntensity ("ちらつきの強さ", Range(0, 1)) = 0.5
        _FlickerScale ("ちらつきノイズのスケール", Range(0, 1)) = 0.5

        [Header(Color)]
        [Toggle] _UseColorRamp ("Color Rampを使う(0=下の3色 / 1=Ramp)", Float) = 0
        _ColorRamp ("Color Ramp Texture (横方向に下→上のグラデーション)", 2D) = "white" {}
        _RampOffset ("Rampのずらし", Range(-0.5, 0.5)) = 0.0
        _RampNoiseInfluence ("Rampへのノイズの影響度", Range(0, 1)) = 0.15
        _ColorBase ("根本の色(Ramp未使用時)", Color) = (1, 0.9, 0.3, 1)
        _ColorMid ("中間の色(Ramp未使用時)", Color) = (1, 0.5, 0.05, 1)
        _ColorTip ("先端の色(Ramp未使用時)", Color) = (0.6, 0.1, 0.6, 1)
        _Brightness ("明るさ倍率(Bloom用)", Range(0.5, 8)) = 1.5
        _AlphaPower ("透明度のカーブ", Range(0.1, 5)) = 1.0

        [Header(Render Settings Position)]
        _FlameOffsetX ("横位置オフセット", Range(-0.5, 0.5)) = 0
        _FlameOffsetY ("縦位置オフセット", Range(-0.5, 0.5)) = 0
        _FlameSize ("大きさ", Range(0, 1)) = 0.5

        [Header(Render Settings Blend Mode)]
        // 内部でアルファ事前乗算(premultiplied alpha)しているため、
        // Alpha合成は [One, OneMinusSrcAlpha] / Additive合成は [One, One] にすると
        // どちらでも炎の形(アルファマスク)が正しく反映される
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend (Alpha:One / Additive:One)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend (Alpha:OneMinusSrcAlpha / Additive:One)", Float) = 6
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "VRCFallback"="Unlit"
        }
        LOD 100
        Cull Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _UseTexture;

            float _FlameWidth, _TipSharpness, _EdgeSoftness, _BaseWidth;
            float _NoiseScale, _NoiseStretch, _DistortionStrength;
            float _NoiseScale2, _DetailStrength, _NoiseSpeed;
            float _SwayAmount, _SwaySpeed, _SwayFrequency;
            float _FlickerSpeed, _FlickerIntensity, _FlickerScale;

            float _FlameOffsetX, _FlameOffsetY, _FlameSize;

            float _UseColorRamp;
            sampler2D _ColorRamp;
            float _RampOffset, _RampNoiseInfluence;
            float4 _ColorBase, _ColorMid, _ColorTip;
            float _Brightness, _AlphaPower;

            // ---- 0〜1の正規化ノブを 下限/基準値/上限 の3点でなめらかに補間する ----
            // norm=0 -> lo / norm=0.5 -> mid(旧バージョンの見た目) / norm=1 -> hi
            float Remap01(float norm, float lo, float mid, float hi)
            {
                float n = saturate(norm);
                return (n < 0.5)
                    ? lerp(lo, mid, n * 2.0)
                    : lerp(mid, hi, (n - 0.5) * 2.0);
            }

            // ---- 疑似Perlin(勾配)ノイズ ----
            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float pnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = dot(hash2(i + float2(0, 0)), f - float2(0, 0));
                float b = dot(hash2(i + float2(1, 0)), f - float2(1, 0));
                float c = dot(hash2(i + float2(0, 1)), f - float2(0, 1));
                float d = dot(hash2(i + float2(1, 1)), f - float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // オクターブを重ねたFBM(大きいうねりに使用)
            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    v += amp * pnoise(p);
                    p *= 2.0;
                    amp *= 0.5;
                }
                return v;
            }

            // オブジェクトごとに異なる値を返す(同じシェーダーの炎同士が同期しないようにする)
            float GetInstanceOffset()
            {
                float3 originWS = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                return frac(sin(dot(originWS.xz, float2(12.9898, 78.233))) * 43758.5453) * 100.0;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float t = _Time.y + GetInstanceOffset();

                float swayAmount = Remap01(_SwayAmount, 0.0, 0.001, 0.3);
                float swaySpeed = Remap01(_SwaySpeed, 0.0, 5.0, 15.0);
                float swayFrequency = Remap01(_SwayFrequency, 0.0, 10.0, 25.0);
                float flameSize = max(Remap01(_FlameSize, 0.3, 1.0, 3.0), 0.01);

                // オフセット + 拡大縮小を高さの基準にも反映(frag側と同じ空間で揺れを計算するため)
                float2 pivot = float2(0.5, 0.0);
                float2 shiftedUV = v.uv - float2(_FlameOffsetX, _FlameOffsetY);
                float2 scaledUV = pivot + (shiftedUV - pivot) / flameSize;
                float heightFactor = saturate(scaledUV.y);

                // 上ほど大きく揺れる。ノイズとsin波を混ぜて自然な揺れに
                float swayNoise = pnoise(float2(t * swaySpeed, heightFactor * swayFrequency));
                float sway = sin(t * swaySpeed + heightFactor * swayFrequency) * 0.5 + swayNoise * 0.5;
                v.vertex.x += sway * swayAmount * heightFactor * heightFactor;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float instOffset = GetInstanceOffset();
                float t = _Time.y + instOffset;

                // ---- 0〜1ノブを実際の計算値に変換(0.5で旧バージョンの見た目) ----
                float flameWidth = Remap01(_FlameWidth, 0.0, 0.34, 1.2);
                float tipSharpness = Remap01(_TipSharpness, 0.1, 0.5, 6.0);
                float edgeSoftness = Remap01(_EdgeSoftness, 0.001, 0.098, 0.5);
                float baseWidth = Remap01(_BaseWidth, 0.0, 0.491, 1.0);
                float noiseScale = Remap01(_NoiseScale, 0.5, 10.0, 25.0);
                float noiseStretch = Remap01(_NoiseStretch, 0.2, 2.0, 6.0);
                float distortionStrength = Remap01(_DistortionStrength, 0.0, 0.0, 0.4);
                float noiseScale2 = Remap01(_NoiseScale2, 0.5, 10.0, 25.0);
                float detailStrength = Remap01(_DetailStrength, 0.0, 0.02, 0.15);
                float noiseSpeed = Remap01(_NoiseSpeed, 0.0, 1.0, 4.0);
                float flickerSpeed = Remap01(_FlickerSpeed, 0.0, 5.0, 15.0);
                float flickerIntensity = Remap01(_FlickerIntensity, 0.0, 0.6, 1.5);
                float flickerScale = Remap01(_FlickerScale, 0.1, 1.0, 4.0);
                float flameSize = max(Remap01(_FlameSize, 0.3, 1.0, 3.0), 0.01);

                // 炎の位置オフセット + 大きさ: UVを移動・拡大縮小して平面上の位置とサイズを変える
                // (オフセットは正のXで右へ、正のYで上へ移動)
                float2 pivot = float2(0.5, 0.0);
                float2 shiftedUV = i.uv - float2(_FlameOffsetX, _FlameOffsetY);
                float2 uv = pivot + (shiftedUV - pivot) / flameSize;
                float h = saturate(uv.y);

                // レイヤー1: 大きく漂う粗いノイズ(縦に伸ばして揺らめきの塊を大きくする)
                float2 bigUV = uv * float2(noiseScale, noiseScale / max(noiseStretch, 0.01))
                                + float2(instOffset * 0.11, -t * noiseSpeed);
                float nBig = fbm(bigUV);

                // レイヤー2: 細かくちらつく詳細ノイズ
                float2 detailUV = uv * noiseScale2 + float2(instOffset * 0.37, -t * noiseSpeed * 1.7);
                float nDetail = pnoise(detailUV);

                // 根本(下)ほど歪みを弱く、先端ほど強くする
                float distortAmount = (nBig * distortionStrength + nDetail * detailStrength) * h;
                float distortedX = uv.x + distortAmount;
                float centered = distortedX - 0.5;

                // 高さに応じて先細りする炎の形状
                float widthAtH = flameWidth * (1.0 - pow(h, tipSharpness));
                float basePinch = lerp(baseWidth, 1.0, saturate(h * 4.0));
                widthAtH *= basePinch;

                float dist = abs(centered) - widthAtH * 0.5;
                float shapeMask = 1.0 - smoothstep(0.0, edgeSoftness, dist);

                // 上端・下端をフェード
                float topFade = 1.0 - smoothstep(0.85, 1.0, h);
                float bottomFade = smoothstep(0.0, 0.05, h);
                shapeMask *= topFade * bottomFade;

                float texMask = tex2D(_MainTex, uv).r;
                float mask = lerp(shapeMask, shapeMask * texMask, _UseTexture);

                // Perlinノイズによる明滅(ちらつき)。インスタンスごとにずらして同期を防ぐ
                float flickerNoise = pnoise(float2(t * flickerSpeed, instOffset * 0.05) * flickerScale);
                float flickerMul = 1.0 + flickerNoise * flickerIntensity;

                // Color Ramp: 高さ + ノイズの揺らぎでサンプリング位置をずらし、色にも揺らめきを出す
                float rampU = saturate(h + (nBig + nDetail) * 0.5 * _RampNoiseInfluence + _RampOffset);
                float3 rampCol = tex2D(_ColorRamp, float2(rampU, 0.5)).rgb;

                float3 proceduralCol = lerp(_ColorBase.rgb, _ColorMid.rgb, saturate(h * 2.0));
                proceduralCol = lerp(proceduralCol, _ColorTip.rgb, saturate(h * 2.0 - 1.0));

                float3 col = lerp(proceduralCol, rampCol, _UseColorRamp);
                col *= _Brightness * flickerMul;

                float alpha = pow(saturate(mask), _AlphaPower) * saturate(flickerMul);

                // アルファ事前乗算: これにより Alpha合成/Additive合成のどちらでも
                // マスクの外側(alpha=0)が色を持ち込まなくなり、形が正しく抜ける
                col *= alpha;

                fixed4 c = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Transparent"
    CustomEditor "CandleFlameShaderGUI"
}
