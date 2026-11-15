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

	// Read by an ICombinerSurface's shading only (complex.shader's terms): how much the occlusion darkens direct diffuse and
	// specular light, the alpha test's cutoff and edge strength, the scale of the uv the opacity texture is sampled at
	// (for alpha to coverage's mip compensation), and whether lights behind the surface light its Transmission
	public float AmbientOcclusionDirectDiffuse = 0;
	public float AmbientOcclusionDirectSpecular = 1;
	public float AlphaTestReference = 0.5;
	public float AntiAliasedEdgeStrength = 1;
	public float2 OpacityUvScale = 1;
	public bool BackfaceTransmission = false;

	// Factors on the diffuse and specular colours the combiner lights with (generic.shader's specular switched off, and
	// the share its self-illumination mask takes off both)
	public float DiffuseScale = 1;
	public float SpecularScale = 1;

	// Unlit, as the final combiner's S_UNLIT: the diffuse colour and emission as they are, no lights
	public bool Unlit = false;

	// Specular light at all (the combiner's S_SPECULAR): off, the lights' and environment's specular terms are dropped, as
	// a zero specular colour alone doesn't (Fresnel still reflects at grazing angles)
	public bool Specular = true;

	// Blended additively (S_ADDITIVE_BLEND): fog thins it, its alpha by the fog's transmittance, instead of colouring it
	public bool Additive = false;

	// Fogged (the combiner's g_bFogEnabled, a material value of complex and generic.shader)
	public bool Fog = true;

	// A multiplying overlay (static_overlay.shader's blend modes 3 mod2x, 5 multiply, 6 mod then add; 0 for none): fogged
	// as an overlay, then faded by its opacity towards the colour that leaves what's behind it as it is
	public int OverlayMode = 0;

	// Read by a combiner surface's shading only where its [Combiner( ... )] features are on (skin.shader's): the blurred
	// normal subsurface scattering is lit with (zero for Normal), a factor on the curvature it takes from the normal's
	// screen derivatives, and where it scatters (S_SUBSURFACE_SCATTERING); the second specular lobe's share and its roughness
	// as a factor of Roughness (S_TWO_LOBE_SPECULAR); the colour occlusion bleeds (S_AMBIENT_OCCLUSION_COLOR_BLEED); how
	// Transmission falls off (S_TRANSMISSIVE). Bent normals, where set, light the indirect terms
	public float3 SssNormal = 0;
	public float SssCurvatureScale = 1;
	public float SssMask = 1;
	public MaterialTexture2D SssFalloff;   // the lookup the scattering is lit through (skin's diffuse falloff)
	public float TwoLobeSpecularRatio = 0;
	public float SecondLobeRoughness = 1;
	public float3 AmbientOcclusionColorBleed = 1;
	public float3 TransmissiveFalloff = 0;
	public bool UseBentNormals = false;
	public float3 BentNormal = float3( 0, 0, 1 );
	public float3 BentDiffuseNormal = float3( 0, 0, 1 );

	/// Blend two surfaces' properties, as common/material.hlsl's Material::lerp does (the normal renormalized).
	public static Material Lerp( Material a, Material b, float amount )
	{
		Material o = a;
		o.Albedo = lerp( a.Albedo, b.Albedo, amount );
		o.Emission = lerp( a.Emission, b.Emission, amount );
		o.Opacity = lerp( a.Opacity, b.Opacity, amount );
		o.TintMask = lerp( a.TintMask, b.TintMask, amount );
		o.Normal = normalize( lerp( a.Normal, b.Normal, amount ) );
		o.Roughness = lerp( a.Roughness, b.Roughness, amount );
		o.Metalness = lerp( a.Metalness, b.Metalness, amount );
		o.AmbientOcclusion = lerp( a.AmbientOcclusion, b.AmbientOcclusion, amount );
		o.Transmission = lerp( a.Transmission, b.Transmission, amount );
		return o;
	}

	/// What common/material.hlsl's Material::Init( i ) starts from: the defaults, and the geometric normal.
	public static Material Init( SurfaceInput i )
	{
		Material m = {};
		m.Normal = i.Normal;
		return m;
	}

	/// What Material::From( i ) reads when Material.CommonInputs.hlsl's textures are the material's: albedo and opacity,
	/// tinted by the tint colour and the vertex colour; the normal map with its tint mask; roughness, metalness, occlusion
	/// and transmission.
	public static Material FromCommonInputs( SurfaceInput i, MaterialTexture2D color, MaterialTexture2D normal, MaterialTexture2D rma, float3 tint )
	{
		float4 c = i.Sample( color, i.Uv );
		float4 n = i.Sample( normal, i.Uv );
		float4 r = i.Sample( rma, i.Uv );

		Material m = {};
		m.Albedo = c.rgb * tint * i.Color.rgb;
		m.Opacity = c.a;
		m.Metalness = r.g;
		m.Roughness = r.r;
		m.AmbientOcclusion = r.b;
		m.Transmission = r.a;
		m.TintMask = n.a;
		m.Normal = i.NormalFromMap( n.rgb );
		return m;
	}
}

