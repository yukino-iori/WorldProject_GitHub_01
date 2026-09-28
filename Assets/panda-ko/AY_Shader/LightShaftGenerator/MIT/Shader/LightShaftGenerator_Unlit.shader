// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: Copyright (c) 2026 AYANO_TFT.

Shader "AY_Shader/LightShaftGenerator/Unlit"
{
	Properties
	{
		_MainTex("MainTex", 2D) = "white" {}
		[HDR] _Color("Color", Color) = ( 1, 1, 1 )
		_CameraFadeMin("CameraFadeMin", Float) = 0
		_CameraFadeMax("CameraFadeMax", Float) = 5
		_Intensity("Intensity", Range(0, 50)) = 1.0

		_EdgeFade ("EdgeFade", Range(0, 1)) = 0.2
		_CornerFade ("CornerFade", Range(0, 2)) = 0.0
		_TopAnchor("TopAnchor", Range(0, 1)) = 0.2

		_AttenRate("AttenRate", Range(0, 1)) = 1.0
		_AttenSharpness("AttenSharpness", Range(0.01, 100)) = 2

		_NoiseTex("NoiseTex", 2D) = "white" {}
		_NoiseTexScale("NoiseTexScale", Range(0.001, 10)) = 1
		_NoiseSpeedX("NoiseSpeedX", Range(-1.0, 1.0)) = 0.03
		_NoiseSpeedY("NoiseSpeedY", Range(-1.0, 1.0)) = 0.03
		_NoiseMin("NoiseMin", Range(0, 1)) = 0
		_NoiseMax("NoiseMax", Range(0, 1)) = 1
	}

	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }

		Blend One One, Zero One
		AlphaToMask Off
		Cull Off
		ZWrite Off
		ZTest LEqual

		Pass
		{
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 3.5
				#pragma multi_compile_fog

				#include "UnityCG.cginc"
				#include "UnityShaderVariables.cginc"

				// TEXCOORD0: x=ライン方向距離, y=レイ方向0-1（ライン内最大で正規化）, z=ベース0/先端1, w=ライン方向0-1
				// TEXCOORD1: xy=開始平面ローカル座標, z=1.0/ポリゴン枚数, w=予約
				struct appdata
				{
					float4 vertex : POSITION;
					float4 uv0 : TEXCOORD0;
					float4 uv1 : TEXCOORD1;
					float3 normal : NORMAL;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float4 vertex : SV_POSITION;
					float4 uv0 : TEXCOORD0;
					float4 uv1 : TEXCOORD1;
					float3 positionWS : TEXCOORD2;
					float3 normalWS : TEXCOORD3;
					UNITY_FOG_COORDS(4)
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};

				uniform sampler2D _MainTex;
				uniform float4 _MainTex_ST;
				uniform float3 _Color;
				uniform float _CameraFadeMin;
				uniform float _CameraFadeMax;
				uniform float _Intensity;
				uniform float _AttenRate;
				uniform float _AttenSharpness;
				uniform float _EdgeFade;
				uniform float _CornerFade;
				uniform float _TopAnchor;

				uniform sampler2D _NoiseTex;
				uniform float _NoiseTexScale;
				uniform float _NoiseSpeedX;
				uniform float _NoiseSpeedY;
				uniform float _NoiseMin;
				uniform float _NoiseMax;

				v2f vert(appdata v)
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);

					o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
					o.normalWS = UnityObjectToWorldNormal(v.normal);

					o.uv0 = v.uv0;
					o.uv1 = v.uv1;
					o.uv1.xy *= _NoiseTexScale;

					o.vertex = UnityObjectToClipPos(v.vertex);
					UNITY_TRANSFER_FOG(o, o.vertex);
					return o;
				}

				float4 frag(v2f IN, bool vface : SV_IsFrontFace) : SV_Target
				{
					UNITY_SETUP_INSTANCE_ID(IN);
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

					float4 col = 1.0;

					float2 uv = IN.uv0.xy;
					float3 originWS = UNITY_MATRIX_M._m03_m13_m23;
					uv.x += originWS.x + originWS.z;
					uv.y = (uv.y - _TopAnchor);
					float rate = uv.y > 0 ? (1.0 - _TopAnchor) : -_TopAnchor;
					uv.y /= rate;
					float2 orguv = uv.xy;
					uv = TRANSFORM_TEX(uv, _MainTex);
					col = tex2D(_MainTex, uv);

					float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
					float3 normalWS = normalize(IN.normalWS);
					normalWS = vface > 0 ? normalWS : -normalWS;
					float NdV = dot(normalWS, viewDirWS);
					float fresnel = pow(1.0 - NdV, 5.0);

					float invFresnel = saturate(1.0 - fresnel);
					float cameraFade = smoothstep(_CameraFadeMin, _CameraFadeMax, distance(_WorldSpaceCameraPos, IN.positionWS));

					float2 centerUV = float2(IN.uv0.w, IN.uv0.z) - 0.5;
					float euclidDist = saturate(1.0 - length(centerUV) * 2.0 * 0.70710678);
					float cornerFade = smoothstep(0.0, _CornerFade + 1e-5, euclidDist);
					float chebDist = saturate(1.0 - max(abs(centerUV.x), abs(centerUV.y)) * 2.0);
					float edgeFade = smoothstep(0.0, _EdgeFade + 1e-5, chebDist);
					float atten = lerp(1.0, pow(saturate(1.0 - orguv.y), _AttenSharpness), _AttenRate);
					float polycountRate = IN.uv1.z;

					col.rgb *= _Color * _Intensity * invFresnel * cameraFade * edgeFade * cornerFade * atten * polycountRate;

					float2 noiseOffset = float2(_NoiseSpeedX, _NoiseSpeedY) * _Time.y;
					float3 noise1 = tex2Dlod(_NoiseTex, float4(IN.uv1.xy * 0.1 + noiseOffset, 0, 0)).rgb;
					col.rgb *= smoothstep(_NoiseMin, _NoiseMax, noise1);

					UNITY_APPLY_FOG_COLOR(IN.fogCoord, col, fixed4(0,0,0,0));

					return col;
				}
			ENDCG
		}
	}
	CustomEditor "AY_Shader.LightShaftGenUnlitShaderGUI"
}
