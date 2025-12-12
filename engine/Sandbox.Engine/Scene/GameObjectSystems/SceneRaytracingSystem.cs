using NativeEngine;

namespace Sandbox;

internal class SceneRaytracingSystem : GameObjectSystem<SceneRaytracingSystem>
{
	// This could all be done on C# side, all below does is to update the TLAS, todo
	internal IRayTraceSceneWorld native;

	bool Supported => g_pRenderDevice.IsRayTracingSupported();

	public SceneRaytracingSystem( Sandbox.Scene scene ) : base( scene )
	{
		if ( !Supported )
			return;
		// Update TLAS after bones are updated
		Listen( Stage.UpdateBones, Int32.MaxValue, UpdateSkinnedForRaytracing, "UpdateSkinnedForRaytracing" );
		Listen( Stage.FinishUpdate, Int32.MaxValue, UpdateRaytracing, "UpdateRaytracing" );

		var RAY_TYPE_COUNT = 2;
		native = CSceneSystem.CreateRayTraceWorld( Scene.Name, RAY_TYPE_COUNT );
	}

	~SceneRaytracingSystem()
	{
		if ( !Supported )
			return;

		CSceneSystem.DestroyRayTraceWorld( native );
	}


	void UpdateSkinnedForRaytracing()
	{
		using var _ = PerformanceStats.Timings.Render.Scope();

		var allSkinnedRenderers = Scene.GetAllComponents<SkinnedModelRenderer>()
										.ToArray();

		foreach ( var renderer in allSkinnedRenderers )
		{
			if ( !renderer.IsValid() )
				continue;

		}
	}

	/// <summary>
	/// Top-level acceleration structure needs to be updated every frame
	/// </summary>
	internal void UpdateRaytracing()
	{
		using var _ = PerformanceStats.Timings.Render.Scope();

		native.BuildTLASForWorld( Scene.SceneWorld, Scene.RenderAttributes.Get() );
	}
}
