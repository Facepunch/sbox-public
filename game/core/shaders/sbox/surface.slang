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
	public float ToneMapScale;       // the view's tone map scalar (g_flToneMapScalarLinear): what the frame's exposure multiplies by
	public float2 ViewportSize;      // the view's size in pixels

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

	// The tangent frame: in world space after the engine's transform (and skinning), and the mesh's own in object space.
	// Together they carry an object-space direction through skinning (citizen_eye's gaze axes)
	public float3 TangentU;          // world
	public float3 TangentV;          // world
	public float3 ObjectNormal;      // object space, the mesh's
	public float4 ObjectTangent;     // object space, the mesh's: tangent U, and V's sign in w
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

/// One layer of a layered surface (ILayeredSurface), as it asks the engine to light it.
public struct LightLayer
{
	public float3 Normal;               // world: specular light and the lights' N.L
	public float3 DiffuseNormal;        // world: diffuse light's, with a [Combiner] subsurface model (the combiner's subsurface normal)
	public float2 Roughness;            // the engine raises it by the geometric normal's curvature
	public float3 SpecularColor;
	public float DiffuseWrap = 0;       // SUBSURFACE_SCATTERING_WRAP: how far diffuse light wraps past the terminator,
	public float DiffuseWrapExponent = 1;   // and its falloff's exponent (0 and 1: the combiner's default)
	public float3 Offset = 0;           // world: lit from this far off the pixel (lifting lighting off a coarse shadow mesh)
	public bool GeometricRoughness = true;   // raise Roughness by the geometric normal's curvature; off for a surface that filters its own
	public bool Direct = true;          // the lights
	public bool Indirect = true;        // lightmaps, probes, the environment and screen-space reflections
}

/// The light a layer receives: diffuse before the surface's diffuse colour, specular with its specular colour.
public struct LayerLight
{
	public float3 Diffuse;
	public float3 IndirectDiffuse;
	public float3 Specular;
	public float3 IndirectSpecular;
	public float DiffuseOcclusion;      // the scene's occlusion of indirect diffuse light (screen-space and baked), not applied
	public float SpecularOcclusion;     // and of indirect specular light along the layer's normal, not applied
}

/// What the engine hands ILayeredSurface.Shade: its lighting at the pixel.
public interface ILayerLighting
{
	/// Every light, the environment and the probes on one layer: the engine's light loops through the final combiner's
	/// lighting with the surface's [Combiner] features. Callable inside branches: its derivatives were taken outside.
	LayerLight Light( LightLayer layer );

	/// Where the opaque scene is at a pixel of the view (from the depth prepass), in world space: for a surface's own
	/// screen-space occlusion.
	float3 ScenePosition( float2 pixel );
}

/// Optional, for a combiner surface that combines its own light (eyeball's cornea over its iris): forward passes call
/// Shade instead of Evaluate and the final combiner. Shade lights as many layers as it needs through the lighting it's
/// handed, mixes them and returns the lit colour before fog, with the Material the engine still reads (opacity, alpha
/// test, the tools' views). The engine fogs it. Depth passes call Evaluate, as for any combiner surface.
public interface ILayeredSurface : ICombinerSurface
{
	float3 Shade<L : ILayerLighting>( SurfaceInput i, L lighting, out Material m );
}

/// A layered surface with per-vertex values of its own (as IInterpolatedSurface's Varyings, read and adjusted the same
/// way): Shade gets them interpolated. Lit through the final combiner as an ICombinerSurface is. Also an
/// IInterpolatedVertexSurface to work them out from the vertex in world space (citizen_eye's gaze). Not derived from
/// IInterpolatedSurface: Slang doesn't match a requirement naming an associated type inherited from a base interface.
public interface IInterpolatedLayeredSurface
{
	associatedtype Varyings;
	Varyings Vertex( Varyings v );
	Material Evaluate( SurfaceInput i, Varyings v );
	float3 Shade<L : ILayerLighting>( SurfaceInput i, Varyings v, L lighting, out Material m );
}

/// What the engine hands an author's own vertex stage (IVertexStageSurface.Vertex).
public struct VertexStageInput
{
	public uint VertexId;            // SV_VertexID
	public uint InstanceId;          // the instance within the draw (SV_InstanceID): 0 for the first, also in instanced draws
	public float3x4 ObjectToWorld;   // the draw's object transform (its instance's)
	public float3 Camera;            // the camera's world position
	public float3 CameraDirection;   // world, the camera's forward
	public float3 Wind;              // world: the view's wind direction times its strength
	public float Time;               // the view's time, in seconds
}

/// What an author's vertex stage returns: where the vertex is, and the engine's interpolants the pixel stage builds
/// SurfaceInput from. The engine projects Position itself, so every pass places the vertex the same way.
public struct SurfaceVertex
{
	public float3 Position;                       // world
	public float3 Normal = float3( 0, 0, 1 );     // world: SurfaceInput.Normal, the geometric normal
	public float3 TangentU = float3( 1, 0, 0 );   // world
	public float3 TangentV = float3( 0, 1, 0 );   // world
	public float2 Uv = 0;
	public float2 Uv2 = 0;
	public float4 Color = 1;                      // SurfaceInput.Color

	// With [ClipDistances( n )] on the [Material] struct, the first n are clip distances: the rasterizer cuts the triangle
	// where one is negative, in every pass (holes, the edge of a heightmap), so no pixel shader discards
	public float4 ClipDistance = 1;
}

