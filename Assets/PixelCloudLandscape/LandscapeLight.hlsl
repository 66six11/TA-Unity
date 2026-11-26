#ifndef PIXEL_LANDSCAPE_LIGHT_INCLUDED
#define PIXEL_LANDSCAPE_LIGHT_INCLUDED
// Four shared steps from cool fill to warm sunlight, evaluated in display space.
float3 landscapeLight(float exposure, float storm)
{
    float band = floor(saturate(exposure) * 3.99) / 3;
    return lerp(float3(0.50,0.59,0.72),float3(1.0,0.965,0.86),band)
         * lerp(1,0.73,storm);
}
float3 toDisplay(float3 c)
{
#ifndef UNITY_COLORSPACE_GAMMA
    return LinearToSRGB(max(0,c));
#else
    return c;
#endif
}
float3 toTarget(float3 c)
{
#ifndef UNITY_COLORSPACE_GAMMA
    return SRGBToLinear(saturate(c));
#else
    return c;
#endif
}
#endif