/// common/utils/normal.hlsl's DecodeNormal, for converted shaders.
public float3 DecodeNormal( float3 encoded )
{
	return normalize( 2.0 * encoded - 1.0 );
}

/// common/utils/normal.hlsl's TransformNormal (the tools' y flip, normalized tangents), for converted shaders.
public float3 TransformNormal( float3 tangentNormal, float3 normal, float3 tangentU, float3 tangentV )
{
	tangentNormal.y = -tangentNormal.y;
	return normalize( tangentNormal.x * normalize( tangentU ) + tangentNormal.y * normalize( tangentV ) + tangentNormal.z * normal );
}

/// math_general.fxc's ComputeNormalFromRGTexture: a two-channel normal map's texel as a tangent-space normal.
public float3 ComputeNormalFromRGTexture( float2 texel )
{
	float2 xy = texel * 2.0 - 1.0;
	return float3( xy, sqrt( saturate( 1.0 - dot( xy, xy ) ) ) );
}

/// math_general.fxc's sRGB conversions, for converted shaders.
public float3 SrgbGammaToLinear( float3 c )
{
	float3 linearSegment = c / 12.92;
	float3 expSegment = pow( ( c / 1.055 ) + ( 0.055 / 1.055 ), float3( 2.4, 2.4, 2.4 ) );
	return float3( c.r <= 0.04045 ? linearSegment.r : expSegment.r, c.g <= 0.04045 ? linearSegment.g : expSegment.g, c.b <= 0.04045 ? linearSegment.b : expSegment.b );
}

public float3 SrgbLinearToGamma( float3 c )
{
	float3 linearSegment = c * 12.92;
	float3 expSegment = 1.055 * pow( c, float3( 1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4 ) ) - 0.055;
	return float3( c.r <= 0.0031308 ? linearSegment.r : expSegment.r, c.g <= 0.0031308 ? linearSegment.g : expSegment.g, c.b <= 0.0031308 ? linearSegment.b : expSegment.b );
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
	public float2 Uv2;               // the second uv set, where the mesh has one
	public float4 Color;             // vertex colour
	public float3 Camera;            // the camera's world position
	public float Time;               // the view's time, in seconds
	public float4 ScreenPosition;    // SV_Position
	public float2 ScreenUv;          // ScreenPosition.xy over the viewport: 0..1 across the view, for screen-space textures
	public float3 View;             // world, towards the camera
	public float ViewDistance;       // to the main view's camera, in its shadow views too, so distance fades agree
	public float3 FaceNormal;        // world, the triangle's own (flat): from derivatives in raster passes, from the triangle in a resolve
	public float AlphaReference;     // the alpha test's cutoff (the material's), for surfaces that move it
	public float2 UvDdx;             // the uv's screen gradients, used when Gradients is set
	public float2 UvDdy;
	public bool Gradients;           // sample with UvDdx/UvDdy: in Light, which runs inside the light loop

	// The view, for surfaces that look through themselves (glass's refraction). Unread ones cost nothing: the compiler
	// drops them, and the depth read with them
	public bool FrontFace;           // the pixel is on a front face (only two-sided materials draw back faces); a combiner surface's Normal is turned to the viewer
	public float3 PositionFromOrigin;  // world, from the lighting origin (the main view's camera, in its shadow views too): ViewDistance is its length
	public float3 CameraDirection;   // world, the camera's forward
	public bool Orthographic;        // the view's projection is orthographic: view rays are parallel, along CameraDirection
	public float4x4 WorldToProjection;   // world to clip space; a direction (w 0) relative to the camera projects as a point relative to it
	public float3 ScenePosition;     // world: where the view ray through the pixel meets the opaque scene behind it (the depth buffer), as RecoverWorldPosFromProjectedDepthAndRay finds it
	public float2 FrameBufferUvScale;  // ScreenUv to the frame-buffer copy's uv (it may be larger than the view)
	public bool Skybox;              // drawn in the 3D skybox's view (today's D_SKYBOX combo), whose reprojection from the world is wrong

	public float4 Sample( MaterialTexture2D texture, float2 uv )
	{
		if ( kSampleWithGradients || Gradients ) return texture.SampleGrad( g_MaterialFiltering, uv, UvDdx, UvDdy );
		return texture.Sample( g_MaterialFiltering, uv );
	}

	/// The same with a sampler of the surface's own (a module global, its state from its annotations).
	public float4 Sample( MaterialTexture2D texture, float2 uv, SamplerState filtering )
	{
		if ( kSampleWithGradients || Gradients ) return texture.SampleGrad( filtering, uv, UvDdx, UvDdy );
		return texture.Sample( filtering, uv );
	}

	/// At a mip level (a blurred read: skin's diffuse normal).
	public float4 SampleLevel( MaterialTexture2D texture, float2 uv, float level )
	{
		return texture.SampleLevel( g_MaterialFiltering, uv, level );
	}

	/// vr_common.fxc's CalculateMipLevel: the mip level a texture is read at, at the mesh's uv (from UvDdx and UvDdy).
	public float MipLevel( MaterialTexture2D texture )
	{
		float2 size = float2( texture.Size() );
		float2 dx = UvDdx * size.x;
		float2 dy = UvDdy * size.y;
		return 0.5 * log2( max( dot( dx, dx ), dot( dy, dy ) ) );
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

	/// A hemi-octahedral normal map's rg (today's packed normal-roughness maps) in world space, as DecodeHemiOctahedronNormal
	/// then TransformNormal read it.
	public float3 NormalFromHemiOctahedron( float2 texel )
	{
		float2 d = float2( texel.x + texel.y - 1.00392163, texel.x - texel.y );
		float3 t = normalize( float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) );
		t.y = -t.y;
		return normalize( t.x * normalize( TangentU ) + t.y * normalize( TangentV ) + t.z * Normal );
	}
}

