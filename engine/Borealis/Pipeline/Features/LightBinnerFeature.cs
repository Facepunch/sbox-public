using System.Numerics;
using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Packs lights, probes and decals into a 24x16x48 cluster grid (<c>CLightBinnerStandard</c>, <c>ClusteredCullingLayer</c>).
/// Shadow indices are assigned before upload. Type-specific packing lives in the partial files; cookies are unsupported.
/// </summary>
internal sealed partial class LightBinnerFeature : RenderFeature
{
	/// <summary>
	/// Native's <c>kMaxLights</c>.
	/// </summary>
	public const int MaxLights = 2048;

	const int ClusterCountX = 24, ClusterCountY = 16, ClusterCountZ = 48;
	const int ClusterCount = ClusterCountX * ClusterCountY * ClusterCountZ;
	const int MaxLightsPerCluster = 64;

	/// <summary>
	/// Native's <c>kMaxEnvMaps</c>, and the envmap slots per cluster the engine's <c>ClusteredCullingLayer</c> gives.
	/// </summary>
	public const int MaxEnvMaps = 256;
	const int MaxEnvMapsPerCluster = 32;

	static readonly StringToken LightBufferName = new( "BinnedLightBufferV2" );
	static readonly StringToken NoBakedLightShadowsName = new( "NoBakedLightShadows" );
	static readonly StringToken NoClusteredLightShadowsName = new( "NoClusteredLightShadows" );
	static readonly StringToken EnvMapBufferName = new( "BinnedEnvMapBuffer" );
	static readonly StringToken ClusterConstantsName = new( "ClusteredLightingConstants" );

	/// <summary>
	/// Shared culling and shading constants (<c>ClusteredLightingConstants</c>).
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	struct ClusterConstants
	{
		public Vector4 Counts;         // xyz = counts, w = total
		public Vector4 InvCounts;      // xyz = 1 / counts
		public Vector4 ZParams;        // x = log scale, y = log bias, z = near, w = far
		public Vector4 ScreenParams;   // xy = size, zw = 1 / size
		public Vector4 Capacities;     // light, envmap, decal slots per cluster; w = 1 to cull envmaps by box (not captures)
	}

	readonly UploadRing<GpuLight> lightRing = new( "Borealis lights" );
	readonly UploadRing<GpuEnvMap> envMapRing = new( "Borealis envmaps" );
	GpuLight[] lights = new GpuLight[64];
	LightObject[] packedObjects = new LightObject[64];
	float[] packedScreenSize = new float[64];
	GpuMaterial cullingShader;

	// The cluster lists: a section per item type, lights, envmaps then decals (ClusterCulling.hlsl)
	GpuBuffer<uint> clusterCounts, clusterIndices;

	// Each type's slots per cluster this frame: what the view's items need, rounded up so the index list rarely resizes
	int lightCapacity, envMapCapacity, decalCapacity;

	/// <summary>
	/// Lights - and <see cref="EnvMapObject"/>s too, through <see cref="Accepts"/>.
	/// </summary>
	public override Type ObjectType => typeof( LightObject );

	internal override bool Accepts( Type type ) => typeof( LightObject ).IsAssignableFrom( type ) || typeof( EnvMapObject ).IsAssignableFrom( type ) || typeof( DecalObject ).IsAssignableFrom( type );

	/// <summary>
	/// Environment maps binned for the current view.
	/// </summary>
	public int EnvMapCount { get; private set; }

	/// <summary>
	/// Lights binned for the current view.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// The binned lights, most important first - <see cref="Count"/> of them, in their GPU order.
	/// </summary>
	internal ReadOnlySpan<LightObject> PackedLights => packedObjects.AsSpan( 0, Count );

	/// <summary>
	/// The binned probes, in the order they blend.
	/// </summary>
	internal ReadOnlySpan<EnvMapObject> PackedEnvMaps => envMapObjects.AsSpan( 0, EnvMapCount );

	/// <summary>
	/// The binned lights as the GPU gets them.
	/// </summary>
	internal ReadOnlySpan<GpuLight> PackedGpuLights => lights.AsSpan( 0, Count );

	/// <summary>
	/// GPU lights, the extra suns and their baked entries, optional baked sun, then a black fallback for unmapped bake indices.
	/// </summary>
	internal ReadOnlySpan<GpuLight> UploadedLights => lights.AsSpan( 0, Count + extraLights );

