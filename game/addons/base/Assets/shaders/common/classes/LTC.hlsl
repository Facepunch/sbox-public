// [ Heitz et al. 2016, "Real-Time Polygonal-Light Shading with Linearly Transformed Cosines" ]
#ifndef LTC_HLSL
#define LTC_HLSL

#include "common/lightbinner.hlsl"

//-----------------------------------------------------------------------------

// LUTs for Linearly Transformed Cosines lighting
// LUT from https://github.com/PiMaker/ltcgi/
Texture2D LtcAmplitudeLookup < Attribute("LtcAmplitudeLookup"); >;
Texture2D LtcMatrixLookup < Attribute("LtcMatrixLookup"); >;

//-----------------------------------------------------------------------------

// Fast approximation of arctangent function
float FastArcTan(float x)
{
    float y = x * (abs(x) * ( (3.1415 * 0.5f) * abs(x) - 0.00507668) + .420691) /
              (abs(x) * (abs(x) * (.633387806077409 + abs(x)) + .671041944493641) + .21519262713177476);
    return y;
}

// Position derivative term for line integral calculation
float PositionDerivative(float distance, float length)
{
    return length / (distance * (distance * distance + length * length)) + 
           FastArcTan(length/distance)/(distance*distance);
}

// Tangent derivative term for line integral calculation
float TangentDerivative(float distance, float length)
{
    return length*length/(distance*(distance*distance + length*length));
}

//-----------------------------------------------------------------------------

// Instruction for polygon clipping operations
struct LtcClipInstruction
{
    int Type;    // 0: original vertex, 1: intersection
    int IndexA;  // First vertex index
    int IndexB;  // Second vertex index
};

// Table entry for polygon clipping configurations
struct LtcClipEntry
{
    int VertexCount;
    LtcClipInstruction Instructions[5];
};

static const LtcClipEntry table[16] = {
    {0, {{0,0,0}, {0,1,0}, {0,2,0}, {0,3,0}, {0,4,0}}},
    {3, {{0,0,0}, {1,1,0}, {1,3,0}, {0,0,0}, {0,4,0}}},
    {3, {{1,0,1}, {0,1,0}, {1,2,1}, {0,0,0}, {0,4,0}}},
    {4, {{0,0,0}, {0,1,0}, {1,2,1}, {1,3,0}, {0,0,0}}},
    {3, {{1,3,2}, {1,1,2}, {0,2,0}, {0,0,0}, {0,4,0}}},
    {0, {{0,0,0}, {0,1,0}, {0,2,0}, {0,3,0}, {0,4,0}}},
    {4, {{1,0,1}, {0,1,0}, {0,2,0}, {1,3,2}, {0,0,0}}},
    {5, {{0,0,0}, {0,1,0}, {0,2,0}, {1,3,2}, {1,3,0}}},
    {3, {{1,0,3}, {1,2,3}, {0,3,0}, {0,0,0}, {0,4,0}}},
    {4, {{0,0,0}, {1,1,0}, {1,2,3}, {0,3,0}, {0,0,0}}},
    {0, {{0,0,0}, {0,1,0}, {0,2,0}, {0,3,0}, {0,4,0}}},
    {5, {{0,0,0}, {0,1,0}, {1,2,1}, {1,2,3}, {0,3,0}}},
    {4, {{1,0,3}, {1,1,2}, {0,2,0}, {0,3,0}, {0,0,0}}},
    {5, {{0,0,0}, {1,1,0}, {1,1,2}, {0,2,0}, {0,3,0}}},
    {5, {{1,0,1}, {0,1,0}, {0,2,0}, {0,3,0}, {1,0,3}}},
    {4, {{0,0,0}, {0,1,0}, {0,2,0}, {0,3,0}, {0,0,0}}}
};

//-----------------------------------------------------------------------------

//
// Linearly Transformed Cosines for realistic area light calculations
//
class LTC
{
// public
    // Calculates area light contribution at a surface point
    static float3 Contribution(
        in BinnedLight light,
        in float3 position,
        in float3 normal,
        in float3 view,
        in float roughness,
        bool specular
    );

// private
    // Calculates the contribution of an edge to the integral
    static float IntegrateEdge(float3 v1, float3 v2);

    // Clips polygon against horizon plane
    static void ClipQuadToHorizon(inout float3 vertices[5], out int vertexCount);

    // Core LTC evaluation for area lights
    static float3 EvaluateLtc(
        float3 normal,
        float3 view,
        float3 position,
        float3x3 transform,
        float3 points[4],
        BinnedLight light
    );

    // Samples the light cookie texture on a plane
    static float3 SampleCookie( in BinnedLight light, float3 vertices[5]);

    // Extracts points from a light definition based on its shape
    static void GetRectPoints(in BinnedLight light, in float3 position, in float3 normal, out float3 points[4]);

