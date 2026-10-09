using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Features;

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
		public Vector4 Capacities;     // light, envmap, decal slots per cluster
	}

	readonly UploadRing<GpuLight> lightRing = new( "SceneRenderer lights" );
	readonly UploadRing<GpuEnvMap> envMapRing = new( "SceneRenderer envmaps" );
	GpuLight[] lights = new GpuLight[64];
	LightObject[] packedObjects = new LightObject[64];
	float[] packedScreenSize = new float[64];
	Material cullingShader;

	GpuBuffer<uint> lightCounts, envMapCounts, decalCounts;
	GpuBuffer<uint> lightIndices, envMapIndices;

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
	/// GPU lights, optional baked sun, then a black fallback for unmapped bake indices.
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
			if ( light.Baked != baked || light.Radius <= 0 ) continue;

			EnsureLightCapacity( Count + 1 );
			packedObjects[Count] = light;
			packedScreenSize[Count] = span.Length < 2 ? ComputeScreenSize( light, extents[span[i]], view ) : -sortKeys[i].Size;
			lights[Count++] = light.NativePacked ?? Pack( light );
		}
	}

	void EnsureLightCapacity( int count )
	{
		if ( count <= lights.Length ) return;

		var size = Math.Min( Math.Max( count, lights.Length * 2 ), MaxLights );
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
		EnsureLightCapacity( Count + 2 );

		if ( lighting.SunBaked is { } sun && lighting.SunBakeIndex >= 0 && lighting.SunBakeIndex < bakedIndexMapping.Length )
		{
			bakedIndexMapping[lighting.SunBakeIndex] = Count + extraLights;
			lights[Count + extraLights++] = sun;
		}

		var dummy = Count + extraLights;
		lights[Count + extraLights++] = default;

		foreach ( ref var mapping in bakedIndexMapping.AsSpan() )
		{
			if ( mapping < 0 ) mapping = dummy;
		}
	}

	readonly int[] bakedIndexMapping = new int[256];
	int extraLights;

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
	internal override void Setup( RenderContext context, RenderWorld world, ViewPass pass )
	{
		EnsureResources();

		var view = pass.View;

		var attributes = context.Attributes;
		var lightBuffer = lightRing.Upload( context, UploadedLights );

		ResolveCubemaps();
		var envMapBuffer = envMapRing.Upload( context, envMaps.AsSpan( 0, EnvMapCount ) );

		PackDecals();
		var decalBuffer = decalRing.Upload( context, decals.AsSpan( 0, DecalCount ) );
		var decalExtraBuffer = decalExtraRing.Upload( context, decalExtras.AsSpan( 0, decalExtraWords ) );

		attributes.Set( LightBufferName, lightBuffer );
		attributes.Set( EnvMapBufferName, envMapBuffer );
		attributes.Set( DecalBufferName, decalBuffer );
		attributes.Set( DecalExtraBufferName, decalExtraBuffer );
		attributes.Set( "ClusterLightCounts", lightCounts );
		attributes.Set( "ClusterEnvMapCounts", envMapCounts );
		attributes.Set( "ClusterDecalCounts", decalCounts );
		attributes.Set( "ClusterLightIndices", lightIndices );
		attributes.Set( "ClusterEnvMapIndices", envMapIndices );
		attributes.Set( "ClusterDecalIndices", decalIndices );

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

			Capacities = new( MaxLightsPerCluster, MaxEnvMapsPerCluster, MaxDecalsPerCluster, 0 ),
		} );

		// Write-after-read needs only an execution dependency on previous-frame readers.
		foreach ( var buffer in ClusterBuffers() )
			context.BufferBarrier( buffer, RenderBarrierPipelineStageFlags_t.FragmentShaderBit, 0, RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderBarrierAccessFlags_t.ShaderWriteBit );

		context.BufferBarrier( lightBuffer, RenderBarrierPipelineStageFlags_t.TransferBit, RenderBarrierAccessFlags_t.TransferWriteBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit | RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderBarrierAccessFlags_t.ShaderReadBit );
		context.BufferBarrier( envMapBuffer, RenderBarrierPipelineStageFlags_t.TransferBit, RenderBarrierAccessFlags_t.TransferWriteBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit | RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderBarrierAccessFlags_t.ShaderReadBit );
		context.BufferBarrier( decalBuffer, RenderBarrierPipelineStageFlags_t.TransferBit, RenderBarrierAccessFlags_t.TransferWriteBit,
			RenderBarrierPipelineStageFlags_t.ComputeShaderBit | RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderBarrierAccessFlags_t.ShaderReadBit );
		context.BufferBarrier( decalExtraBuffer, RenderBarrierPipelineStageFlags_t.TransferBit, RenderBarrierAccessFlags_t.TransferWriteBit,
			RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderBarrierAccessFlags_t.ShaderReadBit );

		// One thread per cluster - the dispatch divides by the shader's 8x8x8 group size
		context.Dispatch( cullingShader, ClusterCountX, ClusterCountY, ClusterCountZ );

		foreach ( var buffer in ClusterBuffers() )
			context.BufferBarrier( buffer, RenderBarrierPipelineStageFlags_t.ComputeShaderBit, RenderBarrierAccessFlags_t.ShaderWriteBit, RenderBarrierPipelineStageFlags_t.FragmentShaderBit, RenderBarrierAccessFlags_t.ShaderReadBit );
	}

	GpuBuffer[] clusterBuffers;

	/// <summary>
	/// Culling output buffers; an array avoids iterator allocations.
	/// </summary>
	GpuBuffer[] ClusterBuffers() => clusterBuffers ??= [lightCounts, envMapCounts, decalCounts, lightIndices, envMapIndices, decalIndices];

	void EnsureResources()
	{
		cullingShader ??= Material.FromShader( "shaders/clustered_light_culling_cs.shader" );

		lightCounts ??= new GpuBuffer<uint>( ClusterCount, GpuBuffer.UsageFlags.Structured, "SceneRenderer ClusterLightCounts" );
		envMapCounts ??= new GpuBuffer<uint>( ClusterCount, GpuBuffer.UsageFlags.Structured, "SceneRenderer ClusterEnvMapCounts" );
		decalCounts ??= new GpuBuffer<uint>( ClusterCount, GpuBuffer.UsageFlags.Structured, "SceneRenderer ClusterDecalCounts" );
		lightIndices ??= new GpuBuffer<uint>( ClusterCount * MaxLightsPerCluster, GpuBuffer.UsageFlags.Structured, "SceneRenderer ClusterLightIndices" );
		envMapIndices ??= new GpuBuffer<uint>( ClusterCount * MaxEnvMapsPerCluster, GpuBuffer.UsageFlags.Structured, "SceneRenderer ClusterEnvMapIndices" );
		decalIndices ??= new GpuBuffer<uint>( ClusterCount * MaxDecalsPerCluster, GpuBuffer.UsageFlags.Structured, "SceneRenderer ClusterDecalIndices" );

	}

	internal override void Dispose()
	{
		// Material.FromShader is shared with other renderers; do not destroy it.
		cullingShader = null;
		lightRing.Dispose();
		envMapRing.Dispose();
		decalRing.Dispose();
		decalExtraRing.Dispose();
		decalIndices?.Dispose();
		decalIndices = null;
		envMapIndices?.Dispose();
		envMapIndices = null;
		lightCounts?.Dispose();
		envMapCounts?.Dispose();
		decalCounts?.Dispose();
		lightIndices?.Dispose();
		lightCounts = envMapCounts = decalCounts = lightIndices = null;
		clusterBuffers = null;
	}
}
