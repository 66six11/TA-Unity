Shader "Hidden/TA/Pixel Cloud Landscape"
{
    Properties
    {
        [NoScaleOffset] _Map("Coverage",2D)="white"{}
        [NoScaleOffset] _Bubbly("Rounded lobes",3D)=""{}
        [NoScaleOffset] _Swirley("Edge erosion",3D)=""{}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "LandscapeLight.hlsl"
            TEXTURE2D_X_FLOAT(_LandscapeDepth);
            sampler2D _Map;
            sampler3D _Bubbly, _Swirley;
            float4 _Weather, _Layer, _Flow, _Clock;
            float _PixelAngle;
            float4 _LightBands[32], _ViewBands[32], _BandCounts;
            int _ThicknessPreview, _BezierSteps, _SpacingCount;
            StructuredBuffer<float2> _CloudBandSpacing;
            float3 _CloudSky, _DarkControl, _LightControl;
            static const float extinction = 0.033;

            float3 sceneSunColor()
            {
                #ifdef UNITY_COLORSPACE_GAMMA
                    return SRGBToLinear(_MainLightColor.rgb);
                #else
                    return _MainLightColor.rgb;
                #endif
            }

            float2 mapPosition(float2 xz)
            {
                return xz + _Flow.xy + _Flow.zw*(_Clock.x + _Clock.y*_Time.y);
            }
            // The broad silhouette and its projection on the ground use identical coordinates.
            float mass(float2 p)
            {
                p /= _Layer.z;
                float region = tex2Dlod(_Map,float4(p*0.000004,0,1)).r;
                float shape = tex2Dlod(_Map,float4(p*0.000024,0,1)).a;
                return saturate((region*0.38 + shape*0.62 + _Weather.x*0.86-0.79)*2.4);
            }
            float broadDensity(float body, float h)
            {
                float top = lerp(0.22+0.77*body,0.63,_Weather.y);
                return body - 0.56*smoothstep(0.36*top,top,h)
                            - 0.55*(1-smoothstep(0.02,0.22,h))
                            - 0.55*smoothstep(top,top+0.1,h);
            }
            float density(float3 pos,float h,float pixelWidth)
            {
                if(h<=0 || h>=1) return 0;
                float2 xz=mapPosition(pos.xz);
                float broad=broadDensity(mass(xz),h);
                if(broad <= 0) return 0;
                // Gradually replace unresolved erosion by its mean to preserve coverage.
                float medium=smoothstep(1.5,4.0,115/max(0.01,pixelWidth));
                float fine=smoothstep(1.5,4.0,18/max(0.01,pixelWidth));
                float3 p=float3(xz.x,pos.y,xz.y);
                float lobe=0.48, grain=0.48;
                if(medium>0) lobe=lerp(lobe,tex3Dlod(_Bubbly,float4(p*0.00095,0)).r,medium);
                if(fine>0) grain=lerp(grain,tex3Dlod(_Swirley,float4(p*0.0055,0)).r,fine);
                float edge=saturate(1-broad);
                return saturate(broad - _Weather.w*edge*(0.61*lobe*lobe+0.36*grain*grain));
            }

            float sunTransmittance(float3 p, float h, float footprint)
            {
                // Integrate to the sunward slab boundary; use the same horizon limit as the view ray.
                float dh = _MainLightPosition.y >= 0 ? 1-h : h;
                float lengthToExit = min(65000, dh*_Layer.y/max(abs(_MainLightPosition.y),1e-6));
                float tau = 0;
                [loop] for(int k=0; k<(int)_BandCounts.z; ++k)
                {
                    // Shorter segments near p resolve the local cloud even for a grazing sun.
                    float a = k/_BandCounts.z, b = (k+1)/_BandCounts.z;
                    float ds = (b*b-a*a)*lengthToExit;
                    float t = 0.5*(a*a+b*b)*lengthToExit;
                    tau += density(p+_MainLightPosition.xyz*t, h+_MainLightPosition.y*t/_Layer.y, footprint)*ds*extinction;
                }
                return exp(-tau);
            }

            float cloudBrightness(float3 p, float h, float footprint)
            {
                // A broad density gradient describes the lobe instead of lighting every noise texel.
                float radius = clamp(max(_Layer.y*0.046, footprint), 12, _Layer.y*0.16);
                const float3 k = float3(1,-1,0);
                float3 axes[4] = { k.xxx, k.xyy, k.yxy, k.yyx };
                float3 outward = 0;
                [unroll] for (int j=0; j<4; ++j)
                {
                    float3 axis = axes[j];
                    outward -= axis * density(p+radius*axis, h+radius*axis.y/_Layer.y, footprint);
                }
                // Regularization softens unstable directions in almost-uniform interiors.
                float3 n = outward * rsqrt(dot(outward,outward)+0.0025);
                float diffuse = saturate((dot(n,_MainLightPosition.xyz)+0.25)/1.25);
                float visibility = sunTransmittance(p,h,footprint);
                if (_BezierSteps >= 0)
                {
                    const float3 luma = float3(0.2126, 0.7152, 0.0722);
                    float sky = max(0, dot(_CloudSky, luma));
                    float sun = max(0, dot(sceneSunColor(), luma));
                    float skyExposure = 0.5 + 0.5 * n.y;
                    float sunExposure = diffuse * (0.28 + 0.72 * visibility);
                    return (sky * skyExposure + sun * sunExposure) / max(sky + sun, 1e-6);
                }
                float ambient = (0.19+0.10*n.y)*(1-0.30*_Weather.z);
                return saturate(ambient + diffuse*(0.28+0.54*visibility)*(1-0.42*_Weather.z));
            }

            float bandCoordinate(float brightness)
            {
                if (_SpacingCount == 0) return brightness;
                int lo = 0, hi = _SpacingCount;
                [loop] while (lo < hi)
                {
                    int mid = lo + (hi - lo) / 2;
                    if (_CloudBandSpacing[mid].y <= brightness) lo = mid + 1;
                    else hi = mid;
                }
                float2 a = float2(0, 0), b = float2(1, 1);
                if (lo > 0) a = _CloudBandSpacing[lo - 1];
                if (lo < _SpacingCount) b = _CloudBandSpacing[lo];
                return lerp(a.x, b.x, (brightness-a.y)/(b.y-a.y));
            }

            float3 bezierCloud(float brightness, float storm)
            {
                float t = saturate(brightness);
                if (_BezierSteps == 1) t = 0.5;
                if (_BezierSteps > 1) t = min(floor(bandCoordinate(t) * _BezierSteps), _BezierSteps - 1) / (_BezierSteps - 1);
                float s = 1 - t;
                float3 response = 3*s*s*t*_DarkControl + 3*s*t*t*_LightControl + t*t*t;
                float3 sky = max(0, _CloudSky) * (1 - 0.30 * storm);
                float3 sun = max(0, sceneSunColor()) * (1 - 0.42 * storm);
                return sky * (0.4 + 0.6 * response) + sun * response;
            }

            float groundShade(float3 ground)
            {
                if(_Weather.x<=0 || _Layer.w<=0 || _MainLightPosition.y<=0.015) return 0;
                float optical=0;
                [unroll] for(int k=0;k<5;k++)
                {
                    float h=(k+0.5)/5.0;
                    float t=(_Layer.x+h*_Layer.y-ground.y)/_MainLightPosition.y;
                    if(t>0) optical+=max(0,broadDensity(mass(mapPosition(ground.xz+t*_MainLightPosition.xyz.xz)),h)-0.10);
                }
                return _Layer.w*(1-exp(-1.7*optical));
            }
            half4 frag(Varyings input):SV_Target
            {
                float2 uv=input.texcoord;
                float depth=SAMPLE_TEXTURE2D_X_LOD(_LandscapeDepth,sampler_PointClamp,uv,0).r;
                #if UNITY_REVERSED_Z
                    const float nearZ=1, farZ=0;
                #else
                    const float nearZ=UNITY_NEAR_CLIP_VALUE, farZ=1;
                    depth=lerp(nearZ,1,depth);
                #endif
                float3 origin=ComputeWorldSpacePosition(uv,nearZ,UNITY_MATRIX_I_VP);
                float3 farPoint=ComputeWorldSpacePosition(uv,farZ,UNITY_MATRIX_I_VP);
                float3 ray=normalize(farPoint-origin);
                float3 world=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
                bool isGround=abs(depth-farZ)>0.000001;
                float end=isGround ? distance(world,origin) : 65000;
                float storm=_Weather.z;
                float3 haze=lerp(float3(0.77,0.85,0.89),float3(0.46,0.52,0.61),storm);
                float3 background=lerp(haze,float3(0.34,0.60,0.80)*(1-0.5*storm),saturate(ray.y));
                if(isGround)
                {
                    float3 terrain=toDisplay(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,uv,0).rgb);
                    terrain*=lerp(1,landscapeLight(0,0),groundShade(world))*lerp(1,0.73,storm);
                    background=lerp(terrain,haze,0.58*(1-exp(-end*0.000035)));
                }
                float begin=0;
                if(abs(ray.y)>0.00001)
                {
                    float a=(_Layer.x-origin.y)/ray.y;
                    float b=(_Layer.x+_Layer.y-origin.y)/ray.y;
                    begin=max(begin,min(a,b));
                    end=min(end,max(a,b));
                }
                else if(origin.y<_Layer.x || origin.y>_Layer.x+_Layer.y) end=0;
                end=min(end,65000);
                float trans=1, weightedLight=0, weightedHaze=0, viewTau=0;
                bool bands=_BandCounts.w>0.5;
                if(end>begin && _Weather.x>0)
                {
                    float stride=(end-begin)/_Clock.z;
                    [loop] for(int j=0;j<(int)_Clock.z && (bands || trans>0.015);j++)
                    {
                        float dist=begin+(j+0.5)*stride;
                        float3 p=origin+dist*ray;
                        float h=(p.y-_Layer.x)/_Layer.y;
                        float footprint=_PixelAngle*lerp(dist,1,unity_OrthoParams.w);
                        float d=density(p,h,footprint);
                        if(d<=0.001) continue;
                        float deltaTau=d*stride*extinction;
                        viewTau+=deltaTau;
                        float weight=trans*(1-exp(-deltaTau));
                        if(bands)
                        {
                            if(weight>0 && _ThicknessPreview!=2)
                                weightedLight+=cloudBrightness(p,h,footprint)*weight;
                            trans*=exp(-deltaTau);
                            continue;
                        }
                        float stepSun=_Layer.y*0.095;
                        float block=density(p+_MainLightPosition.xyz*stepSun,h+_MainLightPosition.y*0.095,footprint);
                        float lit=saturate(0.19+0.26*h+0.37*exp(-6*block)+1.95*(d-block)-0.18*storm);
                        weightedLight+=lit*weight;
                        weightedHaze+=(1-exp(-dist*0.000025))*weight;
                        trans-=weight;
                    }
                }
                float opacity=1-trans;
                if(bands)
                {
                    float brightness = saturate(weightedLight/max(opacity,1e-8));
                    if(_ThicknessPreview!=0)
                    {
                        float value=viewTau<=0 ? 0 : (_ThicknessPreview==1 ? brightness : opacity);
                        return half4(toTarget(value.xxx),1);
                    }
                    if(viewTau<=0) return half4(toTarget(background),1);
                    float3 tint=_LightBands[0].rgb;
                    float alpha=_ViewBands[0].y;
                    [loop] for(int a=1;a<(int)_BandCounts.y;++a)
                        if(viewTau>=_ViewBands[a].x) alpha=_ViewBands[a].y;
                    if (_BezierSteps >= 0)
                    {
                        float3 cloud = bezierCloud(brightness, storm);
                        float3 composite = lerp(SRGBToLinear(background), cloud, alpha);
                        #ifdef UNITY_COLORSPACE_GAMMA
                            composite = LinearToSRGB(composite);
                        #endif
                        return half4(composite, 1);
                    }
                    [loop] for(int c=1;c<(int)_BandCounts.x;++c)
                        if(brightness>=_LightBands[c].w) tint=_LightBands[c].rgb;
                    return half4(toTarget(lerp(background,tint,alpha)),1);
                }
                float3 cloud=landscapeLight(weightedLight/max(0.001,opacity),storm);
                cloud=lerp(cloud,haze,weightedHaze/max(0.001,opacity)*0.60);
                return half4(toTarget(lerp(background,cloud,round(smoothstep(0.16,0.86,opacity)*8)/8)),1);
            }
            ENDHLSL
        }
    }
}