    // Helper function to create an orthonormal basis from a normal vector
    static void CreateOrthoBasis(float3 normal, out float3 basis1, out float3 basis2);

    // Capsule light analytical integration
    static float Line(float3 p1, float3 p2);
};

// Calculates the contribution of an edge to the integral
float LTC::IntegrateEdge(float3 v1, float3 v2)
{
    float cosTheta = dot(v1, v2);
    cosTheta = clamp(cosTheta, -0.9999, 0.9999);

    float theta = acos(cosTheta);
    return cross(v1, v2).z * theta / sin(theta);
}

// Clips polygon against horizon to handle backfacing cases
void LTC::ClipQuadToHorizon(inout float3 vertices[5], out int vertexCount)
{
    // Detect clipping config
    int config = 0;
    [unroll]
    for (int i = 0; i < 4; i++) 
    {
        config |= (vertices[i].z > 0.0) << i;
    }

    // Copy original points since vertices is modified in place
    float3 originalVertices[5] = { vertices[0], vertices[1], vertices[2], vertices[3], vertices[4] };

    // Look up the clipping entry
    const LtcClipEntry entry = table[config];
    vertexCount = entry.VertexCount;
    
    // Apply instructions to set vertices[0] to vertices[4]
    for (i = 0; i < 5; i++) {
        const LtcClipInstruction instr = entry.Instructions[i];
        if (instr.Type == 0) {
            vertices[i] = originalVertices[instr.IndexA];
        } else {
            vertices[i] = -originalVertices[instr.IndexA].z * originalVertices[instr.IndexB] + originalVertices[instr.IndexB].z * originalVertices[instr.IndexA];
        }
    }

    // Special cases
    if (vertexCount == 3 || vertexCount == 4)
        vertices[vertexCount] = vertices[0];
}

// Samples the light cookie texture
float3 LTC::SampleCookie( in BinnedLight light, float3 vertices[5])
{
    // Calculate area light plane basis
    float3 V1 = vertices[1] - vertices[0];
    float3 V2 = vertices[3] - vertices[0];
    float3 planeOrtho = cross(V1, V2);
    float planeAreaSquared = dot(planeOrtho, planeOrtho);
    float planeDistxPlaneArea = dot(planeOrtho, vertices[0]);
    
    // Project origin onto light plane
    float3 P = planeDistxPlaneArea * planeOrtho / planeAreaSquared - vertices[0];

    // Calculate texture coordinates
    float dot_V1_V2 = dot(V1, V2);
    float inv_dot_V1_V1 = 1.0 / dot(V1, V1);
    float3 V2_ = V2 - V1 * dot_V1_V2 * inv_dot_V1_V1;
    float2 Puv;
    Puv.y = dot(V2_, P) / dot(V2_, V2_);
    Puv.x = dot(V1, P) * inv_dot_V1_V1 - dot_V1_V2 * inv_dot_V1_V1 * Puv.y;

    // Calculate LOD and sample texture
    float d = abs(planeDistxPlaneArea) / pow(planeAreaSquared, 0.75);
    float2 texCoord = float2(0.125, 0.125) + 0.75 * Puv;
    texCoord.y = 1.0 - texCoord.y; // flip y
    float lod = log(2048.0 * d) / log(3.0);
    float4 sample = pow( light.SampleLightCookie( saturate( texCoord ), lod ), 2.2);
    
    return sample.rgb * sample.a;
}

// Core LTC evaluation function
float3 LTC::EvaluateLtc(float3 normal, float3 view, float3 position, float3x3 transform, float3 points[4], BinnedLight light)
{
    // Construct orthonormal basis around surface normal
    float3 T1 = normalize(view - normal * dot(view, normal));
    float3 T2 = cross(normal, T1);

    // Transform area light into tangent space
    transform = mul(transpose(transform), float3x3(T1, T2, normal));

    float3 color = light.Color;

    // Handle capsule lights with analytical integration
    if (light.Shape == LightShape::LightShapeCapsule)
    {
        float3 lightPosition = light.GetPosition();
        float3 forward = normalize(light.GetDirection());
        float halfLength = light.ShapeSize.y * 0.5;

        // Transform capsule endpoints to tangent space
        float3 p1 = mul(transform, ( lightPosition - forward * halfLength ) - position);
        float3 p2 = mul(transform, ( lightPosition + forward * halfLength ) - position);
        float radius = light.ShapeSize.x;

        return radius * Line(p1, p2) * color;
    }
    
    // For other light types, use the standard polygon integration
    // Transform light vertices
    float3 vertices[5];
    for (int i = 0; i < 4; i++)
        vertices[i] = mul(transform, points[i] - position);

    // Get light color and apply cookie if available
    if (light.HasLightCookie())
        color *= SampleCookie( light, vertices );

    // Clip light polygon against horizon
    int vertexCount;
    ClipQuadToHorizon(vertices, vertexCount);
    
    if (vertexCount == 0)
        return float3(0, 0, 0);

    // Normalize vertices for integration
    for ( i = 0; i < vertexCount; i++)
        vertices[i] = normalize(vertices[i]);

    // Integrate edges using a loop
    float sum = 0;
    for ( i = 0; i < vertexCount; i++)
    {
        sum += IntegrateEdge(vertices[i], vertices[(i + 1) % vertexCount]);
    }

    sum = max(-sum, 0.0);
    return sum * color;
}

