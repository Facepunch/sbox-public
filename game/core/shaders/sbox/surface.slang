// sbox.surface: what a material shader written as a surface sees. A surface is a [Material] struct implementing
// ISurface: one function from the inputs to the surface's properties. The engine owns everything else: vertex
// processing, the passes, lighting and render state. See docs/managed/shader-format-replacement.md.
module surface;

__exported import sbox.material;

/// What a surface returns: the properties common/material.hlsl's Material and ShaderGraph already use.
public struct Material
{
	public float3 Albedo = 1;
	public float Metalness = 0;
	public float Roughness = 1;
	public float3 Emission = 0;
	public float3 Normal = float3( 0, 0, 1 );   // world space
	public float AmbientOcclusion = 1;
	public float Opacity = 1;
	public float3 Transmission = 0;
	public float TintMask = 1;

	// A factor on the final coverage, after the alpha test's sharpened cutoff, for fading whole triangles (foliage's
	// grazing-angle fade): spent as sub-pixel coverage with multisampling, else cut at a half. Not on Opacity, which the
	// cutoff's derivatives would cancel.
	public float Coverage = 1;
}

// Set by the pass's pipeline: off in raster passes (hardware derivatives), on in a visibility-buffer resolve, which
// supplies the uv gradients
[vk::constant_id( 1 )] public const bool kSampleWithGradients = false;

/// What the engine hands Evaluate and Light. Built by the pass: from interpolants in raster passes, from the triangle and
/// barycentrics in a visibility-buffer resolve. Surfaces read textures through Sample(), never ddx/ddy, so the resolve
/// can supply gradients, and so can the light loop, where lanes diverge and hardware derivatives are undefined.
public struct SurfaceInput
{
	public float3 Position;          // world
	public float3 Normal;            // world, geometric
	public float3 TangentU;          // world
	public float3 TangentV;          // world
	public float2 Uv;
	public float4 Color;             // vertex colour
	public float4 ScreenPosition;    // SV_Position
	public float3 View;              // world, towards the camera
	public float ViewDistance;       // to the camera
	public float3 FaceNormal;        // world, the triangle's own (flat): from derivatives in raster passes, from the triangle in a resolve
	public float AlphaReference;     // the alpha test's cutoff (the material's), for surfaces that move it
	public float2 UvDdx;             // the uv's screen gradients, used when Gradients is set
	public float2 UvDdy;
	public bool Gradients;           // sample with UvDdx/UvDdy: in Light, which runs inside the light loop

	public float4 Sample( MaterialTexture2D texture, float2 uv )
	{
		if ( kSampleWithGradients || Gradients ) return texture.SampleGrad( g_MaterialFiltering, uv, UvDdx, UvDdy );
		return texture.Sample( g_MaterialFiltering, uv );
	}

	/// A tangent-space normal to world space.
	public float3 ToWorld( float3 tangentNormal )
	{
		return normalize( tangentNormal.x * TangentU + tangentNormal.y * TangentV + tangentNormal.z * Normal );
	}

	/// A normal map's texel in world space, as common/material.hlsl's Material::From reads it (DecodeNormal, then
	/// TransformNormal's y flip for the tools' tangent convention on normalized tangents).
	public float3 NormalFromMap( float3 texel )
	{
		float3 t = normalize( 2.0 * texel - 1.0 );
		t.y = -t.y;
		return normalize( t.x * normalize( TangentU ) + t.y * normalize( TangentV ) + t.z * Normal );
	}
}

/// A material shader.
public interface ISurface
{
	Material Evaluate( SurfaceInput i );
}

/// What the engine hands IVertexSurface.Displace.
public struct VertexSurfaceInput
{
	public float3 Position;          // object space
	public float3 Normal;            // object space
	public float4 Color;             // the mesh's vertex colour (COLOR0); (0, 0, 0, 1) when it has none
	public float3 ObjectOrigin;      // world
	public float3 Wind;              // world: the view's wind direction times its strength
	public float Time;
}

/// Optional: move vertices (wind, waves). Every pass that transforms vertices calls it, so depth, shadows and forward
/// agree. Returns the object-space position.
public interface IVertexSurface : ISurface
{
	float3 Displace( VertexSurfaceInput v );
}

/// What a light hands ILitSurface.Light.
public struct LightSample
{
	public float3 Direction;         // towards the light
	public float3 Color;             // the light's colour, with its modulators
	public float Attenuation;        // distance and cone falloff
	public float Visibility;         // shadow: 1 unshadowed
	public bool Sun;                 // the directional light
}

/// Optional: light a surface's own way. The engine's light loop calls Light for every light it shades the pixel with (the
/// sun, clustered lights, baked indexed lights), shadowed ones included, after its standard terms; what it returns is added
/// to the diffuse light as emission. The loop stays the engine's. Sample() in Light uses the pixel's gradients.
public interface ILitSurface : ISurface
{
	float3 Light( Material m, SurfaceInput i, LightSample light );
}