/// A material shader.
public interface ISurface
{
	Material Evaluate( SurfaceInput i );
}

/// Instead of ISurface: a surface with per-vertex values of its own. Varyings is a struct of extra vertex inputs, each
/// field with the mesh semantic it reads (`float4 Blend : TEXCOORD4;`; zeros where a mesh has none); Vertex adjusts them
/// per vertex, and Evaluate gets them interpolated.
public interface IInterpolatedSurface
{
	associatedtype Varyings;
	Varyings Vertex( Varyings v );
	Material Evaluate( SurfaceInput i, Varyings v );
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

/// What the engine hands IInterpolatedVertexSurface.Displace: the vertex after the engine has moved it to world space.
public struct WorldVertexInput
{
	public float3 Position;          // world
	public float3 Normal;            // world
	public float2 Uv;                // the mesh's first uv
	public float3 ObjectOrigin;      // world
	public float3 Camera;            // the camera's world position
	public float3 Wind;              // world: the view's wind direction times its strength
	public float Time;
	public MaterialTexture2D BlueNoise;   // the view's blue noise texture (vr_lighting.fxc's g_tBlueNoise)
}

/// Instead of IInterpolatedSurface: one that also moves its vertices, in world space after the engine's transform (fur's
/// wind, by the mesh's colour and a noise texture). Displace gets the Varyings as Vertex made them, may move v.Position
/// and returns the Varyings to interpolate, so values it works out on the way reach Evaluate. Every pass that transforms
/// vertices calls it.
public interface IInterpolatedVertexSurface : IInterpolatedSurface
{
	Varyings Displace( inout WorldVertexInput v, Varyings values );
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
/// Everything Light reads stays live across the loop and costs registers there: read textures in Evaluate, and implement
/// ISunLitSurface instead when the term only needs the sun.
public interface ILitSurface : ISurface
{
	float3 Light( Material m, SurfaceInput i, LightSample light );
}

/// Optional, instead of ILitSurface: a term of the surface's own from the sun alone (foliage's translucency), called once
/// before shading with the sun's direction, colour (with its modulators) and shadow, and added to the emission. It runs
/// outside the light loop, so it costs about what the same code in a .shader does.
public interface ISunLitSurface : ISurface
{
	float3 Light( Material m, SurfaceInput i, LightSample sun );
}

/// Optional: work on the lit colour, after shading, in forward passes (depth passes skip it): mix in a screen-space
/// texture the engine binds to a module global (water's refraction, through `Texture2D X < Attribute( "RefractionTexture" ) >`).
/// Gets the material Evaluate returned. Returns the pixel's colour.
public interface ICompositeSurface : ISurface
{
	float4 Composite( float4 shaded, Material m, SurfaceInput i );
}

/// Optional: light the surface as complex.shader's final combiner does, rather than common/pixel.hlsl's shading model:
/// emission and transmission added after the light loops, back-face transmission (Material.BackfaceTransmission), the
/// material's direct-light occlusion factors and its own alpha-test cutoff. For surfaces standing in for complex-style
/// materials, so they draw as those do.
public interface ICombinerSurface : ISurface
{
}

/// An interpolated surface (IInterpolatedSurface) lit through the final combiner, as ICombinerSurface is
/// (vertex_color.shader's mesh colour).
public interface IInterpolatedCombinerSurface : IInterpolatedSurface
{
}