	/// <summary>
	/// Bounding-sphere screen size for shadow selection (<c>CFrustum::ComputeScreenSize</c>).
	/// </summary>
	internal float ScreenSize( int packed ) => packedScreenSize[packed];

	/// <summary>
	/// Assign a projected/cube shadow index before light upload.
	/// </summary>
	internal void SetShadowMapIndex( int packed, uint shadowIndex ) => lights[packed].ShadowMapIndex = shadowIndex;

	internal override void Prepare( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		Count = 0;
		DynamicCount = 0;

		var objects = world.Objects;
		var extents = world.BoundsExtents;
		var visible = pass.Visible( this );
		PrepareDecals( objects, visible );
		PrepareEnvMaps( objects, visible );

		var span = CollectionsMarshal.AsSpan( visible );
		SortByImportance( objects, extents, span, pass.View );

		// Priority-sorted dynamic lights precede indexed baked lights (CLightBinnerStandard::CullAndSortLights).
		PackLights( objects, extents, span, pass.View, baked: false );
		DynamicCount = Count;
		PackLights( objects, extents, span, pass.View, baked: true );

		// Don't keep removed lights alive
		packedObjects.AsSpan( Count ).Clear();

		MapBakedLights( world.Lighting );
	}

	void PackLights( ReadOnlySpan<RenderObject> objects, ReadOnlySpan<Vector3> extents, ReadOnlySpan<int> span, RenderView view, bool baked )
	{
		for ( int i = 0; i < span.Length; i++ )
		{
			// Two slots kept back for the sun's baked entry and the dummy light, as native keeps them
			if ( Count == MaxLights - 2 ) break;

			var light = (LightObject)objects[span[i]];
			if ( light.Baked != baked || light.Radius <= 0 || !light.Visible ) continue;

			EnsureLightCapacity( Count + 1 );
			packedObjects[Count] = light;
			packedScreenSize[Count] = span.Length < 2 ? ComputeScreenSize( light, extents[span[i]], view ) : -sortKeys[i].Size;
			lights[Count] = light.NativePacked ?? Pack( light );
			if ( light.Cookie is { } cookie )
			{
				lights[Count].LightCookieTextureIndex = Borealis.Gpu.GpuContext.BindlessIndex( cookie );
				lights[Count].Flags |= LightCookieEnabled;
			}

			Count++;
		}
	}

	void EnsureLightCapacity( int count )
	{
		if ( count <= lights.Length ) return;

		// The extra suns, and their baked entries, go after the slots native's limit leaves
		var size = Math.Min( Math.Max( count, lights.Length * 2 ), MaxLights + 2 * SceneLighting.MaxExtraSuns );
		Array.Resize( ref lights, size );
		Array.Resize( ref packedObjects, size );
		Array.Resize( ref packedScreenSize, size );
	}

	/// <summary>
	/// Map bake indices to packed lights, including the sun; missing lights use a black fallback
	/// (<c>CLightBinnerStandard::AllocateLights</c>).
	/// </summary>
	void MapBakedLights( SceneLighting lighting )
	{
		bakedIndexMapping.AsSpan().Fill( -1 );

		for ( int i = 0; i < Count; i++ )
		{
			var index = packedObjects[i].BakeIndex;
			if ( index >= 0 && index < bakedIndexMapping.Length ) bakedIndexMapping[index] = i;
		}

		extraLights = 0;
		EnsureLightCapacity( Count + 2 + 2 * SceneLighting.MaxExtraSuns );
		PackExtraSuns( lighting );

		if ( lighting.SunBaked is { } sun && lighting.SunBakeIndex >= 0 && lighting.SunBakeIndex < bakedIndexMapping.Length )
		{
			bakedIndexMapping[lighting.SunBakeIndex] = Count + extraLights;
			lights[Count + extraLights++] = sun;
		}

		var dummy = Count + extraLights;
		dummySlot = dummy;
		lights[Count + extraLights++] = default;

		foreach ( ref var mapping in bakedIndexMapping.AsSpan() )
		{
			if ( mapping < 0 ) mapping = dummy;
		}
	}

	readonly int[] bakedIndexMapping = new int[256];
	int extraLights;
	int dummySlot;

