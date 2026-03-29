using NativeEngine;
using System.Collections.Concurrent;

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
	static ConcurrentDictionary<IntPtr, ShadowMapper> ShadowMappers = [];

	static ShadowMapper Get( IntPtr pLightMapper ) => ShadowMappers.GetOrAdd( pLightMapper, _ => new() );

	internal static void InitForView( IntPtr handle, ISceneView sceneView ) => Get( handle ).InitForView( sceneView );
	internal static void SetShaderAttributes( IntPtr handle, CRenderAttributes renderAttr )
	{
		Get( handle ).SetShaderAttributes( new RenderAttributes( renderAttr ) );
	}

	internal static void UploadToGPU( IntPtr handle ) => Get( handle ).UploadToGPU();
	internal static uint FindOrCreateShadowMaps( IntPtr handle, SceneLight sceneObject, ISceneView view, float flScreenSize ) => Get( handle ).FindOrCreateShadowMaps( sceneObject, view, flScreenSize );
	internal static int DoDirectionalLight( IntPtr handle, SceneLight sceneObject, ISceneView view ) => Get( handle ).DoDirectionalLight( sceneObject, view );

	internal static void OnLightRemoved( SceneLight sceneObject )
	{
		ShadowMapper.OnLightRemoved( sceneObject );
	}
}
