#ifndef RAYTRACING_INTERSECT_H
#define RAYTRACING_INTERSECT_H
//
// Shared functions for ray-surface intersections
//

// axis aligned box centered at the origin, with size boxSize
float2 IntersectBox(float3 vPosStartWS, float3 invDir, unsigned int nodeNum, float3 vMaxs, float3 vMins )
{
    float2 T;
    float3 DiffMax = vMaxs - vPosStartWS;
    float3 DiffMin = vMins - vPosStartWS;
    
    DiffMax *= invDir;
    DiffMin *= invDir;


    T[0] = min(DiffMin.x,DiffMax.x);
    T[1] = max(DiffMin.x,DiffMax.x);

    T[0] = max(T[0],min(DiffMin.y,DiffMax.y));
    T[1] = min(T[1],max(DiffMin.y,DiffMax.y));

    T[0] = max(T[0],min(DiffMin.z,DiffMax.z));
    T[1] = min(T[1],max(DiffMin.z,DiffMax.z));

    //empty interval
    if (T[0] > T[1])
    {
        T[0] = T[1] = -1.0f;
    }

    return T;
}

float2 IntersectSphere( in float3 vOrigin, in float3 vDir, in float3 vCenter, float fRadius )
{
    float3 oc = vOrigin - vCenter;
    float b = dot( oc, vDir );
    float c = dot( oc, oc ) - fRadius*fRadius;
    float h = b*b - c;
    if( h<0.0 ) 
        return -1.0; // no intersection
    h = sqrt( h );
    return float2( -b-h, -b+h );
}
#endif //RAYTRACING_INTERSECT_H