#ifndef SURFACE_DUST_PARTICLE_INCLUDED
#define SURFACE_DUST_PARTICLE_INCLUDED
// 48 bytes; matches SurfaceDustEmitter.ParticleSnapshot.
struct DustParticle
{
    float3 position;
    float age;
    float3 velocity;
    float lifetime;
    float size;
    uint seed;
    float travel;
    float padding;
};
#endif
