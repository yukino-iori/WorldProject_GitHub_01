// Based on: Copyright (c) 2020 momoma (MS_Grass.shader)
// Released under the MIT license
// https://opensource.org/licenses/mit-license.php
//
// ---- このシェーダーで追加・変更した点 ----
// 1. GPUインスタンシング対応 (UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_SETUP_INSTANCE_ID / UNITY_TRANSFER_INSTANCE_ID)
//    ・vert -> hull -> domain -> geometry -> fragment まで Instance ID を伝搬させています。
//    ・_Color, _TopColor, _WindPhaseOffset はインスタンスごとに変更可能 (UNITY_INSTANCING_BUFFER)。
//    ・マテリアルの Enable GPU Instancing にチェックを入れ、同一メッシュを使うタイル/チャンクを
//      MaterialPropertyBlock 経由で描画すると、ドローコールがまとまり軽量化されます。
//
// 2. 「メッシュ形状」と「配置密度」を分離
//    ・旧実装は _Tess (テッセレーション係数) がそのまま密度になり、かつ
//      テッセレーション後の辺の長さから幅を算出していたため、密度を上げると
//      葉が細くなってしまう問題がありました。
//    ・_Density  : 配置密度専用 (テッセレーション係数)
//    ・_BladeWidth / _BladeWidthRandomness : メッシュ形状(幅)専用。密度を変えても幅は変わりません。
//    ・_Size は従来通り葉の全体的な大きさ(高さ方向のスケール)を担当します。
//
// 3. 距離による自動非表示
//    ・hullconst (パッチ定数関数) 内でパッチ中心とカメラの距離を計算し、
//      _MaxDistance - _FadeDistance 〜 _MaxDistance の範囲でテッセレーション係数自体を
//      0までフェードします。係数が0になったパッチはテッセレーション/ジオメトリシェーダーの
//      呼び出し自体が発生しないため、遠景の草の非表示と負荷軽減を同時に行えます。
//
// 4. 風表現(明るさによる「風の波」)
//    ・従来の葉先が揺れる表現(_WindSpeed)はそのまま残しつつ、
//      ワールド座標(XZ)×風向き(_WindDirection)と時間から生成した進行波を
//      フラグメントシェーダーで色に乗算することで、フィールド全体を伝わる
//      明暗のうねり(風の波)を表現します。
//    ・_WindWaveFrequency: 波の空間的な細かさ  _WindWaveSpeed: 波が流れる速さ
//      _WindWaveStrength : 明るさの変化量  _WindPhaseOffset(インスタンス単位) : 位相ずらし
//    ・単調に見えないよう、周波数/速度/向きの異なる3つの波を合成し、さらに低周波の
//      「ガスト(突風)」包絡線で強弱にムラを付けています。_WindGustiness で調整可能です。
//
// 5. 白黒マスクによる配置範囲・高さの制御
//    ・_MaskTex (グレースケール想定) をワールド座標(XZ)にマッピングしてサンプリングし、
//      白(1) に近いほど密に生え、黒(0) に近いほど生えなくなります。
//    ・_MaskTex は普通のテクスチャと同じ感覚でメッシュのUV2(3チャンネル目)を使ってサンプリングします。
//      Tiling/Offsetもマテリアルインスペクタの_MaskTexの欄でそのまま調整できます。
//      ※メッシュ生成時にUV2へ0〜1のUVを書き込んでおく必要があります(uv0/uv1は他の用途で使用中)。
//    ・_MaskHeightInfluence (0〜1) でマスクの明るさを草の高さにも反映できます(0=高さに影響なし)。
//
// 6. 画像を貼った板ポリ(草原用の"カード"メッシュ)モード
//    ・マテリアルの Use Image Quad にチェックを入れると、従来の手続き型の細い葉メッシュの代わりに、
//      _GrassTex を貼った1枚の板ポリ(クアッド)を配置します。画像1枚(=草のクラスタ画像など)を
//      たくさん並べて草原にするタイプの表現です。
//    ・密度(_Density)・距離カリング・マスク・風(明るさ)・GPUインスタンシングは両モード共通です。
//    ・_GrassTex のアルファをカットアウト用に使います(_Cutoff で閾値調整)。
//
// 7. 風のなびき表現の強化
//    ・従来の「明るさの乗算」だけだと暗いシーンでは変化が見えにくいため、
//      風が吹き抜ける瞬間に穂先が加算(Additive)でハイライトするようにしました
//      (_WindHighlightColor / _WindHighlightStrength)。暗所でも視認できます。
//    ・葉の物理的な揺れ幅を _WindBendStrength として独立して調整可能にし、
//      さらに風の波(進行方向)と軽く同期させることで、明るさに頼らずシルエットの
//      動きだけでも「風が吹いている」感が出るようにしました。
//    ・_WindTipWeight で、根元より穂先に効果を集中させられます。
//
// 8. 草原全体の輝度調整
//    ・_Brightness で最終的な色に一律の輝度倍率をかけられます。
//
// 9. VRC Light Volumes 対応
//    ・プロジェクトに VRC Light Volumes (red.sim.lightvolumes) がインストールされていると
//      自動的に定義される VRC_LIGHT_VOLUMES_ENABLED を見て、対応コードを条件コンパイルします。
//      パッケージが無いプロジェクトでもそのままコンパイルできます。
//    ・草原のような植生では負荷軽減のため、公式ドキュメント推奨の通り
//      ジオメトリシェーダー側(草1本あたり1回)でLightVolumeSHをサンプリングしています。
//    ・_LightVolumeStrength で寄与の強さを調整できます。
//
// ---- Unity側での注意 ----
// ・メッシュのトポロジーは元シェーダーと同様に Lines (2点1組の線分) を想定しています。
//   通常のTriangleメッシュでは動作しません。
// ・マテリアルインスペクタで「Enable GPU Instancing」をON にしてください。

