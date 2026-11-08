using NativeEngine;
using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// Stupid class for native to call, instances a shadowmapper per lightbinner
/// </summary>
internal static class ShadowMapperCallbacks
{
	/// <summary>
	/// Each pooled lightbinner gets a shadowmapper, use the lightbinner ptr as a handle.. probably fine
	/// There's only gonna be a handful of these
	/// </summary>
	static ConcurrentDictionary<IntPtr, NativeShadowMapper> ShadowMappers = [];

	static NativeShadowMapper Get( IntPtr pLightMapper )
	{
		if ( ShadowMappers.TryGetValue( pLightMapper, out var mapper ) )
			return mapper;

		mapper = ShadowMappers.GetOrAdd( pLightMapper, _ => new NativeShadowMapper() );
		Interlocked.Increment( ref _mappersVersion );
		return mapper;
	}

	// Mappers are only ever added, so a copy of them stays good until the version moves.
	// Values would copy them into a new array on every call.
	sealed record MapperSnapshot( int Version, NativeShadowMapper[] Mappers );
	static int _mappersVersion;
	static MapperSnapshot _mappers = new( 0, [] );

	static NativeShadowMapper[] GetMappers()
	{
		var snapshot = _mappers;
		var version = Volatile.Read( ref _mappersVersion );
		if ( snapshot.Version == version ) return snapshot.Mappers;

		_mappers = snapshot = new( version, ShadowMappers.Values.ToArray() );
		return snapshot.Mappers;
	}

	/// <summary>
	/// Free every mapper's GPU buffers, and the shadow maps both renderers cached. Called at quit, after the last frame.
	/// </summary>
	internal static void Shutdown()
	{
		foreach ( var mapper in ShadowMappers.Values ) mapper.DisposeBuffers();
		Basalt.Shadows.ShadowMapper.ResetCache();
	}

	internal static void InitForView( IntPtr handle, ISceneView sceneView ) => Get( handle ).InitForView( sceneView );
	internal static void SetShaderAttributes( IntPtr handle, CRenderAttributes renderAttr )
	{
		Get( handle ).SetShaderAttributes( renderAttr );
	}

	internal static void UploadToGPU( IntPtr handle ) => Get( handle ).UploadToGPU();
	internal static uint FindOrCreateShadowMaps( IntPtr handle, SceneLight sceneObject, ISceneView view, float flScreenSize ) => Get( handle ).FindOrCreateShadowMaps( sceneObject, view, flScreenSize );
	internal static int DoDirectionalLight( IntPtr handle, SceneLight sceneObject, ISceneView view ) => Get( handle ).DoDirectionalLight( sceneObject, view );

	/// <summary>
	/// Run light command lists for the current Graphics view (after depth).
	/// </summary>
	internal static void RenderScreenSpaceShadows()
	{
		var view = Graphics.SceneView;

		// A managed frame (r_managed_scene) has no native view to draw them for
		if ( view.IsNull ) return;

		foreach ( var sm in GetMappers() )
			sm.RenderScreenSpaceShadows( view );
	}
}
