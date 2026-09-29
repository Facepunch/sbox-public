using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Features;

/// <summary>
/// Lights: sorted by how much of the screen they cover and packed as native <c>GPULight</c>s.
/// </summary>
internal sealed partial class LightBinnerFeature
{
	/// <summary>
	/// 148-byte scalar-layout mirror of <c>GPULight</c>/<c>BinnedLight</c> (lightbinner_standard.h, common/lightbinner.hlsl).
	/// </summary>
	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	internal struct GpuLight
	{
		public uint Type;
		public uint Shape;
		public uint Flags;

		/// <summary>
		/// Row-major VMatrix with forward/left/up/position columns; shader indices 0 and 3 select forward and position.
		/// </summary>
		public Vector4 Row0, Row1, Row2, Row3;

		public Vector3 Color;
		public float LinearFalloff;
		public float QuadraticFalloff;
		public float FalloffBias;
		public float Radius;
		public float RadiusSquared;
		public Vector2 ShapeSize;
		public Vector4 SpotLightInnerOuterConeCosines;
		public float FogIntensity;
		public uint ShadowMapIndex;
		public uint LightCookieTextureIndex;
		public uint ShadowMaskTextureIndex;

		public const int NativeSize = 148;

		internal static void ValidateLayout()
		{
			var size = System.Runtime.CompilerServices.Unsafe.SizeOf<GpuLight>();
			if ( size != NativeSize )
				throw new InvalidOperationException( $"GpuLight is {size} bytes, native GPULight is {NativeSize}" );
		}
	}

	// LightType in lightbinner.hlsl - and native's LightType_t
	const uint LightTypePoint = 1;
	const uint LightTypeSpot = 3;

	// LightFlags in lightbinner.hlsl
	const uint DiffuseEnabled = 0x2;
	const uint SpecularEnabled = 0x4;
	const uint TransmissiveEnabled = 0x8;

	/// <summary>
	/// Screen size from world-AABB radius (<c>CFrustum::ComputeScreenSize</c>): sqrt(3) times reach for point lights.
	/// </summary>
	static float ComputeScreenSize( LightObject light, in Vector3 worldExtents, RenderView view )
	{
		var radius = worldExtents.Length;
		var distance = light.Transform.Position.Distance( view.Position );
		var tanHalfFov = MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
		return distance < radius ? 1.0f : Math.Clamp( radius / (distance * tanHalfFov), 0, 1 );
	}

	(float Size, float Distance)[] sortKeys = new (float, float)[64];

	/// <summary>
	/// Largest screen size first, then nearest; cluster overflow drops the least important lights.
	/// </summary>
	void SortByImportance( ReadOnlySpan<RenderObject> objects, ReadOnlySpan<Vector3> extents, Span<int> visible, RenderView view )
	{
		if ( visible.Length < 2 ) return;
		if ( sortKeys.Length < visible.Length ) sortKeys = new (float, float)[Math.Max( visible.Length, sortKeys.Length * 2 )];

		var camera = view.Position;

		for ( int i = 0; i < visible.Length; i++ )
		{
			var light = (LightObject)objects[visible[i]];

			// CFrustum::ComputeScreenSize - negated so an ascending sort puts the biggest first
			sortKeys[i] = (-ComputeScreenSize( light, extents[visible[i]], view ), light.Transform.Position.Distance( camera ));
		}

		sortKeys.AsSpan( 0, visible.Length ).Sort( visible );
	}

	/// <summary>
	/// The same packing as <c>CLightBinnerStandard::AddLight</c>.
	/// </summary>
	internal static GpuLight Pack( LightObject light )
	{
		var transform = light.Transform;
		var position = transform.Position;
		var radius = light.Radius;

		// The component's attenuation is scaled up to be readable, native stores it raw
		var quadratic = light.Attenuation / 10000.0f;

		var d = new GpuLight
		{
			Type = light.Kind == LightObject.LightKind.Spot ? LightTypeSpot : LightTypePoint,
			Shape = 0, // sphere
			Flags = DiffuseEnabled | SpecularEnabled | TransmissiveEnabled,
			Color = new( light.Color.r, light.Color.g, light.Color.b ),
			LinearFalloff = 0,
			QuadraticFalloff = quadratic,

			// Subtracted from the falloff so it reaches exactly zero at the radius
			FalloffBias = 1.0f / (float.Epsilon + radius * radius * quadratic),
			Radius = radius,
			RadiusSquared = radius * radius,
			ShadowMapIndex = 0xFFFFFFFF,
			LightCookieTextureIndex = 0xFFFFFFFF,
		};

		Vector3 forward = default, left = default, up = default;

		if ( light.Kind == LightObject.LightKind.Spot )
		{
			var cosInner = MathF.Cos( MathF.Min( light.ConeInner, light.ConeOuter ).DegreeToRadian() );
			var cosOuter = MathF.Cos( light.ConeOuter.DegreeToRadian() );
			d.SpotLightInnerOuterConeCosines = new( cosInner, cosOuter, 1.0f / MathF.Max( 0.01f, cosInner - cosOuter ), MathF.Tan( light.ConeOuter.DegreeToRadian() ) );

			forward = transform.Rotation.Forward;
			up = transform.Rotation.Up;
			left = Vector3.Cross( up, forward );
			left /= left.Length;
		}
		else
		{
			// No cone. A sphere point light keeps a zero rotation - the direction would vignette it
			d.SpotLightInnerOuterConeCosines = new( 0, -1, 1, 0 );
		}

		d.Row0 = new( forward.x, left.x, up.x, position.x );
		d.Row1 = new( forward.y, left.y, up.y, position.y );
		d.Row2 = new( forward.z, left.z, up.z, position.z );
		d.Row3 = new( 0, 0, 0, 1 );

		return d;
	}
}