float LTC::Line(float3 p1, float3 p2)
{    
    // Calculate tangent vector along the line
    float3 wt = normalize(p2 - p1);

    // Handle backfacing parts by clipping against horizon
    if (p1.z <= 0.0 && p2.z <= 0.0) return 0.0;
    if (p1.z < 0.0) p1 = (+p1*p2.z - p2*p1.z) / (+p2.z - p1.z);
    if (p2.z < 0.0) p2 = (-p1*p2.z + p2*p1.z) / (-p2.z + p1.z);

    // Parametrize line segment
    float l1 = dot(p1, wt);
    float l2 = dot(p2, wt);

    // Calculate orthogonal projection to the line
    float3 po = p1 - l1*wt;

    // Distance to line
    float d = length(po);

    // Calculate final integral using derivative terms
    float I = (PositionDerivative(d, l2) - PositionDerivative(d, l1)) * po.z +
              (TangentDerivative(d, l2) - TangentDerivative(d, l1)) * wt.z;
    
    return I;
}

// Extracts points from a light definition based on its shape
void LTC::GetRectPoints(in BinnedLight light, in float3 position, in float3 normal, out float3 points[4])
{
    switch (light.Shape)
    {
        case LightShape::LightShapeSphere:
        {
            float3 lightPosition = light.GetPosition();
            float radius = light.ShapeSize.x;
            float3 toLight = lightPosition - position;

            // Create orthogonal basis for the silhouette plane
            float3 dir = normalize(toLight);
            float3 up, right;
            CreateOrthoBasis(dir, up, right);

            // Calculate points on the silhouette circle
            points[0] = lightPosition + (-right - up) * radius;
            points[1] = lightPosition + (right - up) * radius;
            points[2] = lightPosition + (right + up) * radius;
            points[3] = lightPosition + (-right + up) * radius;
            break;
        }
        case LightShape::LightShapeCapsule:
        {
            break;
        }
        case LightShape::LightShapeRectangle:
        {
            float3 lightPosition = light.GetPosition();
            float3 forward = normalize(light.GetDirection());
            float3 up = normalize(light.GetDirectionUp());
            float3 right = cross(up, forward);
            float2 size = light.ShapeSize;

            float3 ex = size.y * right;
            float3 ey = size.x * up;

            points[0] = lightPosition - ex - ey;
            points[1] = lightPosition + ex - ey;
            points[2] = lightPosition + ex + ey;
            points[3] = lightPosition - ex + ey;
            break;
        }
    }   
}

// Helper function to create an orthonormal basis from a normal vector
void LTC::CreateOrthoBasis(float3 normal, out float3 basis1, out float3 basis2)
{
    float sign = normal.z >= 0 ? 1 : -1;
    float a = -1.0f / (sign + normal.z);
    float b = normal.x * normal.y * a;
    basis1 = float3(1.0f + sign * normal.x * normal.x * a, sign * b, -sign * normal.x);
    basis2 = float3(b, sign + normal.y * normal.y * a, -normal.y);
}

// Main entry point for LTC lighting calculation
float3 LTC::Contribution(
    in BinnedLight light,
    in float3 position,
    in float3 normal,
    in float3 view,
    in float roughness,
    bool specular)
{
    float3 points[4];
    GetRectPoints(light, position, normal, points);

    // Prepare roughness and calculate view angle
    roughness = sqrt(roughness);
    float theta = acos(dot(normal, view));
    float2 uv = float2(roughness, 1.0 - (theta / (0.5 * 3.14159265359)));
    
    // Sample LTC textures
    float4 t1 = LtcMatrixLookup.SampleLevel(g_sTrilinearBorder, uv, 0);
    float4 t2 = LtcAmplitudeLookup.SampleLevel(g_sTrilinearBorder, uv, 0);

    // Choose appropriate transformation matrix
    float3x3 transform = float3x3(
        float3(1, 0, 0),
        float3(0, 1, 0),
        float3(0, 0, 1)
    );

    float flBRDF = 1.0f;;
    if(specular)
    {
        transform = float3x3(
            float3(1,     0, t1.y),
            float3(0,  t1.z,    0),
            float3(t1.w,   0, t1.x)
        );

        flBRDF = t2.x * t2.y;
    }

    // Calculate light contribution
    float3 result = EvaluateLtc(normal, view, position, transform, points, light);
    return result * flBRDF;
}

#endif // LTC_HLSL
