#ifndef TA_UNITY_ARTISTIC_LIGHTING_INCLUDED
#define TA_UNITY_ARTISTIC_LIGHTING_INCLUDED

// RGB transmission curve inside the direct-lighting path. Endpoint values stay
// 0 and 1; ordered Bezier controls keep each channel monotonic. Neutral controls
// 1/3 and 2/3 reproduce the original scalar response exactly.
float3 TA_TransitionResponse(float coverage)
{
    float t = saturate(coverage);
    float3 darkControl = lerp(float3(1.0/3.0,1.0/3.0,1.0/3.0),
        saturate(_TAArtShadowColor.rgb)*0.5,saturate(_TAArtStrength));
    float3 lightControl = lerp(float3(2.0/3.0,2.0/3.0,2.0/3.0),
        0.5+saturate(_TAArtLightColor.rgb)*0.5,saturate(_TAArtStrength));
    return 3*(1-t)*(1-t)*t*darkControl + 3*(1-t)*t*t*lightControl + t*t*t;
}

float3 TA_ShadowTransmission(InputData inputData)
{
    half4 parameters = GetMainLightShadowParams();
    float strength = saturate(parameters.x);
    float realtime = MainLightRealtimeShadow(inputData.shadowCoord);
    float rawRealtime = saturate((realtime-(1-strength))/max(strength,0.0001));
    float rawBaked = 1;
    #ifdef CALCULATE_BAKED_SHADOWS
        rawBaked = saturate(1+dot(inputData.shadowMask-1,_MainLightOcclusionProbes));
    #endif
    float fade = 1;
    #ifdef MAIN_LIGHT_CALCULATE_SHADOWS
        fade = GetMainLightShadowFade(inputData.positionWS);
    #endif
    // Resolve color from geometric coverage BEFORE strength and distance mixing.
    float3 realtimeRGB = lerp(float3(1,1,1),TA_TransitionResponse(rawRealtime),strength);
    float3 bakedRGB = lerp(float3(1,1,1),TA_TransitionResponse(rawBaked),strength);
    #if defined(_RECEIVE_SHADOWS_OFF)
        return float3(1,1,1);
    #elif defined(LIGHTMAP_SHADOW_MIXING)
        return min(lerp(realtimeRGB,float3(1,1,1),fade),bakedRGB);
    #else
        return lerp(realtimeRGB,bakedRGB,fade);
    #endif
}

float3 TA_SurfaceResponse(float signedNdotL)
{
    float ndotl = saturate(signedNdotL);
    float width = clamp(_TAArtTerminatorWidth,0.01,1);
    // Ease out of the unlit region with zero slope, then join the native cosine
    // with matching value AND slope. The old width*F(t) only matched values;
    // its endpoint derivatives made the dark/color and color/lit joins visible.
    if (ndotl >= width) return ndotl.xxx;
    float t = ndotl/width;
    float shadowBlend = lerp(1.0,smoothstep(0.0,0.35,t),saturate(_TAArtStrength));
    float3 response = TA_TransitionResponse(t)*shadowBlend;
    float lightBlend = smoothstep(0.65,1.0,t);
    return width*lerp(response,t.xxx,lightBlend);
}

float3 TA_DirectTransmission(InputData inputData,Light mainLight)
{
    return TA_ShadowTransmission(inputData)
         * TA_SurfaceResponse(dot(inputData.normalWS,mainLight.direction));
}

half3 TA_ArtisticMainLight(BRDFData brdfData,BRDFData coat,InputData inputData,
    Light mainLight,half3 normalWS,half3 viewDirectionWS,half coatMask,bool specularOff)
{
    float nativeTransmission = saturate(dot(normalWS,mainLight.direction))*mainLight.shadowAttenuation;
    float3 coloredTransmission = TA_DirectTransmission(inputData,mainLight);
    // Replace NdotL * scalarShadow by the RGB transmission at the light source
    // contribution. Reusing the native BRDF preserves its specular/coat behavior.
    if (nativeTransmission > 0)
        mainLight.color *= coloredTransmission/max(nativeTransmission,0.000001);
    return LightingPhysicallyBased(brdfData,coat,mainLight,normalWS,viewDirectionWS,coatMask,specularOff);
}

// Adapted from the installed URP 17.5 UniversalFragmentPBR. Only the main-light
// evaluation is redirected above; GI, other lights, emission and AO stay native.
half4 TA_UniversalFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
    #endif

    // Clear-coat calculation...
    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();

#if (UNITY_PLATFORM_META_QUEST)
    if (dot(GetMainLight().direction, inputData.normalWS) <= 0.0)
    {
        inputData.shadowCoord.z = -1; // Force outside of shadowmap
    }
#endif
    
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);

    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = TA_ArtisticMainLight(brdfData, brdfDataClearCoat, inputData,
                                                              mainLight,
                                                              inputData.normalWS, inputData.viewDirectionWS,
                                                              surfaceData.clearCoatMask, specularHighlightsOff);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
#if defined(UNITY_PLATFORM_META_QUEST)
            if(light.distanceAttenuation > 0.0)
#endif
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}
#endif
