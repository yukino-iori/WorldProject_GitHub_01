Shader "Birdhouse/BirdhouseDaySky"
{
    Properties
    {
        [Header(Sky Settings)]
        _SkyColor ("Sky Color", Color) = (0.2, 0.5, 0.8, 1)
        _HorizonColor ("Horizon Color", Color) = (0.7, 0.8, 0.9, 1)
        
        [Header(Sun Settings)]
        _SunPitch ("Sun Pitch (Vertical)", Range(-90, 90)) = 45
        _SunYaw ("Sun Yaw (Horizontal)", Range(-180, 180)) = 0
        _SunColor ("Sun Color", Color) = (1, 1, 0.9, 1)
        _SunOpacity ("Sun Opacity", Range(0, 1)) = 1.0
        _SunSize ("Sun Size", Range(0.001, 0.1)) = 0.02
        _SunBlur ("Sun Blur", Range(0.001, 0.2)) = 0.05

        [Header(Cloud Base Settings)]
        _CloudColor ("Cloud Color (Rim)", Color) = (1, 1, 1, 1)
        _RimBlur ("Rim Blur", Range(0.001, 0.5)) = 0.05
        
        _CloudShadowColor1 ("Cloud Shadow 1", Color) = (0.8, 0.8, 0.85, 1)
        _Shadow1Blur ("Shadow 1 Blur", Range(0.001, 0.5)) = 0.1
        
        _CloudShadowColor2 ("Cloud Shadow 2 (Deep)", Color) = (0.6, 0.6, 0.7, 1)
        _Shadow2Blur ("Shadow 2 Blur", Range(0.001, 0.5)) = 0.2
        
        [Header(Cloud Control)]
        _CloudScale ("Cloud Scale", Range(0.1, 10)) = 1.5
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.5
        _CloudSpeed ("Cloud Move Speed", Range(0, 0.5)) = 0.05
        _CloudEvolution ("Cloud Shape Evolution", Range(0, 0.5)) = 0.02
        _CloudDistortion ("Cloud Distortion", Range(0, 5)) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float3 texcoord : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            fixed4 _SkyColor, _HorizonColor, _SunColor;
            fixed4 _CloudColor, _CloudShadowColor1, _CloudShadowColor2;
            float _SunPitch, _SunYaw, _SunSize, _SunBlur, _SunOpacity;
            float _CloudScale, _CloudCoverage, _CloudSpeed, _CloudEvolution, _CloudDistortion;
            float _RimBlur, _Shadow1Blur, _Shadow2Blur;

            float hash3(float3 p) {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453123);
            }

            float noise3(float3 p) {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(hash3(i+float3(0,0,0)), hash3(i+float3(1,0,0)), f.x),
                         lerp(hash3(i+float3(0,1,0)), hash3(i+float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash3(i+float3(0,0,1)), hash3(i+float3(1,0,1)), f.x),
                         lerp(hash3(i+float3(0,1,1)), hash3(i+float3(1,1,1)), f.x), f.y), f.z);
            }

            float fbm3_warped(float3 p) {
                float v = 0.0;
                float m = 0.5;
                float3 offset = float3(noise3(p + float3(0, 0, 0)), 
                                       noise3(p + float3(5.2, 1.3, 0)),
                                       noise3(p + float3(1.3, 5.2, 0)));
                p += offset * _CloudDistortion;

                for (int i = 0; i < 5; i++) {
                    v += noise3(p) * m;
                    p = float3(p.x * 0.866 + p.y * 0.5, -p.x * 0.5 + p.y * 0.866, p.z) * 2.2;
                    m *= 0.5;
                }
                return v;
            }

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.viewDir = v.texcoord;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float3 dir = normalize(i.viewDir);
                
                // 太陽の方向計算
                float pitch = _SunPitch * 0.01745329;
                float yaw = _SunYaw * 0.01745329;
                float3 sunDir = float3(cos(pitch) * sin(yaw), sin(pitch), cos(pitch) * cos(yaw));

                // 背景グラデーション
                float horizonFact = smoothstep(-0.2, 0.6, dir.y);
                float3 finalColor = lerp(_HorizonColor.rgb, _SkyColor.rgb, horizonFact);

                // 太陽の描画（不透明度スライダー適用）
                float sunDot = saturate(dot(dir, sunDir));
                float sunMask = smoothstep(1.0 - _SunSize - _SunBlur, 1.0 - _SunSize, sunDot);
                finalColor = lerp(finalColor, _SunColor.rgb, sunMask * _SunOpacity);

                // 雲の描画
                if (dir.y > -0.05) {
                    float2 uv = dir.xz / (max(0.01, dir.y) + 0.3);
                    float3 p = float3(uv * _CloudScale, _Time.y * _CloudEvolution);
                    p.xy += _Time.y * _CloudSpeed;

                    float n = fbm3_warped(p);
                    float dMap = noise3(float3(uv * _CloudScale * 0.3 + n * 0.2, p.z * 0.5));
                    float combinedDensity = n * dMap;
                    
                    float threshold = 1.0 - _CloudCoverage;
                    
                    float maskRim = smoothstep(threshold, threshold + _RimBlur, combinedDensity);
                    float maskShadow1 = smoothstep(threshold + 0.1, threshold + 0.1 + _Shadow1Blur, combinedDensity);
                    float maskShadow2 = smoothstep(threshold + 0.2, threshold + 0.2 + _Shadow2Blur, combinedDensity);
                    
                    float3 cloudCol = _CloudColor.rgb;
                    cloudCol = lerp(cloudCol, _CloudShadowColor1.rgb, maskShadow1);
                    cloudCol = lerp(cloudCol, _CloudShadowColor2.rgb, maskShadow2);
                    
                    float edgeFade = smoothstep(-0.05, 0.4, dir.y);
                    finalColor = lerp(finalColor, cloudCol, maskRim * edgeFade);
                }

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
}