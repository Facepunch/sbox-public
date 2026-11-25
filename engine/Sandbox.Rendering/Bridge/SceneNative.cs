using NativeEngine;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// What a world's renderer reads from native scene objects and worlds through <c>RenderTools</c>, and the native draws it needs.
/// </summary>
internal static class SceneNative
{
	/// <summary>
	/// Replace the list with native per-object draws. Refresh on model/material changes, not every frame.
	/// </summary>
	/// <remarks><paramref name="material"/> finds each draw's material (a world's, which it holds), else <see cref="MirrorContent.Material"/>.</remarks>
	public static unsafe void ReadSceneObjectDraws( SceneObject sceneObject, List<RenderMesh.DrawState> into, Func<Material, GpuMaterial> material = null )
	{
		into.Clear();

		// SCENEOBJECTDRAW_* in rendertools.cpp
		const int Visible = 1, AlphaBlended = 2, ShadowFastPath = 4, Lightmap = 8;

		for ( int i = 0; ; i++ )
		{
			int mesh = -1, drawCall = 0, flags = 0;
			var native = RenderTools.GetSceneObjectDraw( sceneObject, i, (IntPtr)(&mesh), (IntPtr)(&drawCall), (IntPtr)(&flags) );
			if ( mesh < 0 ) return;

			var drawn = Material.FromNative( native );
			var gpu = material is null ? MirrorContent.Material( drawn ) : material( drawn );
			into.Add( new RenderMesh.DrawState( mesh, drawCall, gpu, (flags & Visible) != 0, Blends( (flags & AlphaBlended) != 0, gpu ), (flags & ShadowFastPath) != 0, (flags & Lightmap) != 0 ) );
		}
	}

	/// <summary>
	/// The draws an object's managed state works out (<see cref="SceneObject.GetDraws"/>), as <see cref="ReadSceneObjectDraws"/>
	/// gives native's.
	/// </summary>
	public static void ReadManagedDraws( SceneObject sceneObject, List<SceneObjectDraw> scratch, List<RenderMesh.DrawState> into, Func<Material, GpuMaterial> material = null )
	{
		sceneObject.GetDraws( scratch );
		into.Clear();
		foreach ( var draw in scratch )
		{
			var gpu = material is null ? MirrorContent.Material( draw.Material ) : material( draw.Material );
			into.Add( new RenderMesh.DrawState( draw.Mesh, draw.DrawCall, gpu, draw.Visible, Blends( draw.Translucent, gpu ), draw.ShadowFastPath, draw.Lightmapped ) );
		}
	}

	/// <summary>
	/// Whether a draw blends: as native's flags say, or as its material on Borealis reads its shader. A material made at run
	/// time from a shader (Rubble's glass, <c>Material.Create</c> of a shader that defines <c>S_TRANSLUCENT</c>) has no
	/// compiled attributes, and under -borealisgpu Source 2's empty render system never loads the shader that exports its
	/// <c>translucent</c> attribute, so native's flags call it opaque.
	/// </summary>
	static bool Blends( bool native, GpuMaterial material ) => native || material is { IsTranslucent: true };

	/// <summary>
	/// Read wind strength, frequencies and direction from the <c>r_world_wind_*</c> settings (<c>RenderTools.GetWorldWind</c>).
	/// </summary>
	public static unsafe void ReadWorldWind( out Vector4 strengthFreq, out Vector4 direction )
	{
		Vector4 strength = default, dir = default;
		RenderTools.GetWorldWind( (IntPtr)(&strength), (IntPtr)(&dir) );
		strengthFreq = strength;
		direction = dir;
	}
}
