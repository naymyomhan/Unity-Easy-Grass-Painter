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

            // Player Interactors & Spring Wobble
            float _EnableInteraction;
            float _ElasticOscillation;

            int _InteractorCount;
            float4 _Interactors[16];
            float4 _InteractorParams[16];

            int _ImpulseCount;
            float4 _ImpulsePoints[32];
            float4 _ImpulseParams[32];

            int _ShockwaveCount;
            float4 _ShockwaveOrigins[8];
            float4 _ShockwaveParams[8];

            int _WindZoneCount;
            float4 _WindZoneOrigins[8];
            float4 _WindZoneVectors[8];
            float4 _WindZoneParams[8];
            float4 _WindZoneTimes[8];

            float3 ApplyFoliageDisplacement(float3 positionOS, float2 uv, float3 rootWS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);

                // 1. Wind Sway (scaled by height above ground)
                float height = max(0.0, positionOS.y);
                float sway = sin(_Time.y * _WindSpeed + positionWS.x * 0.8 + positionWS.z * 0.8) * _WindSway * height;
                positionWS.x += sway;
                positionWS.z += sway * 0.5;

                // 2. Interactive Player Push & Spring Wobble
                if (_EnableInteraction > 0.5)
                {
                    float3 interactorPush = float3(0, 0, 0);
                    float minContactDist = 999.0;

                    // Direct Contact Push (while inside player radius)
                    if (_InteractorCount > 0)
                    {
                        for (int i = 0; i < _InteractorCount; i++)
                        {
                            float3 interPos = _Interactors[i].xyz;
                            float interRadius = _InteractorParams[i].x;
                            float interStrength = _InteractorParams[i].y;

                            if (interStrength > 0.001 && interRadius > 0.01)
                            {
                                float2 xzOffset = rootWS.xz - interPos.xz;
                                float xzDist = length(xzOffset);
                                float yDist = abs(rootWS.y - (interPos.y - 0.5));

                                if (yDist < 2.5 && xzDist < interRadius)
                                {
                                    float factor = 1.0 - (xzDist / interRadius);
                                    float pushAmount = smoothstep(0.0, 1.0, factor) * interStrength * (interRadius * 0.85);
                                    float2 xzDir = (xzDist > 0.001) ? (xzOffset / xzDist) : float2(0, 1);
                                    float3 pushDir = normalize(float3(xzDir.x, -0.4, xzDir.y));
                                    interactorPush += pushDir * pushAmount;

                                    float relDist = xzDist / interRadius;
                                    if (relDist < minContactDist) minContactDist = relDist;
                                }
                            }
                        }
                    }

                    // Physical Impulse Oscillation (independent of player distance)
                    if (_ElasticOscillation > 0.01 && _ImpulseCount > 0)
                    {
                        float maxWeight = 0.0;
                        float3 impulseWobble = float3(0, 0, 0);
                        float hash = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);

                        for (int k = 0; k < _ImpulseCount; k++)
                        {
                            float3 impPos = _ImpulsePoints[k].xyz;
                            float impTime = _ImpulsePoints[k].w;
                            float2 impDir = _ImpulseParams[k].xy;
                            float impRadius = _ImpulseParams[k].z;
                            float impDuration = _ImpulseParams[k].w;

                            float age = _Time.y - impTime;
                            if (age >= 0.0 && age < impDuration)
                            {
                                float2 diff = rootWS.xz - impPos.xz;
                                float distSq = dot(diff, diff);
                                float radiusSq = impRadius * impRadius;

                                if (distSq < radiusSq)
                                {
                                    float yDist = abs(rootWS.y - impPos.y);
                                    if (yDist < 2.5)
                                    {
                                        float dist = sqrt(distSq);
                                        float distFactor = 1.0 - (dist / impRadius);
                                        float progress = age / impDuration;

                                        float decay = exp(-progress * 3.2) * (1.0 - progress);
                                        float weight = distFactor * decay;

                                        if (weight > maxWeight)
                                        {
                                            maxWeight = weight;
                                            float freq = 11.5 + (hash - 0.5) * 2.0;
                                            float sinWave = sin(age * freq);
                                            float amp = distFactor * decay * min(0.6, 0.08 + _ElasticOscillation * 0.06);

                                            float2 pushDir2D = (length(impDir) > 0.1) ? impDir : ((dist > 0.001) ? (diff / dist) : float2(0, 1));
                                            impulseWobble = float3(pushDir2D.x, 0.0, pushDir2D.y) * (sinWave * amp);
                                        }
                                    }
                                }
                            }
                        }

                        float contactSuppression = (minContactDist < 1.0) ? smoothstep(0.4, 1.0, minContactDist) : 1.0;
                        interactorPush += impulseWobble * contactSuppression;
                    }

                    // 3. Explosion & Shockwave Expanding Ring Physics
                    if (_ShockwaveCount > 0)
                    {
                        float3 shockwavePush = float3(0, 0, 0);
                        float hash = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);

                        for (int s = 0; s < _ShockwaveCount; s++)
                        {
                            float3 sOrigin = _ShockwaveOrigins[s].xyz;
                            float sStartTime = _ShockwaveOrigins[s].w;
                            float sRadius = _ShockwaveParams[s].x;
                            float sSpeed = _ShockwaveParams[s].y;
                            float sForce = _ShockwaveParams[s].z;
                            float sThickness = _ShockwaveParams[s].w;

                            float elapsed = _Time.y - sStartTime;
                            float currentRadius = elapsed * sSpeed;
                            float yDiff = abs(rootWS.y - sOrigin.y);

                            if (yDiff < 4.0 && elapsed > 0.0)
                            {
                                float2 diff = rootWS.xz - sOrigin.xz;
                                float dist = length(diff);

                                if (dist <= sRadius)
                                {
                                    float2 dir = (dist > 0.001) ? (diff / dist) : float2(0, 1);
                                    float distAttenuation = 1.0 - (dist / sRadius);

                                    float distToFront = dist - currentRadius;
                                    if (abs(distToFront) < sThickness)
                                    {
                                        float crestFactor = 1.0 - (abs(distToFront) / sThickness);
                                        float blastPower = smoothstep(0.0, 1.0, crestFactor) * sForce * distAttenuation;
                                        float3 blastDir = normalize(float3(dir.x, -0.6, dir.y));
                                        shockwavePush += blastDir * blastPower;
                                    }
                                    else if (currentRadius > dist)
                                    {
                                        float hitTime = dist / max(1.0, sSpeed);
                                        float age = elapsed - hitTime;
                                        float recoverDuration = 1.6;

                                        if (age > 0.0 && age < recoverDuration)
                                        {
                                            float progress = age / recoverDuration;
                                            float decay = exp(-progress * 3.5) * (1.0 - progress);
                                            float freq = 13.0 + (hash - 0.5) * 2.0;
                                            float wave = sin(age * freq);
                                            float recoilAmp = sForce * distAttenuation * 0.4 * decay;
                                            shockwavePush += float3(dir.x, 0.0, dir.y) * (wave * recoilAmp);
                                        }
                                    }
                                }
                            }
                        }
                        interactorPush += shockwavePush;
                    }

                    // Bend factor increases with height (root stays grounded)
                    float bendFactor = saturate(uv.y);
                    float pushWeight = bendFactor * bendFactor;
                    positionWS += interactorPush * pushWeight;
                }

                // Dynamic Wind Zones & Blasts (Directional Cones & 360 Rotor Wash)
                if (_WindZoneCount > 0)
                {
                    float3 wzOffset = float3(0, 0, 0);
                    float hash = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);
                    for (int wz = 0; wz < _WindZoneCount; wz++)
                    {
                        float3 wzOrigin = _WindZoneOrigins[wz].xyz;
                        float wzMode = _WindZoneOrigins[wz].w;
                        float3 wzDir = _WindZoneVectors[wz].xyz;
                        float cosAngleThreshold = _WindZoneVectors[wz].w;
                        float wzRadius = _WindZoneParams[wz].x;
                        float wzForce = _WindZoneParams[wz].y;
                        float flutterSpeed = _WindZoneParams[wz].z;
                        float vertRange = _WindZoneParams[wz].w;
                        float recoilStartTime = _WindZoneTimes[wz].x;
                        float recoilDuration = _WindZoneTimes[wz].y;

                        if (wzForce > 0.001 && wzRadius > 0.1)
                        {
                            if (recoilStartTime >= 0.0) // Post-wind release: Damped Harmonic Spring Oscillation!
                            {
                                float age = _Time.y - recoilStartTime;
                                if (age >= 0.0 && age < recoilDuration)
                                {
                                    float progress = age / recoilDuration;
                                    float decay = exp(-progress * 3.5) * (1.0 - progress);
                                    float freq = 12.5 + (hash - 0.5) * 2.0;
                                    float wave = cos(age * freq);
                                    float springFactor = wave * decay;

                                    if (wzMode < 0.5) // Directional Recoil
                                    {
                                        float3 toBlade = rootWS - wzOrigin;
                                        float dist = length(toBlade);
                                        if (dist > 0.01 && dist < wzRadius)
                                        {
                                            float3 toBladeDir = toBlade / dist;
                                            float dotFwd = dot(toBladeDir, wzDir);
                                            if (dotFwd >= cosAngleThreshold)
                                            {
                                                float angleFalloff = smoothstep(cosAngleThreshold, 1.0, dotFwd);
                                                float distFalloff = smoothstep(0.0, 1.0, 1.0 - (dist / wzRadius));
                                                wzOffset += wzDir * (wzForce * distFalloff * angleFalloff * springFactor);
                                            }
                                        }
                                    }
                                    else // Omnidirectional 360 Radial Recoil
                                    {
                                        float2 xzOffset = rootWS.xz - wzOrigin.xz;
                                        float horizontalDist = length(xzOffset);
                                        float verticalOffset = wzOrigin.y - rootWS.y;

                                        if (horizontalDist < wzRadius && verticalOffset >= -2.0 && verticalOffset <= vertRange)
                                        {
                                            float2 radialDir = (horizontalDist > 0.001) ? (xzOffset / horizontalDist) : float2(0, 1);
                                            float vertAtten = 1.0 - saturate(abs(verticalOffset) / max(0.1, vertRange));
                                            float horizAtten = smoothstep(1.0, 0.0, horizontalDist / wzRadius);
                                            float3 pushDir = normalize(float3(radialDir.x, -0.4, radialDir.y));
                                            wzOffset += pushDir * (wzForce * horizAtten * vertAtten * springFactor);
                                        }
                                    }
                                }
                            }
                            else // Active continuous wind blowing
                            {
                                if (wzMode < 0.5) // Directional
                                {
                                    float3 toBlade = rootWS - wzOrigin;
                                    float dist = length(toBlade);
                                    if (dist > 0.01 && dist < wzRadius)
                                    {
                                        float3 toBladeDir = toBlade / dist;
                                        float dotFwd = dot(toBladeDir, wzDir);
                                        if (dotFwd >= cosAngleThreshold)
                                        {
                                            float angleFalloff = smoothstep(cosAngleThreshold, 1.0, dotFwd);
                                            float distFalloff = smoothstep(0.0, 1.0, 1.0 - (dist / wzRadius));
                                            float streamPhase = _Time.y * flutterSpeed - dist * 2.5;
                                            float flutter = 0.85 + 0.35 * sin(streamPhase);
                                            wzOffset += wzDir * (wzForce * distFalloff * angleFalloff * flutter);
                                        }
                                    }
                                }
                                else // Omnidirectional 360 Radial Wash
                                {
                                    float2 xzOffset = rootWS.xz - wzOrigin.xz;
                                    float horizontalDist = length(xzOffset);
                                    float verticalOffset = wzOrigin.y - rootWS.y;

                                    if (horizontalDist < wzRadius && verticalOffset >= -2.0 && verticalOffset <= vertRange)
                                    {
                                        float2 radialDir = (horizontalDist > 0.001) ? (xzOffset / horizontalDist) : float2(0, 1);
                                        float vertAtten = 1.0 - saturate(abs(verticalOffset) / max(0.1, vertRange));
                                        float horizAtten = smoothstep(1.0, 0.0, horizontalDist / wzRadius);
                                        float ripplePhase = _Time.y * flutterSpeed - horizontalDist * 3.2;
                                        float washRipple = 0.8 + 0.3 * sin(ripplePhase);
                                        float3 pushDir = normalize(float3(radialDir.x, -0.4, radialDir.y));
                                        wzOffset += pushDir * (wzForce * horizAtten * vertAtten * washRipple);
                                    }
                                }
                            }
                        }
                    }
                    positionWS += wzOffset * saturate(uv.y);
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

            // Player Interactors & Spring Wobble
            float _EnableInteraction;
            float _ElasticOscillation;

            int _InteractorCount;
            float4 _Interactors[16];
            float4 _InteractorParams[16];

            int _ImpulseCount;
            float4 _ImpulsePoints[32];
            float4 _ImpulseParams[32];

            int _ShockwaveCount;
            float4 _ShockwaveOrigins[8];
            float4 _ShockwaveParams[8];

            int _WindZoneCount;
            float4 _WindZoneOrigins[8];
            float4 _WindZoneVectors[8];
            float4 _WindZoneParams[8];
            float4 _WindZoneTimes[8];

            float3 ApplyFoliageDisplacementShadow(float3 positionOS, float2 uv, float3 rootWS)
            {
                float3 positionWS = TransformObjectToWorld(positionOS);

                float height = max(0.0, positionOS.y);
                float sway = sin(_Time.y * _WindSpeed + positionWS.x * 0.8 + positionWS.z * 0.8) * _WindSway * height;
                positionWS.x += sway;
                positionWS.z += sway * 0.5;

                // 2. Interactive Player Push & Spring Wobble
                if (_EnableInteraction > 0.5)
                {
                    float3 interactorPush = float3(0, 0, 0);
                    float minContactDist = 999.0;

                    // Direct Contact Push (while inside player radius)
                    if (_InteractorCount > 0)
                    {
                        for (int i = 0; i < _InteractorCount; i++)
                        {
                            float3 interPos = _Interactors[i].xyz;
                            float interRadius = _InteractorParams[i].x;
                            float interStrength = _InteractorParams[i].y;

                            if (interStrength > 0.001 && interRadius > 0.01)
                            {
                                float2 xzOffset = rootWS.xz - interPos.xz;
                                float xzDist = length(xzOffset);
                                float yDist = abs(rootWS.y - (interPos.y - 0.5));

                                if (yDist < 2.5 && xzDist < interRadius)
                                {
                                    float factor = 1.0 - (xzDist / interRadius);
                                    float pushAmount = smoothstep(0.0, 1.0, factor) * interStrength * (interRadius * 0.85);
                                    float2 xzDir = (xzDist > 0.001) ? (xzOffset / xzDist) : float2(0, 1);
                                    float3 pushDir = normalize(float3(xzDir.x, -0.4, xzDir.y));
                                    interactorPush += pushDir * pushAmount;

                                    float relDist = xzDist / interRadius;
                                    if (relDist < minContactDist) minContactDist = relDist;
                                }
                            }
                        }
                    }

                    // Physical Impulse Oscillation (independent of player distance)
                    if (_ElasticOscillation > 0.01 && _ImpulseCount > 0)
                    {
                        float maxWeight = 0.0;
                        float3 impulseWobble = float3(0, 0, 0);
                        float hash = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);

                        for (int k = 0; k < _ImpulseCount; k++)
                        {
                            float3 impPos = _ImpulsePoints[k].xyz;
                            float impTime = _ImpulsePoints[k].w;
                            float2 impDir = _ImpulseParams[k].xy;
                            float impRadius = _ImpulseParams[k].z;
                            float impDuration = _ImpulseParams[k].w;

                            float age = _Time.y - impTime;
                            if (age >= 0.0 && age < impDuration)
                            {
                                float2 diff = rootWS.xz - impPos.xz;
                                float distSq = dot(diff, diff);
                                float radiusSq = impRadius * impRadius;

                                if (distSq < radiusSq)
                                {
                                    float yDist = abs(rootWS.y - impPos.y);
                                    if (yDist < 2.5)
                                    {
                                        float dist = sqrt(distSq);
                                        float distFactor = 1.0 - (dist / impRadius);
                                        float progress = age / impDuration;

                                        float decay = exp(-progress * 3.2) * (1.0 - progress);
                                        float weight = distFactor * decay;

                                        if (weight > maxWeight)
                                        {
                                            maxWeight = weight;
                                            float freq = 11.5 + (hash - 0.5) * 2.0;
                                            float sinWave = sin(age * freq);
                                            float amp = distFactor * decay * min(0.6, 0.08 + _ElasticOscillation * 0.06);

                                            float2 pushDir2D = (length(impDir) > 0.1) ? impDir : ((dist > 0.001) ? (diff / dist) : float2(0, 1));
                                            impulseWobble = float3(pushDir2D.x, 0.0, pushDir2D.y) * (sinWave * amp);
                                        }
                                    }
                                }
                            }
                        }

                        float contactSuppression = (minContactDist < 1.0) ? smoothstep(0.4, 1.0, minContactDist) : 1.0;
                        interactorPush += impulseWobble * contactSuppression;
                    }

                    // 3. Explosion & Shockwave Expanding Ring Physics
                    if (_ShockwaveCount > 0)
                    {
                        float3 shockwavePush = float3(0, 0, 0);
                        float hash = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);

                        for (int s = 0; s < _ShockwaveCount; s++)
                        {
                            float3 sOrigin = _ShockwaveOrigins[s].xyz;
                            float sStartTime = _ShockwaveOrigins[s].w;
                            float sRadius = _ShockwaveParams[s].x;
                            float sSpeed = _ShockwaveParams[s].y;
                            float sForce = _ShockwaveParams[s].z;
                            float sThickness = _ShockwaveParams[s].w;

                            float elapsed = _Time.y - sStartTime;
                            float currentRadius = elapsed * sSpeed;
                            float yDiff = abs(rootWS.y - sOrigin.y);

                            if (yDiff < 4.0 && elapsed > 0.0)
                            {
                                float2 diff = rootWS.xz - sOrigin.xz;
                                float dist = length(diff);

                                if (dist <= sRadius)
                                {
                                    float2 dir = (dist > 0.001) ? (diff / dist) : float2(0, 1);
                                    float distAttenuation = 1.0 - (dist / sRadius);

                                    float distToFront = dist - currentRadius;
                                    if (abs(distToFront) < sThickness)
                                    {
                                        float crestFactor = 1.0 - (abs(distToFront) / sThickness);
                                        float blastPower = smoothstep(0.0, 1.0, crestFactor) * sForce * distAttenuation;
                                        float3 blastDir = normalize(float3(dir.x, -0.6, dir.y));
                                        shockwavePush += blastDir * blastPower;
                                    }
                                    else if (currentRadius > dist)
                                    {
                                        float hitTime = dist / max(1.0, sSpeed);
                                        float age = elapsed - hitTime;
                                        float recoverDuration = 1.6;

                                        if (age > 0.0 && age < recoverDuration)
                                        {
                                            float progress = age / recoverDuration;
                                            float decay = exp(-progress * 3.5) * (1.0 - progress);
                                            float freq = 13.0 + (hash - 0.5) * 2.0;
                                            float wave = sin(age * freq);
                                            float recoilAmp = sForce * distAttenuation * 0.4 * decay;
                                            shockwavePush += float3(dir.x, 0.0, dir.y) * (wave * recoilAmp);
                                        }
                                    }
                                }
                            }
                        }
                        interactorPush += shockwavePush;
                    }

                    float bendFactor = saturate(uv.y);
                    float pushWeight = bendFactor * bendFactor;
                    positionWS += interactorPush * pushWeight;
                }

                // Dynamic Wind Zones & Blasts (Directional Cones & 360 Rotor Wash)
                if (_WindZoneCount > 0)
                {
                    float3 wzOffset = float3(0, 0, 0);
                    float hash = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);
                    for (int wz = 0; wz < _WindZoneCount; wz++)
                    {
                        float3 wzOrigin = _WindZoneOrigins[wz].xyz;
                        float wzMode = _WindZoneOrigins[wz].w;
                        float3 wzDir = _WindZoneVectors[wz].xyz;
                        float cosAngleThreshold = _WindZoneVectors[wz].w;
                        float wzRadius = _WindZoneParams[wz].x;
                        float wzForce = _WindZoneParams[wz].y;
                        float flutterSpeed = _WindZoneParams[wz].z;
                        float vertRange = _WindZoneParams[wz].w;
                        float recoilStartTime = _WindZoneTimes[wz].x;
                        float recoilDuration = _WindZoneTimes[wz].y;

                        if (wzForce > 0.001 && wzRadius > 0.1)
                        {
                            if (recoilStartTime >= 0.0) // Post-wind release: Damped Harmonic Spring Oscillation!
                            {
                                float age = _Time.y - recoilStartTime;
                                if (age >= 0.0 && age < recoilDuration)
                                {
                                    float progress = age / recoilDuration;
                                    float decay = exp(-progress * 3.5) * (1.0 - progress);
                                    float freq = 12.5 + (hash - 0.5) * 2.0;
                                    float wave = cos(age * freq);
                                    float springFactor = wave * decay;

                                    if (wzMode < 0.5) // Directional Recoil
                                    {
                                        float3 toBlade = rootWS - wzOrigin;
                                        float dist = length(toBlade);
                                        if (dist > 0.01 && dist < wzRadius)
                                        {
                                            float3 toBladeDir = toBlade / dist;
                                            float dotFwd = dot(toBladeDir, wzDir);
                                            if (dotFwd >= cosAngleThreshold)
                                            {
                                                float angleFalloff = smoothstep(cosAngleThreshold, 1.0, dotFwd);
                                                float distFalloff = smoothstep(0.0, 1.0, 1.0 - (dist / wzRadius));
                                                wzOffset += wzDir * (wzForce * distFalloff * angleFalloff * springFactor);
                                            }
                                        }
                                    }
                                    else // Omnidirectional 360 Radial Recoil
                                    {
                                        float2 xzOffset = rootWS.xz - wzOrigin.xz;
                                        float horizontalDist = length(xzOffset);
                                        float verticalOffset = wzOrigin.y - rootWS.y;

                                        if (horizontalDist < wzRadius && verticalOffset >= -2.0 && verticalOffset <= vertRange)
                                        {
                                            float2 radialDir = (horizontalDist > 0.001) ? (xzOffset / horizontalDist) : float2(0, 1);
                                            float vertAtten = 1.0 - saturate(abs(verticalOffset) / max(0.1, vertRange));
                                            float horizAtten = smoothstep(1.0, 0.0, horizontalDist / wzRadius);
                                            float3 pushDir = normalize(float3(radialDir.x, -0.4, radialDir.y));
                                            wzOffset += pushDir * (wzForce * horizAtten * vertAtten * springFactor);
                                        }
                                    }
                                }
                            }
                            else // Active continuous wind blowing
                            {
                                if (wzMode < 0.5) // Directional
                                {
                                    float3 toBlade = rootWS - wzOrigin;
                                    float dist = length(toBlade);
                                    if (dist > 0.01 && dist < wzRadius)
                                    {
                                        float3 toBladeDir = toBlade / dist;
                                        float dotFwd = dot(toBladeDir, wzDir);
                                        if (dotFwd >= cosAngleThreshold)
                                        {
                                            float angleFalloff = smoothstep(cosAngleThreshold, 1.0, dotFwd);
                                            float distFalloff = smoothstep(0.0, 1.0, 1.0 - (dist / wzRadius));
                                            float streamPhase = _Time.y * flutterSpeed - dist * 2.5;
                                            float flutter = 0.85 + 0.35 * sin(streamPhase);
                                            wzOffset += wzDir * (wzForce * distFalloff * angleFalloff * flutter);
                                        }
                                    }
                                }
                                else // Omnidirectional 360 Radial Wash
                                {
                                    float2 xzOffset = rootWS.xz - wzOrigin.xz;
                                    float horizontalDist = length(xzOffset);
                                    float verticalOffset = wzOrigin.y - rootWS.y;

                                    if (horizontalDist < wzRadius && verticalOffset >= -2.0 && verticalOffset <= vertRange)
                                    {
                                        float2 radialDir = (horizontalDist > 0.001) ? (xzOffset / horizontalDist) : float2(0, 1);
                                        float vertAtten = 1.0 - saturate(abs(verticalOffset) / max(0.1, vertRange));
                                        float horizAtten = smoothstep(1.0, 0.0, horizontalDist / wzRadius);
                                        float ripplePhase = _Time.y * flutterSpeed - horizontalDist * 3.2;
                                        float washRipple = 0.8 + 0.3 * sin(ripplePhase);
                                        float3 pushDir = normalize(float3(radialDir.x, -0.4, radialDir.y));
                                        wzOffset += pushDir * (wzForce * horizAtten * vertAtten * washRipple);
                                    }
                                }
                            }
                        }
                    }
                    positionWS += wzOffset * saturate(uv.y);
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
