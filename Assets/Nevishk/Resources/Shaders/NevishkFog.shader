// Drifting mist for the zone around Невишк: the fog walls that ring the city and the layers of murk over the
// countryside. Unlit and transparent; two scrolling noise layers break it up, it softens where it meets the
// ground (needs the camera depth texture) and thins out right in front of the camera.
// Per vertex: colour alpha = density mask, normal = the direction the vertex sways in, uv = noise coordinates in metres.
Shader "Nevishk/Fog"
{
    Properties
    {
        _Color ("Colour (alpha = density)", Color) = (0.36, 0.38, 0.37, 0.6)
        _NoiseTex ("Noise", 2D) = "white" {}
        _Scale ("Noise scale (1/m)", Float) = 0.004
        _Flow ("Flow in m/s (xy: layer 1, zw: layer 2)", Vector) = (6, 2, -4, 3)
        _Soft ("Soft ground contact (m)", Float) = 30
        _NearFade ("Camera fade: start, range (m)", Vector) = (40, 140, 0, 0)
        _Wave ("Sway (m)", Float) = 10
        _Speed ("Time scale (0 = still)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Fog"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _NoiseTex_ST;
                float _Scale;
                float4 _Flow;
                float _Soft;
                float4 _NearFade;
                float _Wave;
                float _Speed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                half4 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = _Time.y * _Speed;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float sway = sin(t * 0.23 + ws.x * 0.011 + ws.z * 0.007) + sin(t * 0.17 - ws.z * 0.013 + ws.x * 0.004);
                ws += n * (_Wave * 0.5 * sway);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.eyeDepth = -TransformWorldToView(ws).z;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p = i.uv * _Scale;
                half n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p + _Flow.xy * (_Scale * t)).r;
                half n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p * 2.7 + _Flow.zw * (_Scale * t)).r;
                half n = saturate((n1 * 0.65 + n2 * 0.35) * 1.9 - 0.5);

                // Soft where the mist meets the ground or a building.
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                // The hardware depth test already hides fog behind the ground, so a scene depth in FRONT of this
                // fragment means the depth texture isn't valid yet (not copied, or copied after transparents):
                // then skip the softening rather than fading the fog to nothing.
                half soft = sceneEye < i.eyeDepth - 2.0 ? 1.0 : saturate((sceneEye - i.eyeDepth) / max(_Soft, 0.01));

                // Thin out right in front of the camera, so it can be flown through.
                float camDist = distance(_WorldSpaceCameraPos, i.positionWS);
                half nearFade = saturate((camDist - _NearFade.x) / max(_NearFade.y, 0.01));

                // The zone breathes: density rises and falls slowly.
                half breathe = 0.85 + 0.15 * sin(t * 0.11 + i.uv.x * 0.0021);

                half a = _Color.a * i.color.a * n * soft * nearFade * breathe;
                half3 col = _Color.rgb * (0.82 + 0.36 * n1);
                col = MixFog(col, i.fogFactor);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
