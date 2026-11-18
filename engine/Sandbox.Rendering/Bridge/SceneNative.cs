using NativeEngine;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// What the mirror reads from native scene objects and worlds through <c>RenderTools</c>, and the native draws it needs.
/// </summary>
internal static class SceneNative
{
	/// <summary>
	/// Replace the list with native per-object draws. Refresh on model/material changes, not every frame.
	/// </summary>
	public static unsafe void ReadSceneObjectDraws( SceneObject sceneObject, List<RenderMesh.DrawState> into )
	{
		into.Clear();

		// SCENEOBJECTDRAW_* in rendertools.cpp
		const int Visible = 1, AlphaBlended = 2, ShadowFastPath = 4, Lightmap = 8;

		for ( int i = 0; ; i++ )
		{
			int mesh = -1, drawCall = 0, flags = 0;
			var material = RenderTools.GetSceneObjectDraw( sceneObject, i, (IntPtr)(&mesh), (IntPtr)(&drawCall), (IntPtr)(&flags) );
			if ( mesh < 0 ) return;

			into.Add( new RenderMesh.DrawState( mesh, drawCall, MirrorContent.Material( Material.FromNative( material ) ), (flags & Visible) != 0, (flags & AlphaBlended) != 0, (flags & ShadowFastPath) != 0, (flags & Lightmap) != 0 ) );
		}
	}

	/// <summary>
	/// Pack native light data, flags and bake index (-1 if absent), excluding shadows (<c>RenderTools.PackSceneLight</c>).
	/// Returns false without a native light. Only the tests use it now, as the reference <see cref="LightPacking"/> matches.
	/// </summary>
	public static unsafe bool PackSceneLight( SceneLight light, out LightBinnerFeature.GpuLight packed, out uint flags, out int bakeIndex )
	{
		LightBinnerFeature.GpuLight d = default;
		int index = -1;
		var result = light.IsValid() ? RenderTools.PackSceneLight( light, (IntPtr)(&d), (IntPtr)(&index) ) : -1;

		packed = d;
		flags = result < 0 ? 0 : (uint)result;
		bakeIndex = index;
		return result >= 0;
	}

	/// <summary>
	/// Shared lightmap-attribute key; zero without lightmaps (<c>RenderTools.GetSceneObjectLightmapKey</c>).
	/// </summary>
	public static ulong LightmapKey( SceneObject sceneObject ) => RenderTools.GetSceneObjectLightmapKey( sceneObject );

	/// <summary>
	/// Read native probe bounds, priority and light groups (<c>RenderTools.GetLightProbeVolume</c>).
	/// </summary>
	public static unsafe void ReadLightProbeVolume( SceneObject lightProbeVolume, Borealis.LightProbeVolume into )
	{
		Vector3 mins, maxs;
		int priority;
		var groups = stackalloc uint[8];
		var count = RenderTools.GetLightProbeVolume( lightProbeVolume, (IntPtr)(&mins), (IntPtr)(&maxs), (IntPtr)(&priority), (IntPtr)groups, 8 );
		into.BoxMins = mins;
		into.BoxMaxs = maxs;
		into.RenderPriority = priority;
		into.LightGroups = new ReadOnlySpan<uint>( groups, Math.Clamp( count, 0, 8 ) ).ToArray();
	}

	/// <summary>
	/// Read wind strength, frequencies and direction; zero without a world renderer (<c>RenderTools.GetSceneWorldWind</c>).
	/// </summary>
	public static unsafe void ReadWorldWind( SceneWorld world, out Vector4 strengthFreq, out Vector4 direction )
	{
		Vector4 strength = default, dir = default;
		if ( world is not null && world.native.IsValid ) RenderTools.GetSceneWorldWind( world, (IntPtr)(&strength), (IntPtr)(&dir) );
		strengthFreq = strength;
		direction = dir;
	}


	/// <summary>
	/// Compare probe packing within float tolerance, excluding the cubemap index (<c>RenderTools.PackSceneEnvMap</c>).
	/// </summary>
	public static unsafe bool PacksAsNative( SceneObject sceneObject, EnvMapObject envMap )
	{
		LightBinnerFeature.GpuEnvMap native;
		if ( !RenderTools.PackSceneEnvMap( sceneObject, (IntPtr)(&native) ) ) return false;

		var mine = LightBinnerFeature.Pack( envMap );
		static bool Near( Vector4 a, Vector4 b ) => a.Distance( b ) <= 1e-3f * MathF.Max( 1, a.Length );
		return Near( mine.Row0, native.Row0 ) && Near( mine.Row1, native.Row1 ) && Near( mine.Row2, native.Row2 ) && Near( mine.BoxMins, native.BoxMins )
			&& Near( mine.BoxMaxs, native.BoxMaxs ) && Near( mine.Color, native.Color ) && mine.Priority == native.Priority;
	}

	/// <summary>
	/// A native aggregate's model, and whether its draws are lit from lightmaps; null if the object isn't an aggregate
	/// (<c>RenderTools.GetAggregateModel</c>).
	/// </summary>
	public static unsafe Model AggregateModel( SceneObject sceneObject, out bool lightmapped )
	{
		bool lit = false;
		var model = RenderTools.GetAggregateModel( sceneObject, (IntPtr)(&lit) );
		lightmapped = lit;
		return model.IsNull ? null : Model.FromNative( model );
	}

	/// <summary>The light probe volume the map compiler bound an object to, 0 for none (<c>RenderTools.GetLightProbeVolumeHandshake</c>).</summary>
	public static int LightProbeHandshake( SceneObject sceneObject ) => RenderTools.GetLightProbeVolumeHandshake( sceneObject );

	/// <summary>
	/// A native aggregate's fragments as it draws them (<c>RenderTools.GetAggregateFragments</c>).
	/// </summary>
	public static unsafe AggregateObject.FragmentDraw[] ReadAggregateFragments( SceneObject sceneObject )
	{
		var count = RenderTools.GetAggregateFragments( sceneObject, IntPtr.Zero, 0 );
		var fragments = new AggregateObject.FragmentDraw[count];
		fixed ( AggregateObject.FragmentDraw* ptr = fragments )
		{
			RenderTools.GetAggregateFragments( sceneObject, (IntPtr)ptr, count );
		}

		return fragments;
	}
}