	/// <summary>
	/// Whether a light the baked loop reaches through <see cref="BakedIndexMapping"/> has a shadow map: an indexed light's
	/// own, or an extra sun's. The black fallback's zeros read as a map, but it adds nothing.
	/// </summary>
	bool BakedLightsShadowed()
	{
		foreach ( var slot in bakedIndexMapping )
		{
			if ( slot != dummySlot && lights[slot].ShadowMapIndex != 0xFFFFFFFF ) return true;
		}

		return false;
	}

	/// <summary>
	/// Whether a light the clusters list (the first <see cref="DynamicCount"/>) has a shadow map.
	/// </summary>
	bool ClusteredLightsShadowed()
	{
		for ( int i = 0; i < DynamicCount; i++ )
		{
			if ( lights[i].ShadowMapIndex != 0xFFFFFFFF ) return true;
		}

		return false;
	}

	/// <summary>
	/// Where each bake index's light is in the light buffer, for <c>ViewLightingConfigV2.BakedLightIndexMapping</c>.
	/// </summary>
	internal ReadOnlySpan<int> BakedIndexMapping => bakedIndexMapping;

	/// <summary>
	/// Of the binned lights, how many are dynamic - the first ones, which the clusters hold. The rest are baked.
	/// </summary>
	public int DynamicCount { get; private set; }

	/// <summary>
	/// Baked lights binned for the current view, after the dynamic ones.
	/// </summary>
	public int BakedCount => Count - DynamicCount;

	/// <summary>
	/// Upload lights and dispatch clustering before any render pass.
	/// </summary>
	internal override void Setup( GpuContext context, RenderWorld world, ViewPass pass )
	{
		EnsureResources();
		SizeClusterIndices();

		var view = pass.View;

		var attributes = context.Attributes;
		var lightBuffer = lightRing.Upload( context, UploadedLights );

		// A specialization constant: the baked loop leaves the shadow filters out when no light it reaches has a map
		attributes.Set( NoBakedLightShadowsName, BakedLightsShadowed() ? 0 : 1 );
		attributes.Set( NoClusteredLightShadowsName, ClusteredLightsShadowed() ? 0 : 1 );

		ResolveCubemaps();
		var envMapBuffer = envMapRing.Upload( context, envMaps.AsSpan( 0, EnvMapCount ) );

		PackDecals();
		var decalBuffer = decalRing.Upload( context, decals.AsSpan( 0, DecalCount ) );
		var decalExtraBuffer = decalExtraRing.Upload( context, decalExtras.AsSpan( 0, decalExtraWords ) );

		attributes.Set( LightBufferName, lightBuffer );
		attributes.Set( EnvMapBufferName, envMapBuffer );
		attributes.Set( DecalBufferName, decalBuffer );
		attributes.Set( DecalExtraBufferName, decalExtraBuffer );
		attributes.Set( "ClusterItemCounts", clusterCounts );
		attributes.Set( "ClusterItemIndices", clusterIndices );

		// Z slices are logarithmic between the view's near and far planes, as ClusteredCullingLayer does
		var near = MathF.Max( view.ZNear, 0.0001f );
		var far = MathF.Max( view.ZFar, near + 0.01f );
		var logScale = ClusterCountZ / MathF.Log( far / near );
		var width = MathF.Max( view.Viewport.Width, 1 );
		var height = MathF.Max( view.Viewport.Height, 1 );

		context.SetConstants( ClusterConstantsName, new ClusterConstants
		{
			Counts = new( ClusterCountX, ClusterCountY, ClusterCountZ, ClusterCount ),
			InvCounts = new( 1.0f / ClusterCountX, 1.0f / ClusterCountY, 1.0f / ClusterCountZ, 0 ),
			ZParams = new( logScale, -MathF.Log( near ) * logScale, near, far ),
			ScreenParams = new( width, height, 1.0f / width, 1.0f / height ),

			// Envmaps culled by each cluster's box in their own space, not only its bounding sphere: not in cube captures, whose
			// faces drew envmaps the box test leaves out
			Capacities = new( lightCapacity, envMapCapacity, decalCapacity, view.Capture ? 0 : 1 ),
		} );

		// Write-after-read needs only an execution dependency on previous-frame readers: shading, particle lighting and volumetric fog in compute.
		foreach ( var buffer in ClusterBuffers() )
			context.BufferBarrier( buffer, GpuStage.FragmentShader | GpuStage.ComputeShader, 0, GpuStage.ComputeShader, GpuAccess.ShaderWrite );

		context.BufferBarrier( lightBuffer, GpuStage.Transfer, GpuAccess.TransferWrite,
			GpuStage.ComputeShader | GpuStage.FragmentShader, GpuAccess.ShaderRead );
		context.BufferBarrier( envMapBuffer, GpuStage.Transfer, GpuAccess.TransferWrite,
			GpuStage.ComputeShader | GpuStage.FragmentShader, GpuAccess.ShaderRead );
		context.BufferBarrier( decalBuffer, GpuStage.Transfer, GpuAccess.TransferWrite,
			GpuStage.ComputeShader | GpuStage.FragmentShader, GpuAccess.ShaderRead );
		context.BufferBarrier( decalExtraBuffer, GpuStage.Transfer, GpuAccess.TransferWrite,
			GpuStage.FragmentShader, GpuAccess.ShaderRead );

		// Everything in the view's set is set by now - the environment, the shadows and these - and the culling reads it
		context.WriteViewSet();

		// One thread per cluster - the dispatch divides by the shader's 4x4x4 group size, which grouped culling relies on (64 threads = two mask words)
		context.Dispatch( cullingShader, ClusterCountX, ClusterCountY, ClusterCountZ );

		foreach ( var buffer in ClusterBuffers() )
			context.BufferBarrier( buffer, GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.FragmentShader | GpuStage.ComputeShader, GpuAccess.ShaderRead );
	}

