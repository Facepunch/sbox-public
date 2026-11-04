using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;
using IntVector3 = global::Vector3Int;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// Clustered light culling: builds the frame's clustered light, envmap and decal lists from what the light binner packed.
/// </summary>
internal sealed class ClusteredCullingLayer : NativeProceduralLayer
{
	static readonly IntVector3 ClusterCounts = new( 24, 16, 48 );

	const int MaxLightsPerCluster = 64;
	const int MaxEnvMapsPerCluster = 32;
	const int MaxDecalsPerCluster = 64;

	static readonly int ClusterCount = ClusterCounts.x * ClusterCounts.y * ClusterCounts.z;

	static readonly ComputeShader Compute = new( "shaders/clustered_light_culling_cs.shader" );

	readonly GpuBuffer clusterLightCounts = CreateStructuredBuffer( ClusterCount, "ClusterLightCounts" );
	readonly GpuBuffer clusterEnvCounts = CreateStructuredBuffer( ClusterCount, "ClusterEnvMapCounts" );
	readonly GpuBuffer clusterDecalCounts = CreateStructuredBuffer( ClusterCount, "ClusterDecalCounts" );

	readonly GpuBuffer clusterLightIndices = CreateStructuredBuffer( ClusterCount * MaxLightsPerCluster, "ClusterLightIndices" );
	readonly GpuBuffer clusterEnvIndices = CreateStructuredBuffer( ClusterCount * MaxEnvMapsPerCluster, "ClusterEnvMapIndices" );
	readonly GpuBuffer clusterDecalIndices = CreateStructuredBuffer( ClusterCount * MaxDecalsPerCluster, "ClusterDecalIndices" );

	ClusteredLightingConstants constants;

	[StructLayout( LayoutKind.Sequential )]
	struct ClusteredLightingConstants
	{
		public Vector4 ClusterCounts;          // xyz = counts, w = total
		public Vector4 ClusterInvCounts;       // xyz = inverse counts, w unused
		public Vector4 ClusterZParams;         // x = log scale, y = log bias, z = near, w = far
		public Vector4 ClusterScreenParams;    // x = width, y = height, z = inv width, w = inv height
		public Vector4 ClusterCapacities;      // x = light cap, y = env cap, z = decal cap, w unused
	}

	public ClusteredCullingLayer() : base( "Clustered Light Culling" )
	{
		Flags |= LayerFlags.DoesntModifyColorBuffers | LayerFlags.DoesntModifyDepthStencilBuffer | LayerFlags.NeedsPerViewLightingConstants;
	}

	/// <summary>
	/// Point the view's shaders at this layer's cluster lists, sized for <paramref name="viewport"/>.
	/// </summary>
	public void Setup( ISceneView view, RenderViewport viewport )
	{
		UpdateConstants( view, viewport );

		var nativeAttributes = view.GetRenderAttributesPtr();

		nativeAttributes.SetBufferValue( "ClusterLightCounts", clusterLightCounts.native );
		nativeAttributes.SetBufferValue( "ClusterEnvMapCounts", clusterEnvCounts.native );
		nativeAttributes.SetBufferValue( "ClusterDecalCounts", clusterDecalCounts.native );

		nativeAttributes.SetBufferValue( "ClusterLightIndices", clusterLightIndices.native );
		nativeAttributes.SetBufferValue( "ClusterEnvMapIndices", clusterEnvIndices.native );
		nativeAttributes.SetBufferValue( "ClusterDecalIndices", clusterDecalIndices.native );

		var viewAttributes = new RenderAttributes( nativeAttributes );
		viewAttributes.SetData( "ClusteredLightingConstants", constants );
	}

	protected override void OnRender()
	{
		var attributes = RenderAttributes.Pool.Get();
		attributes.Set( "ClusterLightCounts", clusterLightCounts );
		attributes.Set( "ClusterEnvMapCounts", clusterEnvCounts );
		attributes.Set( "ClusterDecalCounts", clusterDecalCounts );
		attributes.Set( "ClusterLightIndices", clusterLightIndices );
		attributes.Set( "ClusterEnvMapIndices", clusterEnvIndices );
		attributes.Set( "ClusterDecalIndices", clusterDecalIndices );
		attributes.SetData( "ClusteredLightingConstants", constants );

		TransitionBuffers( ResourceState.UnorderedAccess );

		Compute.DispatchWithAttributes( attributes, ClusterCounts.x, ClusterCounts.y, ClusterCounts.z );

		RenderAttributes.Pool.Return( attributes );

		TransitionBuffers( ResourceState.GenericRead );
	}

	static GpuBuffer CreateStructuredBuffer( int elementCount, string debugName )
	{
		return new GpuBuffer( elementCount, sizeof( uint ), GpuBuffer.UsageFlags.Structured, debugName );
	}

	void UpdateConstants( ISceneView view, RenderViewport viewport )
	{
		var frustum = view.GetFrustum();
		var nearPlane = MathF.Max( frustum.GetCameraNearPlane(), 0.0001f );
		var farPlane = MathF.Max( frustum.GetCameraFarPlane(), nearPlane + 0.01f );

		var width = MathF.Max( (float)viewport.Rect.Width, 1f );
		var height = MathF.Max( (float)viewport.Rect.Height, 1f );

		var counts = ClusterCounts;
		var logScale = counts.z / MathF.Log( farPlane / nearPlane );
		var logBias = -MathF.Log( nearPlane ) * logScale;

		constants = new ClusteredLightingConstants
		{
			ClusterCounts = new Vector4( counts.x, counts.y, counts.z, ClusterCount ),
			ClusterInvCounts = new Vector4( 1.0f / counts.x, 1.0f / counts.y, 1.0f / counts.z, 0f ),
			ClusterZParams = new Vector4( logScale, logBias, nearPlane, farPlane ),
			ClusterScreenParams = new Vector4( width, height, 1.0f / width, 1.0f / height ),
			ClusterCapacities = new Vector4( MaxLightsPerCluster, MaxEnvMapsPerCluster, MaxDecalsPerCluster, 0f )
		};
	}

	void TransitionBuffers( ResourceState targetState )
	{
		Graphics.ResourceBarrierTransition( clusterLightCounts, targetState );
		Graphics.ResourceBarrierTransition( clusterEnvCounts, targetState );
		Graphics.ResourceBarrierTransition( clusterDecalCounts, targetState );
		Graphics.ResourceBarrierTransition( clusterLightIndices, targetState );
		Graphics.ResourceBarrierTransition( clusterEnvIndices, targetState );
		Graphics.ResourceBarrierTransition( clusterDecalIndices, targetState );
	}
}
