Shader "Rasta/FlipCamera/PhotoShader"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Main Color", Color) = (1,1,1,1)
        [NoScaleOffset] _WatermarkTexHorizontal ("Watermark Horizontal", 2D) = "black" {}
        [NoScaleOffset] _WatermarkTexVertical ("Watermark Vertical", 2D) = "black" {}
        _WatermarkColor ("Watermark Color", Color) = (1,1,1,1)
        _WatermarkAngle ("Watermark Angle", int) = 0
        _FullScreenDistance("FullScreenDistance", Float) = 0.01
        _BackgroundColor ("BackgroundColor", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+1000" }
        LOD 100

        Pass
        {
            Tags { "RenderType"="Opaque" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _WatermarkTexHorizontal;
            sampler2D _WatermarkTexVertical;
            fixed4 _WatermarkColor;
            int _WatermarkAngle;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col1 = tex2D(_MainTex, i.uv) * _Color;

                float2 watermarkUV = (uint)_WatermarkAngle / 2u % 2u ? 1-i.uv: i.uv;
                watermarkUV = (uint)_WatermarkAngle % 2u ? float2(watermarkUV.y, 1-watermarkUV.x) : watermarkUV;

                fixed4 col2_h = tex2D(_WatermarkTexHorizontal, watermarkUV) * _WatermarkColor;
                fixed4 col2_v = tex2D(_WatermarkTexVertical, watermarkUV) * _WatermarkColor;
                fixed4 col2 = _WatermarkAngle % 2u ? col2_v : col2_h;
                fixed4 col = fixed4((col2.rgb * col2.a + col1.rgb * (1.0 - col2.a)), 1);

                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
        
        Pass
        {
            Tags { "RenderType" = "Transparent" }
            ZTest Always
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            sampler2D _WatermarkTexHorizontal;
            sampler2D _WatermarkTexVertical;
            fixed4 _WatermarkColor;
            int _WatermarkAngle;
            float4 _BackgroundColor;
            float _FullScreenDistance;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                float4 worldPos = mul(unity_ObjectToWorld, float4(0, 0, 0, 1));
                float3 cameraPos = _WorldSpaceCameraPos;
                float dist = distance(worldPos.xyz, cameraPos);

                if (dist < _FullScreenDistance
                    && 
                    (
                        (_ScreenParams.x == 1280 && _ScreenParams.y == 720) ||
                        (_ScreenParams.x == 1920 && _ScreenParams.y == 1080) ||
                        (_ScreenParams.x == 2560 && _ScreenParams.y == 1440) ||
                        (_ScreenParams.x == 3840 && _ScreenParams.y == 2160) ||
                        (_ScreenParams.x == 7680 && _ScreenParams.y == 4320)
                    )
                )
                {
                    o.vertex.x = v.uv.x > 0.5 ? 1 : -1;
                    o.vertex.y = v.uv.y > 0.5 ? 1 : -1;
                    o.vertex.z = 1;
                    o.vertex.w = 1;

                    #if UNITY_REVERSED_Z
                    o.vertex.z = o.vertex.z * 2.0 - 1.0;
                    #endif
                    #if UNITY_UV_STARTS_AT_TOP
                    o.vertex.y = -o.vertex.y;
                    #endif

                    
                    float2 params = float2(_ScreenParams.x * 3.0/4.0, _ScreenParams.y);
                    o.uv -= 0.5;
                    o.uv = o.uv * (params.x > params.y ? float2(params.x / params.y, 1) : float2(1, params.y / params.x)); 
                    o.uv += 0.5;
                }
                else
                {
                    o.vertex = 0;
                }

                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col1 = tex2D(_MainTex, i.uv) * _Color;

                float2 watermarkUV = (uint)_WatermarkAngle / 2u % 2u ? 1-i.uv: i.uv;
                watermarkUV = (uint)_WatermarkAngle % 2u ? float2(watermarkUV.y, 1-watermarkUV.x) : watermarkUV;

                fixed4 col2_h = tex2D(_WatermarkTexHorizontal, watermarkUV) * _WatermarkColor;
                fixed4 col2_v = tex2D(_WatermarkTexVertical, watermarkUV) * _WatermarkColor;
                fixed4 col2 = _WatermarkAngle % 2u ? col2_v : col2_h;
                fixed4 col = fixed4((col2.rgb * col2.a + col1.rgb * (1.0 - col2.a)), 1);

                col = i.uv.x > 1 || i.uv.x < 0 || i.uv.y > 1 || i.uv.y < 0 ? _BackgroundColor : col;

                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
