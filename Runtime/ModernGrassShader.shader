Shader "ModernGrassTool/ModernGrassShader"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Translucency ("Translucency", Range(0, 1)) = 0.4
        _EdgeHighlight ("Edge Highlight", Range(0, 1)) = 0.4

        [Toggle(_GROUND_BLEND_ON)] _EnableGroundBlend ("Enable Ground Blend", Float) = 0
        _GroundBlendFade ("Ground Blend Fade", Range(-1, 2)) = 0.0
        _GroundBlendStretch ("Ground Blend Stretch", Range(0.1, 5)) = 2.5
        _GroundBlendBrightness ("Ground Blend Brightness", Range(0, 2)) = 1.0
        _GroundBlendSaturation ("Ground Blend Saturation", Range(0, 2)) = 1.0
        _AmbientAdjustmentColor ("Ambient Adjustment Color", Color) = (1, 1, 1, 1)

        [Header(Blade Texture)]
        [Toggle(_USE_BLADE_TEXTURE)] _UseBladeTexture ("Use Blade Texture", Float) = 0
        _MainTex ("Blade Texture (RGBA)", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Range(0.01, 1)) = 0.5
        _BlendWithBladeColor ("Blend With Blade Color", Float) = 1.0

        [Header(Shadow Settings)]
        _ShadowCastDensity ("Shadow Cast Density", Range(0, 1)) = 0.45
        _BladeShadowStrength ("Blade Shadow Strength", Range(0, 1)) = 0.5

        [Header(Blade Shape)]
        _TipShape ("Tip Shape", Float) = 0
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
            #pragma target 4.5

            #pragma multi_compile_local _ _GROUND_BLEND_ON
            #pragma multi_compile_local _ _USE_BLADE_TEXTURE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct DrawVertex
            {
                float3 positionWS;
                float3 normalWS;
                float2 uv;
                float4 color;
                float cutHeight;
                float3 _pad;
            };

            StructuredBuffer<DrawVertex> _DrawVertices;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AmbientAdjustmentColor;
                float _Translucency;
                float _EdgeHighlight;
                float _GroundBlendFade;
                float _GroundBlendStretch;
                float _GroundBlendBrightness;
                float _GroundBlendSaturation;
                float _EnableGroundBlend;
                float _ShadowCastDensity;
                float _BladeShadowStrength;
                float _TipShape;
                float _UseBladeTexture;
                float _AlphaCutoff;
                float _BlendWithBladeColor;
            CBUFFER_END

            // Global Terrain Diffuse Map (set by ModernRenderTerrainMap)
            sampler2D _TerrainDiffuse;
            float _OrthographicCamSizeTerrain;
            float3 _OrthographicCamPosTerrain;

            // Global Weather Wetness (set by GPURainVolume)
            float _GlobalWeatherWetness;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 color      : COLOR;
                float fogFactor   : TEXCOORD3;
                float cutHeight   : TEXCOORD4;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output = (Varyings)0;
                DrawVertex v = _DrawVertices[vertexID];

                output.positionWS = v.positionWS;
                output.positionCS = TransformWorldToHClip(v.positionWS);
                output.normalWS = v.normalWS;
                output.uv = v.uv;
                output.color = v.color;
                output.cutHeight = v.cutHeight;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Real-time Cutting Clip: Discard all blade geometry above cut height
                if (input.positionWS.y > input.cutHeight)
                {
                    discard;
                }

                // Rounded tip dome clipping: discards square top corners for a smooth circular dome
                if (_TipShape > 1.5 && input.uv.y > 0.75)
                {
                    float normX = (input.uv.x - 0.5) * 2.0;
                    float normY = (input.uv.y - 0.75) / 0.25;
                    if ((normX * normX + normY * normY) > 1.0)
                    {
                        discard;
                    }
                }

                float3 normalWS = normalize(input.normalWS);

                // Base blade foliage albedo from vertex color gradient
                float3 albedo = input.color.rgb * _BaseColor.rgb;

                // Blade Texture & Alpha Cutout
                #if defined(_USE_BLADE_TEXTURE)
                    half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    clip(texCol.a - _AlphaCutoff);
                    if (_BlendWithBladeColor > 0.5)
                    {
                        albedo *= texCol.rgb;
                    }
                    else
                    {
                        albedo = texCol.rgb;
                    }
                #endif

                // Distinct freshly sliced stalk top for cut grass
                if (input.cutHeight < 90000.0)
                {
                    float distToCut = input.cutHeight - input.positionWS.y;
                    if (distToCut < 0.04)
                    {
                        albedo *= lerp(0.7, 1.0, saturate(distToCut / 0.04));
                    }
                }

                // Weather Rain Wetness: Darkens grass when wet
                albedo *= lerp(1.0, 0.76, _GlobalWeatherWetness);

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);
                #else
                    Light mainLight = GetMainLight();
                #endif

                // Consistent stylized lighting across the entire blade with softened/dimmed shadow
                float rawShadow = mainLight.shadowAttenuation;
                float shadow = saturate(rawShadow * _BladeShadowStrength + (1.0 - _BladeShadowStrength));
                float3 lightDir = mainLight.direction;
                float3 lightColor = mainLight.color * (mainLight.distanceAttenuation * shadow);

                // Stylized smooth diffuse wrap
                float rawNdotL = dot(normalWS, lightDir);
                float diffuseFactor = saturate(rawNdotL * 0.5 + 0.5);
                float3 directDiffuse = diffuseFactor * lightColor;

                // Stylized Color-Preserving Ambient from Environment
                float3 shColor = SampleSH(normalWS);
                float ambientLuma = dot(shColor, float3(0.299, 0.587, 0.114));
                float3 ambient = lerp(float3(ambientLuma, ambientLuma, ambientLuma), shColor, 0.15);

                #if defined(_GROUND_BLEND_ON)
                    // Upward ground normal (0, 1, 0) for uniform, seamless ground-matching lighting across all blades
                    float3 groundNormalWS = float3(0.0, 1.0, 0.0);
                    float groundNdotL = saturate(dot(groundNormalWS, lightDir) * 0.5 + 0.5);
                    float3 groundDirectDiffuse = groundNdotL * lightColor;

                    float3 groundSH = SampleSH(groundNormalWS);
                    float groundAmbLuma = dot(groundSH, float3(0.299, 0.587, 0.114));
                    float3 groundAmbient = lerp(float3(groundAmbLuma, groundAmbLuma, groundAmbLuma), groundSH, 0.15);
                #endif

                // View direction for backlight transmission
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);

                // Subsurface Scattering / Translucency from Main Light
                float sssWrap = saturate(rawNdotL * 0.5 + 0.5);
                float backScatter = saturate(dot(-viewDir, lightDir) * 0.5 + 0.5);
                float transFactor = (sssWrap * 0.4 + pow(backScatter, 1.8) * 0.8) * _Translucency * saturate(input.uv.y * 1.3);
                float3 transmissionGlow = transFactor * lightColor * albedo * (shadow * 0.7 + 0.3) * 1.6;

                // Additional Lights (Point lights, Spot lights, Lanterns, Torches, Magic)
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.positionCS = input.positionCS;
                    inputData.normalWS = normalWS;
                    inputData.viewDirectionWS = viewDir;
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

                        float3 addLightColor = addLight.color * (addLight.distanceAttenuation * addLight.shadowAttenuation);
                        float addNdotL = saturate(dot(normalWS, addLight.direction) * 0.5 + 0.5);
                        directDiffuse += addNdotL * addLightColor;

                        float addBackScatter = saturate(dot(-viewDir, addLight.direction) * 0.5 + 0.5);
                        float addTrans = (addNdotL * 0.4 + pow(addBackScatter, 1.8) * 0.8) * _Translucency * saturate(input.uv.y * 1.3);
                        transmissionGlow += addTrans * addLightColor * albedo * 1.2;
                    }
                    #endif

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                        #ifdef _LIGHT_LAYERS
                            if (!IsMatchingLightLayer(addLight.layerMask, 1)) continue;
                        #endif

                        float3 addLightColor = addLight.color * (addLight.distanceAttenuation * addLight.shadowAttenuation);
                        float addNdotL = saturate(dot(normalWS, addLight.direction) * 0.5 + 0.5);
                        directDiffuse += addNdotL * addLightColor;

                        // Point light backlight translucency
                        float addBackScatter = saturate(dot(-viewDir, addLight.direction) * 0.5 + 0.5);
                        float addTrans = (addNdotL * 0.4 + pow(addBackScatter, 1.8) * 0.8) * _Translucency * saturate(input.uv.y * 1.3);
                        transmissionGlow += addTrans * addLightColor * albedo * 1.2;
                    LIGHT_LOOP_END
                #endif

                // Stylized Edge Highlight
                float edgeBorder = pow(saturate(abs(input.uv.x - 0.5) * 2.0), 2.0);
                float NdotV = saturate(dot(normalWS, viewDir));
                float fresnelRim = pow(1.0 - NdotV, 2.5);
                float edgeMask = max(edgeBorder, fresnelRim * 0.75) * saturate(input.uv.y);

                float3 halfVector = normalize(lightDir + viewDir);
                float NdotH = saturate(dot(normalWS, halfVector));
                float specSheen = pow(NdotH, 8.0) * 0.6 + 0.4;
                float3 directSpecular = edgeMask * specSheen * _EdgeHighlight * lightColor * (shadow * 0.7 + 0.3) * 2.0;

                // Composite stylized foliage lighting
                float3 bladeLit = albedo * (directDiffuse + ambient) + transmissionGlow + directSpecular;

                // MinionsArt Additive Ground Blending
                #if defined(_GROUND_BLEND_ON)
                    float verticalFade = saturate((input.uv.y + _GroundBlendFade) * _GroundBlendStretch);
                    float3 finalColor;
                    if (verticalFade < 0.999)
                    {
                        float2 groundUV = (input.positionWS.xz - _OrthographicCamPosTerrain.xz) / max(0.001, _OrthographicCamSizeTerrain * 2.0) + 0.5;
                        float4 terrainColor = tex2Dlod(_TerrainDiffuse, float4(groundUV, 0, 0));

                        // Ground color saturation & brightness adjustments
                        float groundLuma = dot(terrainColor.rgb, float3(0.2126, 0.7152, 0.0722));
                        float3 groundAdj = lerp(float3(groundLuma, groundLuma, groundLuma), terrainColor.rgb, _GroundBlendSaturation) * _GroundBlendBrightness;

                        // Environmental light modulation: Preserves full 1:1 ground texture brightness under either direct sunlight
                        // or normal sky ambient (keeping Brightness = 1 matching consistently with sun ON or OFF),
                        // while smoothly dropping to 0.0 only when both extinguish (zero-light night).
                        float directLuma = dot(groundDirectDiffuse, float3(0.299, 0.587, 0.114));
                        float groundAmbLumaVal = dot(groundAmbient, float3(0.299, 0.587, 0.114));
                        float groundNightFactor = saturate(directLuma * 2.0 + groundAmbLumaVal * 5.0);
                        float3 groundLit = groundAdj * groundNightFactor;

                        // Base root (verticalFade = 0) seamlessly matches the lit terrain diffuse map
                        // As blade rises (verticalFade = 1), smoothly transitions to stylized foliage lighting
                        finalColor = lerp(groundLit, bladeLit * _AmbientAdjustmentColor.rgb, verticalFade);
                    }
                    else
                    {
                        finalColor = bladeLit * _AmbientAdjustmentColor.rgb;
                    }
                #else
                    float3 finalColor = bladeLit;
                #endif

                // Wet Glossy Specular Sheen during rain
                if (_GlobalWeatherWetness > 0.001)
                {
                    float3 halfVec = normalize(lightDir + viewDir);
                    float NdotH = saturate(dot(normalWS, halfVec));
                    float wetSpecular = pow(NdotH, 28.0) * _GlobalWeatherWetness * 0.35;
                    finalColor += mainLight.color * wetSpecular;
                }

                // Apply Fog
                finalColor = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_local _ _USE_BLADE_TEXTURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct DrawVertex
            {
                float3 positionWS;
                float3 normalWS;
                float2 uv;
                float4 color;
                float cutHeight;
                float3 _pad;
            };

            StructuredBuffer<DrawVertex> _DrawVertices;
            float3 _LightDirection;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _ShadowCastDensity;
                float _TipShape;
                float _UseBladeTexture;
                float _AlphaCutoff;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float cutHeight   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output = (Varyings)0;
                DrawVertex v = _DrawVertices[vertexID];

                float3 positionWS = v.positionWS;
                float3 normalWS = normalize(v.normalWS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.positionWS = positionWS;
                output.cutHeight  = v.cutHeight;
                output.uv         = v.uv;
                return output;
            }

            static const float ditherTable[16] = {
                0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
               12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
               15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };

            half4 frag(Varyings input) : SV_Target
            {
                if (input.positionWS.y > input.cutHeight)
                {
                    discard;
                }

                // Cutout texture clipping in shadow caster
                #if defined(_USE_BLADE_TEXTURE)
                    half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                    clip(texCol.a - _AlphaCutoff);
                #endif

                // Rounded tip dome clipping in shadow caster
                if (_TipShape > 1.5 && input.uv.y > 0.75)
                {
                    float normX = (input.uv.x - 0.5) * 2.0;
                    float normY = (input.uv.y - 0.75) / 0.25;
                    if ((normX * normX + normY * normY) > 1.0)
                    {
                        discard;
                    }
                }

                // Dithered soft shadow caster:
                // Drops shadow fragments based on 4x4 Bayer matrix to produce soft, dimmed shadows through PCF
                if (_ShadowCastDensity < 0.99)
                {
                    uint2 pixelCoord = uint2(input.positionCS.xy) % 4;
                    float dither = ditherTable[pixelCoord.y * 4 + pixelCoord.x];
                    if (dither >= _ShadowCastDensity)
                    {
                        discard;
                    }
                }

                return 0;
            }
            ENDHLSL
        }
    }
}
