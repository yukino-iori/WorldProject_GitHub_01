Shader "Birdhouse/BirdhouseNightSky"
{
    Properties
    {
        [Header(Sky Colors)]
        _ZenithColor("Zenith Color", Color) = (0.005, 0.005, 0.02, 1)
        _HorizonColor("Horizon Color", Color) = (0.04, 0.03, 0.08, 1)
        
        [Header(Stars Settings)]
        _StarBrightness("Overall Brightness", Range(0.1, 10.0)) = 2.0
        _TwinkleSpeed("Twinkle Speed", Range(0, 10)) = 1.5
        
        [Header(Nebula Settings)]
        _NebulaColor("Nebula Color", Color) = (0.1, 0.05, 0.2, 1)
        _NebulaIntensity("Nebula Intensity", Range(0, 2)) = 0.5

        [Header(Moon Settings)]
        _MoonColor("Moon Color", Color) = (0.9, 0.9, 0.7, 1)
        _MoonAltitude("Moon Altitude", Range(-90, 90)) = 30.0
        _MoonAzimuth("Moon Azimuth", Range(0, 360)) = 180.0
        _MoonSize("Moon Size", Range(0.01, 0.2)) = 0.05
        _MoonGlow("Moon Glow Intensity", Range(0, 10)) = 3.0

        [Header(Rotation)]
        _RotationSpeed("Rotation Speed", Range(0, 2)) = 0.1
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

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 worldDir : TEXCOORD0; };

            fixed4 _ZenithColor, _HorizonColor, _NebulaColor, _MoonColor;
            float _StarBrightness, _TwinkleSpeed, _NebulaIntensity, _RotationSpeed;
            float _MoonAltitude, _MoonAzimuth, _MoonSize, _MoonGlow;

            float3 hash33(float3 p3) {
                p3 = frac(p3 * float3(.1031, .1030, .0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
            }

            float simple_noise(float3 x) {
                float3 i = floor(x); float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash33(i).x, hash33(i + float3(1, 0, 0)).x, f.x),
                            lerp(hash33(i + float3(0, 1, 0)).x, hash33(i + float3(1, 1, 0)).x, f.x), f.y);
            }

            float3x3 rotate(float angle, float3 axis) {
                float s, c; sincos(angle, s, c);
                float t = 1.0 - c;
                return float3x3(
                    t * axis.x * axis.x + c, t * axis.x * axis.y - s * axis.z, t * axis.x * axis.z + s * axis.y,
                    t * axis.x * axis.y + s * axis.z, t * axis.y * axis.y + c, t * axis.y * axis.z - s * axis.x,
                    t * axis.x * axis.z - s * axis.y, t * axis.y * axis.z + s * axis.x, t * axis.z * axis.z + c
                );
            }

            float3 renderStarLayer(float3 dir, float density, float size, float threshold, float twinklePower, float seed) {
                float3 p = mul(rotate(seed, float3(0.4, 0.9, 0.2)), dir * density);
                float3 i = floor(p); float3 f = frac(p);
                float3 rnd = hash33(i);
                if (rnd.x > threshold) {
                    float pSpeed = _TwinkleSpeed * (0.6 + rnd.x);
                    float wave = sin(_Time.y * pSpeed + rnd.x * 100.0) * 0.5 + 0.5;
                    float twinkle = 0.4 + 0.6 * pow(wave, twinklePower);
                    return float3(0.8, 0.9, 1.0) * lerp(float3(1,1,1), rnd, 0.2) * smoothstep(size * (0.8 + rnd.y * 0.4), 0.0, length(f - 0.5)) * twinkle;
                }
                return 0;
            }

            v2f vert (appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldDir = mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float3 dir = normalize(i.worldDir);
                
                // --- 月の位置計算 ---
                float altRad = radians(_MoonAltitude);
                float aziRad = radians(_MoonAzimuth);
                float3 mDir = normalize(float3(cos(altRad)*sin(aziRad), sin(altRad), cos(altRad)*cos(aziRad)));

                // 回転（星と天の川に適用）
                float3x3 rotMat = rotate(_Time.y * _RotationSpeed * 0.05, float3(0, 1, 0));
                float3 rotatedDir = mul(rotMat, dir);

                // 1. 背景
                float y = dir.y;
                fixed4 col = lerp(_HorizonColor, _ZenithColor, smoothstep(-0.4, 0.5, y));

                // 2. 星と天の川
                float3 stars = 0;
                stars += renderStarLayer(rotatedDir, 260, 0.45, 0.93, 2.0, 1.23) * 0.3;
                stars += renderStarLayer(rotatedDir, 130, 0.35, 0.985, 4.0, 4.56) * 0.7;
                stars += renderStarLayer(rotatedDir, 40, 0.25, 0.997, 6.0, 7.89) * 1.6;
                
                float3 nDir = rotatedDir * 2.5;
                float neb = pow((sin(nDir.x + nDir.y) * cos(nDir.z - nDir.x)) * 0.5 + 0.5, 5.0);
                float3 nebula = neb * _NebulaColor.rgb * _NebulaIntensity;

                // 3. 月の計算
                float distMoon = distance(dir, mDir);
                float moonAlpha = 0;
                float3 moonBody = 0;
                float3 moonGlow = 0;

                if (distMoon < _MoonSize * 10.0) {
                    float3 localMoonPos = (dir - mDir) / _MoonSize;
                    
                    // 月本体（マスクとして使用）
                    moonAlpha = smoothstep(1.0, 0.98, length(localMoonPos));
                    
                    // 月の模様
                    float pattern = simple_noise(localMoonPos * 3.5) * 0.5 + simple_noise(localMoonPos * 10.0) * 0.2;
                    moonBody = _MoonColor.rgb * lerp(1.0, 0.45, pattern);
                    
                    // 満月に完全にフィットしたグロウ
                    moonGlow = _MoonColor.rgb * exp(-max(0.0, length(localMoonPos) - 1.0) * 5.0) * _MoonGlow * 0.25;
                }

                // --- 合成：月がある場所は星を消す ---
                col.rgb += (stars * _StarBrightness + nebula) * (1.0 - moonAlpha);
                col.rgb = lerp(col.rgb, moonBody, moonAlpha);
                col.rgb += moonGlow;

                // 地平線マスク
                col.rgb *= smoothstep(-0.1, 0.1, y);
                return col;
            }
            ENDCG
        }
    }
}