	GpuBuffer[] clusterBuffers;

	/// <summary>
	/// Culling output buffers; an array avoids iterator allocations.
	/// </summary>
	GpuBuffer[] ClusterBuffers() => clusterBuffers ??= [clusterCounts, clusterIndices];

	void EnsureResources()
	{
		cullingShader ??= RenderHost.Current.Device.MaterialFromShader( "shaders/clustered_light_culling_cs.shader" );

		clusterCounts ??= new GpuBuffer<uint>( ClusterCount * 3, GpuBuffer.UsageFlags.Structured, "Borealis ClusterItemCounts" );
	}

	/// <summary>
	/// A type's slots per cluster for <paramref name="count"/> items in the view: no more than the items, rounded up to a
	/// power of two, capped at the type's maximum.
	/// </summary>
	internal static int ClusterCapacity( int count, int max ) => count <= 0 ? 0 : Math.Min( max, (int)BitOperations.RoundUpToPowerOf2( (uint)count ) );

	/// <summary>
	/// Size the index list by the view's lights, envmaps and decals rather than every type's maximum (11.5 MiB a view).
	/// It grows as soon as the items need it and shrinks once they need a quarter of it; a replaced buffer lives until
	/// the frames recorded with it finish.
	/// </summary>
	void SizeClusterIndices()
	{
		lightCapacity = ClusterCapacity( DynamicCount, MaxLightsPerCluster );
		envMapCapacity = ClusterCapacity( EnvMapCount, MaxEnvMapsPerCluster );
		decalCapacity = ClusterCapacity( DecalCount, MaxDecalsPerCluster );

		// One element at least: a buffer can't be empty, and an empty view still binds one
		var needed = Math.Max( ClusterCount * (lightCapacity + envMapCapacity + decalCapacity), 1 );
		if ( clusterIndices is not null && clusterIndices.ElementCount >= needed && clusterIndices.ElementCount / 4 < needed ) return;

		clusterIndices?.Dispose();
		clusterIndices = new GpuBuffer<uint>( needed, GpuBuffer.UsageFlags.Structured, "Borealis ClusterItemIndices" );
		clusterBuffers = null;
	}

	internal override void Dispose()
	{
		// Material.FromShader is shared with other renderers; do not destroy it.
		cullingShader = null;
		lightRing.Dispose();
		envMapRing.Dispose();
		decalRing.Dispose();
		decalExtraRing.Dispose();
		clusterCounts?.Dispose();
		clusterIndices?.Dispose();
		clusterCounts = clusterIndices = null;
		clusterBuffers = null;
	}
}
