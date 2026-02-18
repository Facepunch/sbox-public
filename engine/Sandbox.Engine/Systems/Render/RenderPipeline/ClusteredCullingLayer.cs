using System;
using System.Runtime.InteropServices;
using NativeEngine;
using IntVector3 = global::Vector3Int;

namespace Sandbox.Rendering;

/// <summary>
/// Managed clustered light culling layer. Builds clustered light, envmap, and decal lists for the frame.
/// </summary>
internal class ClusteredCullingLayer : ProceduralRenderLayer
{
	private static readonly IntVector3 ClusterCounts = new( 24, 16, 48 );

	private const int MaxLightsPerCluster = 64;
	private const int MaxEnvMapsPerCluster = 32;
	private const int MaxDecalsPerCluster = 64;

	private static readonly int ClusterCount = ClusterCounts.x * ClusterCounts.y * ClusterCounts.z;

	private static readonly ComputeShader _computeShader = new( "shaders/clustered_light_culling_cs.shader" );

	private GpuBuffer _clusterLightCounts;
	private GpuBuffer _clusterEnvCounts;
	private GpuBuffer _clusterDecalCounts;

	private GpuBuffer _clusterLightIndices;
	private GpuBuffer _clusterEnvIndices;
	private GpuBuffer _clusterDecalIndices;

	private ClusteredLightingConstants _constants;
	private RenderViewport _viewport;

	[StructLayout( LayoutKind.Sequential )]
	private struct ClusteredLightingConstants
	{
		public Vector4 ClusterCounts;          // xyz = counts, w = total
		public Vector4 ClusterInvCounts;       // xyz = inverse counts, w unused
		public Vector4 ClusterZParams;         // x = log scale, y = log bias, z = near, w = far
		public Vector4 ClusterScreenParams;    // x = width, y = height, z = inv width, w = inv height
		public Vector4 ClusterCapacities;      // x = light cap, y = env cap, z = decal cap, w unused
	}

	public ClusteredCullingLayer()
	{
		Name = "Clustered Light Culling";
		Flags |= LayerFlags.NeverRemove;
		Flags |= LayerFlags.DoesntModifyColorBuffers;
		Flags |= LayerFlags.DoesntModifyDepthStencilBuffer;
		Flags |= LayerFlags.NeedsPerViewLightingConstants;

		EnsureResources();
	}

	public void Setup( ISceneView view, RenderViewport viewport )
	{
		_viewport = viewport;
		UpdateConstants( view );

		var nativeAttributes = view.GetRenderAttributesPtr();

		nativeAttributes.SetBufferValue( "ClusterLightCounts", _clusterLightCounts.native );
		nativeAttributes.SetBufferValue( "ClusterEnvMapCounts", _clusterEnvCounts.native );
		nativeAttributes.SetBufferValue( "ClusterDecalCounts", _clusterDecalCounts.native );

		nativeAttributes.SetBufferValue( "ClusterLightIndices", _clusterLightIndices.native );
		nativeAttributes.SetBufferValue( "ClusterEnvMapIndices", _clusterEnvIndices.native );
		nativeAttributes.SetBufferValue( "ClusterDecalIndices", _clusterDecalIndices.native );

		var viewAttributes = new RenderAttributes( nativeAttributes );
		viewAttributes.SetData( "ClusteredLightingConstants", _constants );
	}

	internal override void OnRender()
	{
		var attributes = RenderAttributes.Pool.Get();
		attributes.Set( "ClusterLightCounts", _clusterLightCounts );
		attributes.Set( "ClusterEnvMapCounts", _clusterEnvCounts );
		attributes.Set( "ClusterDecalCounts", _clusterDecalCounts );
		attributes.Set( "ClusterLightIndices", _clusterLightIndices );
		attributes.Set( "ClusterEnvMapIndices", _clusterEnvIndices );
		attributes.Set( "ClusterDecalIndices", _clusterDecalIndices );
		attributes.SetData( "ClusteredLightingConstants", _constants );

		TransitionBuffers( ResourceState.UnorderedAccess );

		_computeShader.DispatchWithAttributes( attributes, ClusterCounts.x, ClusterCounts.y, ClusterCounts.z );

		RenderAttributes.Pool.Return( attributes );

		TransitionBuffers( ResourceState.GenericRead );
	}

	private void EnsureResources()
	{
		_clusterLightCounts ??= CreateStructuredBuffer( ClusterCount, "ClusterLightCounts" );
		_clusterEnvCounts ??= CreateStructuredBuffer( ClusterCount, "ClusterEnvMapCounts" );
		_clusterDecalCounts ??= CreateStructuredBuffer( ClusterCount, "ClusterDecalCounts" );

		_clusterLightIndices ??= CreateStructuredBuffer( ClusterCount * MaxLightsPerCluster, "ClusterLightIndices" );
		_clusterEnvIndices ??= CreateStructuredBuffer( ClusterCount * MaxEnvMapsPerCluster, "ClusterEnvMapIndices" );
		_clusterDecalIndices ??= CreateStructuredBuffer( ClusterCount * MaxDecalsPerCluster, "ClusterDecalIndices" );
	}

	private static GpuBuffer CreateStructuredBuffer( int elementCount, string debugName )
	{
		return new GpuBuffer( elementCount, sizeof( uint ), GpuBuffer.UsageFlags.Structured, debugName );
	}

	private void UpdateConstants( ISceneView view )
	{
		var frustum = view.GetFrustum();
		var nearPlane = MathF.Max( frustum.GetCameraNearPlane(), 0.0001f );
		var farPlane = MathF.Max( frustum.GetCameraFarPlane(), nearPlane + 0.01f );

		var width = MathF.Max( (float)_viewport.Rect.Width, 1f );
		var height = MathF.Max( (float)_viewport.Rect.Height, 1f );

		var counts = ClusterCounts;
		var logScale = counts.z / MathF.Log( farPlane / nearPlane );
		var logBias = -MathF.Log( nearPlane ) * logScale;

		_constants = new ClusteredLightingConstants
		{
			ClusterCounts = new Vector4( counts.x, counts.y, counts.z, ClusterCount ),
			ClusterInvCounts = new Vector4( 1.0f / counts.x, 1.0f / counts.y, 1.0f / counts.z, 0f ),
			ClusterZParams = new Vector4( logScale, logBias, nearPlane, farPlane ),
			ClusterScreenParams = new Vector4( width, height, 1.0f / width, 1.0f / height ),
			ClusterCapacities = new Vector4( MaxLightsPerCluster, MaxEnvMapsPerCluster, MaxDecalsPerCluster, 0f )
		};
	}

	private void TransitionBuffers( ResourceState targetState )
	{
		Graphics.ResourceBarrierTransition( _clusterLightCounts, targetState );
		Graphics.ResourceBarrierTransition( _clusterEnvCounts, targetState );
		Graphics.ResourceBarrierTransition( _clusterDecalCounts, targetState );
		Graphics.ResourceBarrierTransition( _clusterLightIndices, targetState );
		Graphics.ResourceBarrierTransition( _clusterEnvIndices, targetState );
		Graphics.ResourceBarrierTransition( _clusterDecalIndices, targetState );
	}
}
