// Raymarched fog for the zone around Невишк. Drawn on the back faces of one big box around the whole landscape;
// for each pixel the ray from the camera is marched through the box (stopping at the ground via the camera depth
// texture) through a density field that is:
//   zero inside the ring around the city, so the districts stay clear;
//   a tall, dense bank just outside the ring (the "wall" of the zone), its mist rising and drifting;
//   low murk lying over the countryside beyond, thinning with height above the (smoothed) ground;
//   broken up by two octaves of scrolling 3D noise.
Shader "Nevishk/VolumeFog"
{
    Properties
    {
        _Color ("Mist in shadow", Color) = (0.42, 0.45, 0.44, 1)
        _LitColor ("Mist in light", Color) = (0.72, 0.74, 0.71, 1)
        _Density ("Density (per m)", Float) = 0.011
        _Noise3D ("3D noise", 3D) = "white" {}
        _NoiseScale ("Noise scale (1/m)", Float) = 0.004
        _Wind ("Wind (xyz m/s), rise (w m/s)", Vector) = (5, 0, 2, 3)
        _Speed ("Time scale (0 = still)", Float) = 1
        _Ground ("Smoothed ground height", 2D) = "black" {}
        _GroundST ("Ground uv: scale xy, offset zw", Vector) = (0.0001, 0.0001, 0.5, 0.5)
        _Ring ("Oval centre xy, semi-axes zw", Vector) = (0, 0, 900, 470)
        _RingCorner ("How far the oval's edge wanders (m)", Float) = 120
        _Extent ("Landscape half size xy, fog top z, bottom w", Vector) = (4200, 3800, 260, -60)
        _Height ("Murk scale height (m)", Float) = 32
        _WallHeight ("Wall scale height (m)", Float) = 140
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "VolumeFog"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_Noise3D);  SAMPLER(sampler_Noise3D);
            TEXTURE2D(_Ground);   SAMPLER(sampler_Ground);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _LitColor;
                float _Density;
                float _NoiseScale;
                float4 _Wind;
                float _Speed;
                float4 _GroundST;
                float4 _Ring;
                float _RingCorner;
                float4 _Extent;
                float _Height;
                float _WallHeight;
                float4 _Noise3D_ST;
                float4 _Ground_ST;
            CBUFFER_END

            #define STEPS 40

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            /// Approximate signed distance (m) to the oval around the city: an ellipse whose edge is pushed in and out
            /// by slow, large-scale noise, so the bank has bays and promontories instead of a perfect outline.
            float RingDistance(float2 p, float t)
            {
                float2 q = p - _Ring.xy;
                float2 a = _Ring.zw;
                float f = length(q / a);
                float g = length(q / (a * a));
                float d = (f - 1.0) * f / max(g, 1e-5);
                if (d > -450.0 && d < 700.0)
                {
                    float w = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, float3(p.x, t * 1.2, p.y) * (1.0 / 3200.0), 0).r;
                    d += (w - 0.5) * 2.0 * _RingCorner;       // _RingCorner = how far the edge wanders (m)
                }
                return d;
            }

            float Density(float3 p, float t)
            {
                float sdf = RingDistance(p.xz, t);
                // Clear inside; the bank rises quickly at the oval's edge, then thins out gradually over the
                // next kilometre or so instead of ending in a band; patchy murk takes over beyond it.
                float wall = smoothstep(-30.0, 90.0, sdf) * exp(-max(sdf - 90.0, 0.0) / 520.0);
                float murk = smoothstep(60.0, 420.0, sdf);
                if (murk > 0.002)
                {
                    // Over the country the murk lies in drifting patches: big soft-edged zones of fog and clear air.
                    float z = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D,
                                                   float3(p.x - _Wind.x * t * 0.35, t * 0.8, p.z - _Wind.z * t * 0.35) * (1.0 / 6500.0), 0).r;
                    murk *= smoothstep(0.42, 0.66, z);
                }
                float2 edge = abs(p.xz) / _Extent.xy;
                float amount = max(murk, wall) * (1.0 - smoothstep(0.8, 1.0, max(edge.x, edge.y)));
                if (amount < 0.002) return 0.0;

                float g = SAMPLE_TEXTURE2D_LOD(_Ground, sampler_Ground, p.xz * _GroundST.xy + _GroundST.zw, 0).r;
                float h = max(p.y - g, 0.0);
                float scaleH = lerp(_Height, _WallHeight, wall);
                float vertical = exp(-h / scaleH);
                if (vertical * amount < 0.002) return 0.0;

                // Drifting with the wind, rising faster in the wall.
                float3 drift = float3(_Wind.x, -(_Wind.w * (0.4 + wall)), _Wind.z) * t;
                float n1 = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, (p + drift) * _NoiseScale, 0).r;
                float n2 = SAMPLE_TEXTURE3D_LOD(_Noise3D, sampler_Noise3D, (p - drift * 1.7) * (_NoiseScale * 2.9), 0).r;
                float n = saturate((n1 * 0.65 + n2 * 0.35) * 2.1 - 0.55);
                return _Density * amount * vertical * n;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.positionWS - ro);

                // Where the ray is inside the fog box.
                float3 bmin = float3(-_Extent.x, _Extent.w, -_Extent.y), bmax = float3(_Extent.x, _Extent.z, _Extent.y);
                float3 inv = 1.0 / (abs(rd) > 1e-5 ? rd : 1e-5);
                float3 ta = (bmin - ro) * inv, tb = (bmax - ro) * inv;
                float3 tmin = min(ta, tb), tmax = max(ta, tb);
                float t0 = max(max(max(tmin.x, tmin.y), tmin.z), 0.0);
                float t1 = min(min(tmax.x, tmax.y), tmax.z);

                // Stop at the ground / buildings. A depth implausibly close to the camera means no valid depth texture.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float eye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float sceneDist = eye / max(dot(rd, camFwd), 1e-3);
                if (sceneDist > 1.0) t1 = min(t1, sceneDist);
                if (t1 <= t0) return 0;

                float time = _Time.y * _Speed;
                float dt = (t1 - t0) / STEPS;
                // Per-pixel offset of the first sample hides the step pattern (interleaved gradient noise).
                float jitter = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                float t = t0 + dt * jitter;

                float trans = 1.0;
                float3 col = 0;
                [loop]
                for (int s = 0; s < STEPS; s++)
                {
                    float3 p = ro + rd * t;
                    float d = Density(p, time);
                    if (d > 0.0)
                    {
                        float a = exp(-d * dt);
                        // Brighter toward the top of the mist, where it catches the overcast light.
                        float lit = saturate((p.y - _Extent.w) / (_Extent.z - _Extent.w) * 1.6);
                        float3 c = lerp(_Color.rgb, _LitColor.rgb, lit);
                        col += trans * (1.0 - a) * c;
                        trans *= a;
                        if (trans < 0.02) break;
                    }
                    t += dt;
                }
                return half4(col, 1.0 - trans);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
