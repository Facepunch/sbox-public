#ifndef RAYTRACING_HLSL
#define RAYTRACING_HLSL

ExternalDescriptorSet RaytracingDescriptorSet Slot 0;
RaytracingAccelerationStructure _accelStruct EXTERNAL_DESC_SET(t, RaytracingDescriptorSet, 0);

class Raytracing
{
    static RaytracingAccelerationStructure GetAccelerationStructure() { return _accelStruct; }
    static float3 GetAlbedoForInstance(int instanceID) { return 1.0f; /* Placeholder white albedo */ }

    struct Result
    {
        bool Hit;
        float3 HitPosition;
        int HitInstanceID;
    };
};

#endif