Shader "MomomaShader/Geometry/GrassField"
{
	Properties
	{
		[Header(Color)]
		_Color ("Bottom Color", Color) = (0.1, 0.4, 0.1, 1)
		_TopColor ("Top Color", Color) = (0.1, 1, 0.1, 1)

		[Header(RenderMode)]
		[Toggle(_USE_BILLBOARD_QUAD)] _UseBillboardQuad ("Use Image Quad (instead of blade mesh)", Float) = 0
		_GrassTex ("Quad Texture (Alpha = Cutout)", 2D) = "white" {}
		_Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

		[Header(BladeShape)]
		_Size ("Blade Height Scale", Range(0, 1)) = 0.5
		_BladeWidth ("Blade Width", Range(0, 1)) = 0.1
		_BladeWidthRandomness ("Blade Width Randomness", Range(0, 1)) = 0.3
		_HeightOffset ("Height Offset", Float) = 0.0

		[Header(PlantingDensity)]
		_Density ("Density (Tessellation)", Range(1, 64)) = 5.0

		[Header(DistanceCulling)]
		_MaxDistance ("Max Draw Distance", Float) = 50
		_FadeDistance ("Fade Distance", Range(0.01, 50)) = 10

		[Header(PlacementMask)]
		[Toggle(_MASK_DEBUG_VIEW)] _MaskDebugView ("Mask Debug View", Float) = 0
		_MaskTex ("Placement/Height Mask (grayscale, uses UV2)", 2D) = "white" {}
		_MaskHeightInfluence ("Mask Height Influence", Range(0, 1)) = 1.0

		[Header(ColorAdjustment)]
		_Brightness ("Overall Brightness", Range(0, 3)) = 1.0

		[Header(WindSway)]
		_WindSpeed ("Wind Speed (Blade Sway)", Float) = 0.5
		_WindBendStrength ("Wind Bend Strength (Geometry Sway)", Range(0, 0.5)) = 0.01

		[Header(WindBrightnessWave)]
		_WindDirection ("Wind Direction (XZ)", Vector) = (1, 0, 0, 0)
		_WindWaveFrequency ("Wind Wave Frequency", Float) = 0.15
		_WindWaveSpeed ("Wind Wave Speed", Float) = 1.0
		_WindWaveStrength ("Wind Wave Brightness Strength", Range(0, 1)) = 0.35
		_WindGustiness ("Wind Gustiness", Range(0, 1)) = 0.6
		_WindPhaseOffset ("Wind Phase Offset (per-instance)", Range(0, 1)) = 0.0
		[HDR] _WindHighlightColor ("Wind Highlight Color (Additive)", Color) = (1.2, 1.1, 0.6, 1)
		_WindHighlightStrength ("Wind Highlight Strength (Additive)", Range(0, 2)) = 0.5
		_WindTipWeight ("Wind Effect Tip Weight", Range(0, 1)) = 0.6

		[Header(VRCLightVolumes)]
		_LightVolumeStrength ("Light Volume Strength", Range(0, 2)) = 1.0
	}

	SubShader
	{
		Tags { "IgnoreProjector" = "True" }
		Cull Off

		CGINCLUDE

		#include "UnityCG.cginc"
		#include "Tessellation.cginc"
		#include "Lighting.cginc"
		#if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
			#include "AutoLight.cginc"
		#endif
		// VRC Light Volumes (red.sim.lightvolumes) がプロジェクトに入っていると
		// VRC_LIGHT_VOLUMES_ENABLED が自動的に定義される。入っていないプロジェクトでも
		// コンパイルできるよう条件付きでインクルードする。
		#if defined(VRC_LIGHT_VOLUMES_ENABLED)
			#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
		#endif

		struct appdata
		{
			float4 pos : POSITION;
			float2 uv : TEXCOORD0;
			float2 uv1 : TEXCOORD1;
			float2 uv2 : TEXCOORD2;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		#if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
			struct g2f
			{
				UNITY_POSITION(pos);
				#ifdef LIGHTMAP_ON
					float4 uv : TEXCOORD0;
				#else
					float2 uv : TEXCOORD0;
				#endif
				float3 worldNormal : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
				UNITY_SHADOW_COORDS(3)
				UNITY_FOG_COORDS(4)
				float2 maskUV : TEXCOORD5;
				#if defined(VRC_LIGHT_VOLUMES_ENABLED)
					float3 lightVolumeColor : TEXCOORD6;
				#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
		#elif defined(UNITY_PASS_SHADOWCASTER)
			struct g2f
			{
				V2F_SHADOW_CASTER;
				#if defined(_USE_BILLBOARD_QUAD)
					float2 uv : TEXCOORD5;
				#endif
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
		#endif

		// ---- インスタンスごとに変更できるプロパティ ----
		UNITY_INSTANCING_BUFFER_START(Props)
			UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
			UNITY_DEFINE_INSTANCED_PROP(fixed4, _TopColor)
			UNITY_DEFINE_INSTANCED_PROP(half, _WindPhaseOffset)
		UNITY_INSTANCING_BUFFER_END(Props)

		// ---- 全インスタンス共通のプロパティ ----
		sampler2D _GrassTex;
		float4 _GrassTex_ST;
		fixed _Cutoff;

		fixed _Size;
		fixed _HeightOffset;

		half _BladeWidth;
		half _BladeWidthRandomness;

		half _Density;

		float _MaxDistance;
		float _FadeDistance;

		sampler2D _MaskTex;
		float4 _MaskTex_ST;
		half _MaskHeightInfluence;

		half _WindSpeed;
		half _WindBendStrength;

		float4 _WindDirection;
		half _WindWaveFrequency;
		half _WindWaveSpeed;
		half _WindWaveStrength;
		half _WindGustiness;
		half4 _WindHighlightColor;
		half _WindHighlightStrength;
		half _WindTipWeight;

		fixed _Brightness;
		half _LightVolumeStrength;

		inline float2 hash22(float2 p)
		{
			return frac(float2(262144, 32768) * sin(dot(p, float2(41, 289))));
		}

		inline float CubicSmooth(float x)
		{
			return x * x * (3.0 - 2.0 * x);
		}

		inline float TriangleWave(float x)
		{
			return abs((frac(x + 0.5) * 2.0) - 1.0);
		}

		inline float TrigApproximate(float x)
		{
			return CubicSmooth(TriangleWave(x)) * 2.0 - 1.0;
		}

		// 普通のテクスチャと同じ感覚で、メッシュのUV2をそのまま使って _MaskTex をサンプリングする。
		// Tiling/Offset もマテリアルインスペクタの _MaskTex の欄でそのまま調整できる。
		// 白(1)=生える/高い、黒(0)=生えない/低い。
		// hull(パッチ定数関数)・geometryシェーダーどちらからも呼べるよう明示LODでサンプリングする。
		inline half SampleMaskUV(float2 uv)
		{
			return tex2Dlod(_MaskTex, float4(TRANSFORM_TEX(uv, _MaskTex), 0, 0)).r;
		}

		appdata vert(appdata v)
		{
			#ifdef LIGHTMAP_ON
				v.uv1.xy = v.uv1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
			#endif
			return v;
		}

		// パッチ定数関数：ここで「配置密度」と「距離カリング」を扱う
		UnityTessellationFactors hullconst(InputPatch < appdata, 3 > v)
		{
			UnityTessellationFactors o;

			UNITY_SETUP_INSTANCE_ID(v[0]);

			float3 worldPos0 = mul(unity_ObjectToWorld, v[0].pos).xyz;
			float3 worldPos1 = mul(unity_ObjectToWorld, v[1].pos).xyz;
			float3 worldPos2 = mul(unity_ObjectToWorld, v[2].pos).xyz;
			float3 centerWorldPos = (worldPos0 + worldPos1 + worldPos2) / 3.0;

			float dist = distance(centerWorldPos, _WorldSpaceCameraPos);
			// _MaxDistance - _FadeDistance 〜 _MaxDistance の間で密度を0までフェードし、
			// それ以上遠い草はテッセレーション/ジオメトリシェーダー自体が走らないようにする
			float fade = 1.0 - saturate((dist - (_MaxDistance - _FadeDistance)) / max(_FadeDistance, 0.0001));

			// 白黒マスクによる配置範囲の制御：黒い部分は密度を0にして生やさない
			// メッシュのUV2(3チャンネル目、乱数化されていない生のUV)を使ってサンプリングする
			float2 maskUV = (v[0].uv2 + v[1].uv2 + v[2].uv2) / 3.0;
			float mask = SampleMaskUV(maskUV);

			float tess = _Density * fade * mask;

			o.edge[0] = tess;
			o.edge[1] = tess;
			o.edge[2] = tess;
			o.inside = tess;
			return o;
		}

		[UNITY_domain("tri")]
		[UNITY_partitioning("fractional_odd")]
		[UNITY_outputtopology("triangle_cw")]
		[UNITY_patchconstantfunc("hullconst")]
		[UNITY_outputcontrolpoints(3)]
		appdata hull(InputPatch < appdata, 3 > v, uint id : SV_OutputControlPointID)
		{
			return v[id];
		}

		[UNITY_domain("tri")]
		appdata doma(UnityTessellationFactors tessFactors, const OutputPatch < appdata, 3 > vi, float3 bary : SV_DomainLocation)
		{
			appdata v;
			UNITY_INITIALIZE_OUTPUT(appdata, v);
			UNITY_TRANSFER_INSTANCE_ID(vi[0], v);
			v.pos = vi[0].pos * bary.x + vi[1].pos * bary.y + vi[2].pos * bary.z;
			v.uv = hash22(vi[0].uv * bary.x + vi[1].uv * bary.y + vi[2].uv * bary.z);
			v.uv1 = vi[0].uv1 * bary.x + vi[1].uv1 * bary.y + vi[2].uv1 * bary.z;
			v.uv2 = vi[0].uv2 * bary.x + vi[1].uv2 * bary.y + vi[2].uv2 * bary.z;
			return v;
		}

		[maxvertexcount(10)]
		void geom(line appdata input[2], inout TriangleStream<g2f> outStream)
		{
			g2f o;
			UNITY_INITIALIZE_OUTPUT(g2f, o);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
			UNITY_SETUP_INSTANCE_ID(input[0]);
			UNITY_TRANSFER_INSTANCE_ID(input[0], o);

			float seed[4] = {
				(input[0].uv.x + input[1].uv.x), (input[0].uv.y + input[1].uv.y) * 0.5, (input[0].uv.x + input[1].uv.y) * 0.5, (input[0].uv.y + input[1].uv.x)
			};
			float2 s;
			sincos(seed[0] * UNITY_TWO_PI, s.y, s.x);
			float4x4 rotationMatrix = 0;
			rotationMatrix._m00 = s.x;
			rotationMatrix._m20 = s.y;
			rotationMatrix._m11 = 1;
			rotationMatrix._m02 = -s.y;
			rotationMatrix._m22 = s.x;

			// ---- メッシュ形状(幅)：配置密度(_Density)とは独立して決める ----
			// 旧実装はテッセレーション後の辺の長さ(=密度に依存)を幅にしていたため、
			// 密度を上げると幅が細くなっていた。ここでは _BladeWidth を基準値とし、
			// 見た目の自然さのためだけに乱数でわずかにばらつかせる。
			float widthJitter = 1.0 + _BladeWidthRandomness * (seed[1] * 2.0 - 1.0);
			float width = _BladeWidth * max(widthJitter, 0.1);

			float4 pos = mul(unity_ObjectToWorld, lerp(input[0].pos, input[1].pos, seed[2]));
			pos.y += _HeightOffset;
			float scale = 1.0 + seed[1] * seed[1] * seed[1];

			// 葉ごとの個別のフラつき(seed[3])に、風の波と同じ進行方向/速度の成分を軽く混ぜることで、
			// 明るさの変化に頼らずシルエットの揺れだけでも「風が吹き抜けている」感が出るようにする
			float2 windDirForBend = normalize(_WindDirection.xz + float2(1e-5, 0));
			float windTravelPhase = dot(pos.xz, windDirForBend) * _WindWaveFrequency - _Time.y * _WindWaveSpeed;
			float wave = _WindBendStrength * TrigApproximate(_Time.y / scale * _WindSpeed + seed[3] + windTravelPhase * 0.5);
			scale *= _Size;

			// 白黒マスクによる高さの制御：マスクが暗いほど草が低くなる(_MaskHeightInfluenceで影響度を調整)
			float2 maskUV = lerp(input[0].uv2, input[1].uv2, seed[2]);
			float maskAtBlade = SampleMaskUV(maskUV);
			scale *= lerp(1.0, maskAtBlade, _MaskHeightInfluence);
			float4 worldPos;
			#if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
				o.worldNormal = float3(-s.y, 0, s.x);
				o.maskUV = maskUV;
				float2 uv1 = lerp(input[0].uv1, input[1].uv1, seed[2]);
				#ifdef LIGHTMAP_ON
					o.uv.zw = uv1;
				#endif
				// VRC Light Volumesは負荷軽減のため、草1本(ブレード)あたり1回だけ
				// (ピクセル単位ではなくジオメトリシェーダー単位で)サンプリングする
				#if defined(UNITY_PASS_FORWARDBASE) && defined(VRC_LIGHT_VOLUMES_ENABLED)
					float3 lvL0, lvL1r, lvL1g, lvL1b;
					LightVolumeSH(pos.xyz, lvL0, lvL1r, lvL1g, lvL1b);
					o.lightVolumeColor = LightVolumeEvaluate(o.worldNormal, lvL0, lvL1r, lvL1g, lvL1b);
				#endif
			#elif defined(UNITY_PASS_SHADOWCASTER)
				appdata_base v;
				UNITY_INITIALIZE_OUTPUT(appdata_base, v);
				v.normal = normalize(mul((float3x3)unity_WorldToObject, float3(-s.y, 0, s.x)));
			#endif

			#if defined(_USE_BILLBOARD_QUAD)
				// ---- 画像を貼った板ポリ(1枚のクアッド)モード ----
				// 幅/高さは同じ _BladeWidth / _Size / マスクの高さ影響をそのまま使う。
				// 頂点は4つ(左下,右下,左上,右上)で、トライアングルストリップとして2枚の三角形になる。
				[unroll]
				for (int qy = 0; qy < 2; qy++)
				{
					[unroll]
					for (int qx = 0; qx < 2; qx++)
					{
						worldPos = pos + mul(rotationMatrix, float4((qx - 0.5) * width * scale, qy * scale, 0, 0));
						worldPos.x += qy * wave * scale;
						#if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
							o.uv.xy = float2(qx, qy);
							o.worldPos = worldPos;
							o.pos = UnityWorldToClipPos(o.worldPos);
							UNITY_TRANSFER_SHADOW(o, uv1);
							UNITY_TRANSFER_FOG(o, o.pos);
						#elif defined(UNITY_PASS_SHADOWCASTER)
							o.uv = float2(qx, qy);
							v.vertex = mul(unity_WorldToObject, worldPos);
							TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
						#endif
						outStream.Append(o);
					}
				}
			#else
				// ---- 従来の手続き型の細い葉メッシュ(5段×2列) ----
				[unroll]
				for (int y = 0; y < 5; y++)
				{
					[unroll]
					for (int x = 0; x < 2; x++)
					{
						worldPos = pos + mul(rotationMatrix, float4(float2((x - 0.5) * (1.0 - y * 0.25) * width, y * 0.25) * scale, 0, 0));
						worldPos.x += y * y * wave * scale;
						#if defined(UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
							o.uv.xy = float2(x, y * 0.25);
							o.worldPos = worldPos;
							o.pos = UnityWorldToClipPos(o.worldPos);
							UNITY_TRANSFER_SHADOW(o, uv1);
							UNITY_TRANSFER_FOG(o, o.pos);
						#elif defined(UNITY_PASS_SHADOWCASTER)
							v.vertex = mul(unity_WorldToObject, worldPos);
							TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
						#endif
						outStream.Append(o);
					}
				}
			#endif
			outStream.RestartStrip();
		}

		// 風の波(-1〜1程度)を計算する共通関数。単一の波だけだと縞模様のように単調に見えるため、
		// 周波数・速度・向きの異なる3つの波を合成し、さらに低周波の
		// 「ガスト(突風)」包絡線で強弱にムラを持たせている。
		inline half ComputeWindWave(float3 worldPos)
		{
			float2 windDir = normalize(_WindDirection.xz + float2(1e-5, 0));
			// windDir を少し回転させた向きを2つ目の波の進行方向として使う(無理数っぽい係数でズレを作る)
			float2 windDir2 = normalize(float2(windDir.x * 0.6 - windDir.y * 0.8, windDir.x * 0.8 + windDir.y * 0.6));

			half phaseOffset = UNITY_ACCESS_INSTANCED_PROP(Props, _WindPhaseOffset) * UNITY_TWO_PI;

			half phase0 = dot(worldPos.xz, windDir) * _WindWaveFrequency - _Time.y * _WindWaveSpeed;
			half phase1 = dot(worldPos.xz, windDir2) * _WindWaveFrequency * 2.37 - _Time.y * _WindWaveSpeed * 1.61;
			half phase2 = dot(worldPos.xz, windDir) * _WindWaveFrequency * 0.53 - _Time.y * _WindWaveSpeed * 0.29;

			half wave0 = TrigApproximate(phase0 + phaseOffset);
			half wave1 = TrigApproximate(phase1 + phaseOffset);
			half wave2 = TrigApproximate(phase2 + phaseOffset);

			// 大きいうねり + 細かいうねりを合成(重み付きブレンド)
			half wave = wave0 * 0.55 + wave1 * 0.3 + wave2 * 0.15;

			// ゆっくり変化する包絡線で「突風が来ては去る」ムラを表現する
			half gustEnvelope = lerp(1.0, 0.5 + 0.5 * TrigApproximate(phase2 * 0.2 + phaseOffset * 0.5), _WindGustiness);

			return wave * gustEnvelope;
		}

		// 風の表現を色に適用する。
		// ・乗算(_WindWaveStrength) : 従来通りの明暗の変化。暗いシーンでは目立ちにくい。
		// ・加算(_WindHighlightStrength) : 風が吹き抜ける瞬間、穂先(tipFactor)がハイライト色で
		//   加算発光する。乗算と違い、暗い場所でも視認できるのが利点。
		inline void ApplyWindLighting(inout half3 color, float3 worldPos, half tipUV)
		{
			half wave = ComputeWindWave(worldPos);
			color *= 1.0 + wave * _WindWaveStrength;

			half tipFactor = lerp(1.0, tipUV, _WindTipWeight);
			half highlight = saturate(wave) * tipFactor * _WindHighlightStrength;
			color += _WindHighlightColor.rgb * highlight;
		}

		ENDCG

		Pass
		{
			Name "FORWARD"
			Tags { "LightMode" = "ForwardBase" }

			CGPROGRAM
			#pragma target 5.0
			#pragma vertex vert
			#pragma hull hull
			#pragma domain doma
			#pragma geometry geom
			#pragma fragment frag
			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			#pragma shader_feature_local _MASK_DEBUG_VIEW
			#pragma shader_feature_local _USE_BILLBOARD_QUAD

			fixed4 frag(g2f IN, fixed facing : VFACE) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				#if defined(_MASK_DEBUG_VIEW)
					// マスクの値をそのままグレースケールで表示して、位置合わせを確認できるようにする
					half m = SampleMaskUV(IN.maskUV);
					return half4(m, m, m, 1);
				#endif
				UNITY_LIGHT_ATTENUATION(atten, IN, IN.worldPos)
				half4 baseColor = lerp(UNITY_ACCESS_INSTANCED_PROP(Props, _Color), UNITY_ACCESS_INSTANCED_PROP(Props, _TopColor), IN.uv.y);
				#if defined(_USE_BILLBOARD_QUAD)
					fixed4 tex = tex2D(_GrassTex, TRANSFORM_TEX(IN.uv.xy, _GrassTex));
					clip(tex.a - _Cutoff);
					baseColor *= tex;
				#endif
				half4 c = baseColor * atten;
				c.rgb *= _LightColor0.rgb;
				c.rgb *= 0.5 + 0.5 * saturate(dot(IN.worldNormal * facing, _WorldSpaceLightPos0.xyz));
				#if defined(VRC_LIGHT_VOLUMES_ENABLED)
					c.rgb += baseColor.rgb * IN.lightVolumeColor * _LightVolumeStrength;
				#endif
				ApplyWindLighting(c.rgb, IN.worldPos, IN.uv.y);
				#ifdef LIGHTMAP_ON
					half4 bakedColorTex = UNITY_SAMPLE_TEX2D(unity_Lightmap, IN.uv.zw);
					c.rgb += baseColor.rgb * DecodeLightmap(bakedColorTex);
				#endif
				c.rgb *= _Brightness;
				UNITY_APPLY_FOG(IN.fogCoord, c);
				UNITY_OPAQUE_ALPHA(c.a);
				return c;
			}
			ENDCG
		}

		Pass
		{
			Name "FORWARD"
			Tags { "LightMode" = "ForwardAdd" }
			ZWrite Off
			Blend One One

			CGPROGRAM
			#pragma target 5.0
			#pragma vertex vert
			#pragma hull hull
			#pragma domain doma
			#pragma geometry geom
			#pragma fragment frag
			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			#pragma shader_feature_local _USE_BILLBOARD_QUAD

			fixed4 frag(g2f IN, fixed facing : VFACE) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				#ifndef USING_DIRECTIONAL_LIGHT
					fixed3 lightDir = normalize(UnityWorldSpaceLightDir(IN.worldPos));
				#else
					fixed3 lightDir = _WorldSpaceLightPos0.xyz;
				#endif
				UNITY_LIGHT_ATTENUATION(attenuation, IN, IN.worldPos);
				fixed4 c = lerp(UNITY_ACCESS_INSTANCED_PROP(Props, _Color), UNITY_ACCESS_INSTANCED_PROP(Props, _TopColor), IN.uv.y);
				#if defined(_USE_BILLBOARD_QUAD)
					fixed4 tex = tex2D(_GrassTex, TRANSFORM_TEX(IN.uv.xy, _GrassTex));
					clip(tex.a - _Cutoff);
					c *= tex;
				#endif
				c.rgb *= saturate(dot(IN.worldNormal * facing, lightDir)) * _LightColor0 * attenuation;
				ApplyWindLighting(c.rgb, IN.worldPos, IN.uv.y);
				c.rgb *= _Brightness;
				UNITY_APPLY_FOG(IN.fogCoord, c);
				UNITY_OPAQUE_ALPHA(c.a);
				return c;
			}
			ENDCG
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			CGPROGRAM
			#pragma target 5.0
			#pragma vertex vert
			#pragma hull hull
			#pragma domain doma
			#pragma geometry geom
			#pragma fragment frag
			#pragma multi_compile_shadowcaster
			#pragma multi_compile_instancing
			#pragma shader_feature_local _USE_BILLBOARD_QUAD

			float4 frag(g2f i) : SV_Target
			{
				#if defined(_USE_BILLBOARD_QUAD)
					fixed4 tex = tex2D(_GrassTex, TRANSFORM_TEX(i.uv, _GrassTex));
					clip(tex.a - _Cutoff);
				#endif
				SHADOW_CASTER_FRAGMENT(i)
			}
			ENDCG
		}
	}
}
