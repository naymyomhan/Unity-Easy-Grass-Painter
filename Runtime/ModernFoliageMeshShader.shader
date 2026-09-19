Shader "ModernGrassTool/FoliageMeshShader"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_USE_VERTEX_COLOR)] _UseVertexColor ("Use Vertex Color", Float) = 1
        [Header(Wind Sway)]
        _WindSway ("Wind Sway Strength", Range(0, 1)) = 0.15
        _WindSpeed ("Wind Speed", Range(0, 5)) = 2.0

        [Header(Ground Blending)]
        [Toggle(_GROUND_BLEND_ON)] _EnableGroundBlend ("Enable Ground Blend", Float) = 0
        _GroundBlendFade ("Ground Blend Fade", Range(0, 1)) = 0.5
        _GroundBlendStretch ("Ground Blend Stretch", Range(0.1, 5)) = 1.0
        _GroundBlendBrightness ("Ground Blend Brightness", Range(0, 2)) = 1.0
        _GroundBlendSaturation ("Ground Blend Saturation", Range(0, 2)) = 1.0
        _AmbientAdjustmentColor ("Ambient Adjustment Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _GROUND_BLEND_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                float4 color        : COLOR;
                float fogFactor     : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Cutoff;
                float _UseVertexColor;
                float _WindSway;
                float _WindSpeed;
                float _EnableGroundBlend;
                float _GroundBlendFade;
                float _GroundBlendStretch;
                float _GroundBlendBrightness;
                float _GroundBlendSaturation;
                float4 _AmbientAdjustmentColor;
            CBUFFER_END

            // Global Terrain Diffuse Map (set by ModernRenderTerrainMap)
            sampler2D _TerrainDiffuse;
            float _OrthographicCamSizeTerrain;
            float3 _OrthographicCamPosTerrain;

            // Player Interactors & Walking Trail
            float _EnableInteraction;
            float _InteractionStrength;
            float _InteractionFlatten;
            float _ElasticRecoverySpeed;
            float _ElasticOscillation;
            float _EnableTrailPersistence;
            float _TrailDuration;
            float _TrailDepression;

            int _InteractorCount;
            float4 _Interactors[16];
            float4 _InteractorParams[16];

            int _TrailCount;
            float4 _TrailPoints[32];
            float4 _TrailParams[32];

            float3 ApplyFoliageDisplacement(float3 positionOS, float2 uv, float3 rootWS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);

                // 1. Wind Sway (scaled by height above ground)
                float height = max(0.0, positionOS.y);
                float sway = sin(_Time.y * _WindSpeed + positionWS.x * 0.8 + positionWS.z * 0.8) * _WindSway * height;
                positionWS.x += sway;
                positionWS.z += sway * 0.5;

                // 2. Interactive Player Push & Walking Trail Persistence
                if (_EnableInteraction > 0.5)
                {
                    float3 interactorPush = float3(0, 0, 0);

                    // 2a. Direct Active Interactors
                    if (_InteractorCount > 0)
                    {
                        for (int i = 0; i < _InteractorCount; i++)
                        {
                            float3 interPos = _Interactors[i].xyz;
                            float interRadius = _InteractorParams[i].x;
                            float interStrength = _InteractorParams[i].y * _InteractionStrength;

                            if (interStrength > 0.001 && interRadius > 0.01)
                            {
                                float2 xzOffset = rootWS.xz - interPos.xz;
                                float xzDist = length(xzOffset);
                                float yDist = abs(rootWS.y - (interPos.y - 0.5));

                                if (xzDist < interRadius && yDist < 2.5)
                                {
                                    float2 xzDir = (xzDist > 0.001) ? normalize(xzOffset) : float2(0, 1);
                                    float factor = 1.0 - (xzDist / interRadius);
                                    float pushAmount = smoothstep(0.0, 1.0, factor) * interStrength * (interRadius * 0.85);
                                    float downPush = -lerp(0.2, 0.9, _InteractionFlatten);
                                    float3 pushDir = normalize(float3(xzDir.x, downPush, xzDir.y));
                                    interactorPush += pushDir * pushAmount;
                                }
                            }
                        }
                    }

                    // 2b. Walking Trail Persistence & Elastic Recovery
                    if (_EnableTrailPersistence > 0.5 && _TrailCount > 0)
                    {
                        for (int j = 0; j < _TrailCount; j++)
                        {
                            float4 tPosTime = _TrailPoints[j];
                            float4 tParams = _TrailParams[j];
                            float age = _Time.y - tPosTime.w;

                            if (age >= 0.0 && age < _TrailDuration)
                            {
                                float progress = age / max(_TrailDuration, 0.01);
                                float2 tOffset = rootWS.xz - tPosTime.xz;
                                float tDist = length(tOffset);
                                float tRadius = tParams.x;
                                float tStrength = tParams.y * _InteractionStrength;

                                if (tDist < tRadius)
                                {
                                    float falloff = 1.0 - (tDist / tRadius);
                                    float factor = smoothstep(0.0, 1.0, falloff);
                                    float decay = pow(saturate(1.0 - progress), max(0.2, 2.0 * _ElasticRecoverySpeed));
                                    float bounce = sin(progress * 6.28318 * _ElasticRecoverySpeed) * exp(-progress * 4.0) * _ElasticOscillation;
                                    float netFactor = saturate(decay + bounce) * factor * tStrength;

                                    float2 walkDir = tParams.zw;
                                    float2 pushXZ = (length(walkDir) > 0.1) ? normalize(walkDir * 0.7 + normalize(tOffset + float2(0.0001, 0.0001)) * 0.3) : normalize(tOffset + float2(0.0001, 0.0001));
                                    float trailDown = -lerp(0.3, 0.95, _TrailDepression);
                                    float3 trailPushDir = normalize(float3(pushXZ.x, trailDown, pushXZ.y));
                                    interactorPush += trailPushDir * (netFactor * tRadius * 0.7);
                                }
                            }
                        }
                    }

                    // Bend factor increases with height (root stays grounded)
                    float bendFactor = saturate(uv.y);
                    float pushWeight = bendFactor * bendFactor;
                    positionWS += interactorPush * pushWeight;
                }

                return positionWS;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 positionWS = ApplyFoliageDisplacement(input.positionOS.xyz, input.uv, rootWS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.uv = input.uv;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(texColor.a - _Cutoff);

                half4 baseCol = _BaseColor * texColor;
                if (_UseVertexColor > 0.5)
                {
                    baseCol *= input.color;
                }

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half NdotL = saturate(dot(input.normalWS, mainLight.direction));
                half3 direct = mainLight.color * (NdotL * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(input.normalWS);

                #if defined(_GROUND_BLEND_ON)
                    half3 groundNormalWS = half3(0.0, 1.0, 0.0);
                    half groundNdotL = saturate(dot(groundNormalWS, mainLight.direction));
                    half3 groundDirect = mainLight.color * (groundNdotL * mainLight.shadowAttenuation);
                    half3 groundAmbient = SampleSH(groundNormalWS);
                #endif

                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.positionCS = input.positionCS;
                    inputData.normalWS = input.normalWS;
                    inputData.viewDirectionWS = normalize(GetCameraPositionWS() - input.positionWS);
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    inputData.shadowMask = half4(1, 1, 1, 1);

                    uint pixelLightCount = GetAdditionalLightsCount();

                    #if USE_FORWARD_PLUS
                    for (uint dirLightIndex = 0u; dirLightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++dirLightIndex)
                    {
                        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                        Light addLight = GetAdditionalLight(dirLightIndex, inputData.positionWS, inputData.shadowMask);
                        #ifdef _LIGHT_LAYERS
                            if (!IsMatchingLightLayer(addLight.layerMask, 1)) continue;
                        #endif

                        half addNdotL = saturate(dot(input.normalWS, addLight.direction));
                        direct += addLight.color * (addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation);
                    }
                    #endif

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                        #ifdef _LIGHT_LAYERS
                            if (!IsMatchingLightLayer(addLight.layerMask, 1)) continue;
                        #endif

                        half addNdotL = saturate(dot(input.normalWS, addLight.direction));
                        direct += addLight.color * (addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation);
                    LIGHT_LOOP_END
                #endif

                half3 finalRGB = baseCol.rgb * (direct + ambient);

                #if defined(_GROUND_BLEND_ON)
                    float verticalFade = saturate((input.uv.y + _GroundBlendFade) * _GroundBlendStretch);
                    if (verticalFade < 0.999)
                    {
                        float2 groundUV = (input.positionWS.xz - _OrthographicCamPosTerrain.xz) / max(0.001, _OrthographicCamSizeTerrain * 2.0) + 0.5;
                        float4 terrainColor = tex2Dlod(_TerrainDiffuse, float4(groundUV, 0, 0));

                        float groundLuma = dot(terrainColor.rgb, float3(0.2126, 0.7152, 0.0722));
                        float3 groundAdj = lerp(float3(groundLuma, groundLuma, groundLuma), terrainColor.rgb, _GroundBlendSaturation) * _GroundBlendBrightness;

                        half directLuma = dot(groundDirect, half3(0.299, 0.587, 0.114));
                        half ambientLuma = dot(groundAmbient, half3(0.299, 0.587, 0.114));
                        half groundNightFactor = saturate(directLuma * 2.0 + ambientLuma * 5.0);
                        float3 groundLit = groundAdj * groundNightFactor;
                        finalRGB = lerp(groundLit, finalRGB * _AmbientAdjustmentColor.rgb, verticalFade);
                    }
                    else
                    {
                        finalRGB = finalRGB * _AmbientAdjustmentColor.rgb;
                    }
                #endif

                finalRGB = MixFog(finalRGB, input.fogFactor);

                return half4(finalRGB, baseCol.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct AttributesShadow
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsShadow
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Cutoff;
                float _UseVertexColor;
                float _WindSway;
                float _WindSpeed;
            CBUFFER_END

            // Player Interactors & Walking Trail
            float _EnableInteraction;
            float _InteractionStrength;
            float _InteractionFlatten;
            float _ElasticRecoverySpeed;
            float _ElasticOscillation;
            float _EnableTrailPersistence;
            float _TrailDuration;
            float _TrailDepression;

            int _InteractorCount;
            float4 _Interactors[16];
            float4 _InteractorParams[16];

            int _TrailCount;
            float4 _TrailPoints[32];
            float4 _TrailParams[32];

            float3 ApplyFoliageDisplacementShadow(float3 positionOS, float2 uv, float3 rootWS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);

                float height = max(0.0, positionOS.y);
                float sway = sin(_Time.y * _WindSpeed + positionWS.x * 0.8 + positionWS.z * 0.8) * _WindSway * height;
                positionWS.x += sway;
                positionWS.z += sway * 0.5;

                // 2. Interactive Player Push & Walking Trail Persistence
                if (_EnableInteraction > 0.5)
                {
                    float3 interactorPush = float3(0, 0, 0);

                    // 2a. Direct Active Interactors
                    if (_InteractorCount > 0)
                    {
                        for (int i = 0; i < _InteractorCount; i++)
                        {
                            float3 interPos = _Interactors[i].xyz;
                            float interRadius = _InteractorParams[i].x;
                            float interStrength = _InteractorParams[i].y * _InteractionStrength;

                            if (interStrength > 0.001 && interRadius > 0.01)
                            {
                                float2 xzOffset = rootWS.xz - interPos.xz;
                                float xzDist = length(xzOffset);
                                float yDist = abs(rootWS.y - (interPos.y - 0.5));

                                if (xzDist < interRadius && yDist < 2.5)
                                {
                                    float2 xzDir = (xzDist > 0.001) ? normalize(xzOffset) : float2(0, 1);
                                    float factor = 1.0 - (xzDist / interRadius);
                                    float pushAmount = smoothstep(0.0, 1.0, factor) * interStrength * (interRadius * 0.85);
                                    float downPush = -lerp(0.2, 0.9, _InteractionFlatten);
                                    float3 pushDir = normalize(float3(xzDir.x, downPush, xzDir.y));
                                    interactorPush += pushDir * pushAmount;
                                }
                            }
                        }
                    }

                    // 2b. Walking Trail Persistence & Elastic Recovery
                    if (_EnableTrailPersistence > 0.5 && _TrailCount > 0)
                    {
                        for (int j = 0; j < _TrailCount; j++)
                        {
                            float4 tPosTime = _TrailPoints[j];
                            float4 tParams = _TrailParams[j];
                            float age = _Time.y - tPosTime.w;

                            if (age >= 0.0 && age < _TrailDuration)
                            {
                                float progress = age / max(_TrailDuration, 0.01);
                                float2 tOffset = rootWS.xz - tPosTime.xz;
                                float tDist = length(tOffset);
                                float tRadius = tParams.x;
                                float tStrength = tParams.y * _InteractionStrength;

                                if (tDist < tRadius)
                                {
                                    float falloff = 1.0 - (tDist / tRadius);
                                    float factor = smoothstep(0.0, 1.0, falloff);
                                    float decay = pow(saturate(1.0 - progress), max(0.2, 2.0 * _ElasticRecoverySpeed));
                                    float bounce = sin(progress * 6.28318 * _ElasticRecoverySpeed) * exp(-progress * 4.0) * _ElasticOscillation;
                                    float netFactor = saturate(decay + bounce) * factor * tStrength;

                                    float2 walkDir = tParams.zw;
                                    float2 pushXZ = (length(walkDir) > 0.1) ? normalize(walkDir * 0.7 + normalize(tOffset + float2(0.0001, 0.0001)) * 0.3) : normalize(tOffset + float2(0.0001, 0.0001));
                                    float trailDown = -lerp(0.3, 0.95, _TrailDepression);
                                    float3 trailPushDir = normalize(float3(pushXZ.x, trailDown, pushXZ.y));
                                    interactorPush += trailPushDir * (netFactor * tRadius * 0.7);
                                }
                            }
                        }
                    }

                    float bendFactor = saturate(uv.y);
                    float pushWeight = bendFactor * bendFactor;
                    positionWS += interactorPush * pushWeight;
                }

                return positionWS;
            }

            VaryingsShadow vertShadow(AttributesShadow input)
            {
                VaryingsShadow output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 rootWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 positionWS = ApplyFoliageDisplacementShadow(input.positionOS.xyz, input.uv, rootWS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, GetMainLight().direction));
                output.uv = input.uv;
                return output;
            }

            half4 fragShadow(VaryingsShadow input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(texColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