/// A surface's Varyings' screen-space derivatives at the pixel, taken by the engine in uniform control flow, so they hold
/// inside branches. Raster passes take them in hardware; integer fields read zero.
public struct Derivatives<V>
{
	public V Ddx;
	public V Ddy;
}

/// Instead of ISurface: a surface that owns its vertex stage (terrain's heightmap clipmap, water's waves). VertexInput is a
/// struct of the mesh's streams it reads, each field with its semantic (`float3 Position : POSITION;`, zeros where a mesh
/// has none). Vertex places the vertex in world space and returns the engine's interpolants; what else the pixel stage
/// needs goes in Varyings, a struct of the surface's own interpolants (`nointerpolation` allowed), which Evaluate gets
/// interpolated with their derivatives. Every pass runs Vertex, depth and shadows too, so they agree; a surface reads
/// render attributes there as module globals (`float3 Origin < Attribute( "Origin" ); >;`). The engine lights the
/// Material Evaluate returns, as for any surface. Drawn the classic way, never in GPU-driven buckets.
public interface IVertexStageSurface
{
	associatedtype VertexInput;
	associatedtype Varyings;
	SurfaceVertex Vertex( VertexInput v, VertexStageInput s, out Varyings varyings );
	Material Evaluate( SurfaceInput i, Varyings v, Derivatives<Varyings> d );
}

/// The engine's lighting at the pixel for a material, as the standard shading model (common/shadingmodel.hlsl) computes it.
public struct SurfaceLight
{
	public float3 Diffuse;              // the lights' diffuse light, before the diffuse colour
	public float3 IndirectDiffuse;      // lightmaps, probes, the ambient
	public float3 Specular;             // the lights' specular light, with the specular colour
	public float3 IndirectSpecular;     // the environment's and screen-space reflections, with the specular colour
	public float3 DiffuseOcclusion;     // of indirect diffuse light: the scene's and the material's, not applied
	public float3 SpecularOcclusion;    // of indirect specular light, not applied
	public float DirectDiffuseOcclusion;    // how far DiffuseOcclusion darkens direct diffuse light (0: not at all)
	public float DirectSpecularOcclusion;   // how far SpecularOcclusion darkens direct specular light
	public float3 DiffuseColor;         // what the material was lit with: from its albedo and metalness
	public float3 SpecularColor;
	public float2 Roughness;            // the material's, raised by the shading normal's curvature

	/// The standard shading model's sum of these with the material's emission, before fog: what an ISurface draws.
	public float3 Color( Material m )
	{
		float3 diffuse = ( Diffuse * lerp( 1, DiffuseOcclusion, DirectDiffuseOcclusion ) + IndirectDiffuse * DiffuseOcclusion ) * DiffuseColor;
		float3 specular = Specular * lerp( 1, SpecularOcclusion, DirectSpecularOcclusion ) + IndirectSpecular * SpecularOcclusion;
		return diffuse + m.Emission + specular;
	}
}

/// What the engine hands IShadedSurface.Shade: its lighting, reflections, fog, decals and view at the pixel. Everything is
/// the engine's, as today's shaders call it; a surface combines the parts itself. ILayerLighting's layers are the final
/// combiner's lighting for one normal and roughness.
public interface ISurfaceLighting : ILayerLighting
{
	/// Every light, the environment and the probes on a material, as the standard shading model lights it.
	SurfaceLight LightMaterial( Material m );

	/// The same with a specular colour of the surface's own instead of the one from albedo and metalness (water's 2%).
	SurfaceLight LightMaterialWithSpecular( Material m, float3 specularColor );

	/// The decals over the pixel applied to the material (colour, normal, roughness, emission), as they apply before
	/// today's shading.
	Material ApplyDecals( Material m );

	/// Screen-space reflections at the pixel for a material roughness: colour, and how much of the reflection they found.
	float4 ScreenReflection( float roughness );

	/// The environment (probes, sky) reflected about a normal at a roughness, before any specular colour.
	float3 Environment( float3 normal, float roughness );

	/// Whether the view fogs (g_bFogEnabled), and its distance and volumetric fog over a colour at the pixel.
	bool FogEnabled();
	float3 Fog( float3 color );
	float3 VolumetricFog( float3 color );

	/// The view's transforms: a world position to clip space, a world direction to clip space (relative to the camera),
	/// and directions between world and view space.
	float4 ProjectPosition( float3 position );
	float4 ProjectDirection( float3 direction );
	float3 WorldToView( float3 direction );
	float3 ViewToWorld( float3 direction );
}

/// Instead of ISurface: a surface that returns its own lit colour. Forward passes call Shade, which lights itself through
/// the engine's lighting (the material's, its layers, reflections, the environment) and returns the pixel's final colour,
/// fogged as it chooses (lighting.Fog); the engine adds only the tools' views and wireframe. m is the Material the engine
/// still reads: opacity for the alpha test, normal and roughness for screen-space reflections. Depth passes call Evaluate.
public interface IShadedSurface
{
	Material Evaluate( SurfaceInput i );
	float4 Shade<L : ISurfaceLighting>( SurfaceInput i, L lighting, out Material m );
}

/// An IShadedSurface for a surface that owns its vertex stage: Shade gets its Varyings and their derivatives. A surface
/// implements both this and IVertexStageSurface (Slang doesn't match a requirement naming an associated type inherited
/// from a base interface).
public interface IShadedVertexStageSurface
{
	associatedtype Varyings;
	float4 Shade<L : ISurfaceLighting>( SurfaceInput i, Varyings v, Derivatives<Varyings> d, L lighting, out Material m );
}
