using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System.Collections.Generic;
using System.Linq;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Blending: which layer each object draws in, native's back to front sort, and what shadow views take.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static: one test's frame
// can't run in the middle of another's
[TestClass]
[DoNotParallelize]
public class TranslucencyTests
{
	[TestMethod]
	public void SortDistanceMatchesNative()
	{
		// A hundredth of a unit to the closest point of the bounds, truncated
		Assert.AreEqual( 900u, MeshRenderFeature.TranslucentSortDistance( new Vector3( 10, 0, 0 ), new Vector3( 1 ), Vector3.Zero ) );
		Assert.AreEqual( 1250u, MeshRenderFeature.TranslucentSortDistance( new Vector3( 0, 13.5f, 0 ), new Vector3( 1 ), Vector3.Zero ) );

		// Off a corner: the distance to the corner, (3, 4, 0)
		Assert.AreEqual( 500u, MeshRenderFeature.TranslucentSortDistance( new Vector3( 4, 5, 0 ), new Vector3( 1 ), Vector3.Zero ) );

		// From inside the bounds it's 0 - drawn last, on top
		Assert.AreEqual( 0u, MeshRenderFeature.TranslucentSortDistance( new Vector3( 10, 0, 0 ), new Vector3( 5 ), new Vector3( 8, 1, -2 ) ) );
	}

	/// <summary>
	/// One of each kind, spread along the view's axis so the translucent ones sort by distance: opaque, faded
	/// opaque, partially blended, and three translucent - the middle one faded.
	/// </summary>
	sealed class BlendWorld
	{
		public readonly RenderWorld World = new();
		public readonly MeshObject Opaque, Faded, Partial, Near, Middle, Far;

		public BlendWorld( float spacing )
		{
			var opaqueMesh = TestMesh( false );
			var translucentMesh = TestMesh( true );
			var half = Color.White.WithAlpha( 0.5f );

			Opaque = Add( opaqueMesh, spacing, 3 );
			Faded = Add( opaqueMesh, spacing, -3 );
			Faded.Tint = half;
			Near = Add( translucentMesh, spacing, 0 );
			Middle = Add( translucentMesh, spacing * 1.5f, 0 );
			Middle.Tint = half;
			Partial = Add( TestMesh( false, true ), spacing * 2, 0 );
			Far = Add( translucentMesh, spacing * 3, 0 );
		}

		MeshObject Add( RenderMesh mesh, float x, float y )
		{
			var obj = new MeshObject( mesh, new Transform( new Vector3( x, y, 0 ) ) );
			World.Add( obj );
			return obj;
		}

		public int CountIn( List<int> visible, params MeshObject[] objects ) => visible.Count( i => objects.Contains( World.Objects[i] ) );
	}

	[TestMethod]
	public void LayersSplitByBlend()
	{
		var scene = new BlendWorld( 20 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		var feature = system.GetFeature<MeshRenderFeature>();

		CollectAndPrepare( system, scene.World, view );
		var layers = feature.RunsFor( system.MainPass );

		// Opaque: the opaque object and the partial one's opaque draws; faded opaque in its own layer
		Assert.AreEqual( 2, layers.Opaque.Count );
		Assert.IsTrue( layers.Opaque.Any( r => r.Mesh == scene.Opaque.Mesh && r.Filter == MeshRenderFeature.DrawFilter.All ) );
		Assert.IsTrue( layers.Opaque.Any( r => r.Mesh == scene.Partial.Mesh && r.Filter == MeshRenderFeature.DrawFilter.Opaque ) );
		Assert.AreEqual( 1, layers.Faded.Count );
		Assert.AreEqual( 1, layers.Faded[0].Count );

		// Translucent, back to front: far, the partial one's blended draws, then middle and near - which share a
		// mesh and are neighbours in that order, so they instance together. The main view keeps faded translucency.
		var translucent = layers.Translucent;
		Assert.AreEqual( 3, translucent.Count );
		Assert.AreEqual( scene.Far.Mesh, translucent[0].Mesh );
		Assert.AreEqual( 1, translucent[0].Count );
		Assert.AreEqual( scene.Partial.Mesh, translucent[1].Mesh );
		Assert.AreEqual( MeshRenderFeature.DrawFilter.Translucent, translucent[1].Filter );
		Assert.AreEqual( scene.Near.Mesh, translucent[2].Mesh );
		Assert.AreEqual( 2, translucent[2].Count, "middle then near, one instanced run" );
		Assert.IsTrue( translucent[0].FirstInstance < translucent[1].FirstInstance && translucent[1].FirstInstance < translucent[2].FirstInstance, "instances written in draw order" );

		// The partial object is in two layers, with one transform entry
		var partialOpaque = layers.Opaque.Single( r => r.Mesh == scene.Partial.Mesh );
		Assert.AreEqual( system.Transforms.InstanceSlot( partialOpaque.FirstInstance ), system.Transforms.InstanceSlot( translucent[1].FirstInstance ), "both layers read its one entry" );
	}

	/// <summary>
	/// Shadow views take native's three shadow layers (<c>CSceneSystem::AddShadowView</c>): opaque casters,
	/// faded ones, and translucent ones with nothing opaque in them - faded translucent casters are culled.
	/// </summary>
	[TestMethod]
	public void ShadowLayers()
	{
		var scene = new BlendWorld( 3 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		var feature = system.GetFeature<MeshRenderFeature>();
		Sandbox.Rendering.ShadowMapper.ResetCache();

		CollectAndPrepare( system, scene.World, view );

		var passes = system.ShadowMaps.Passes;
		Assert.IsTrue( passes.Length > 0 );

		var translucentCasters = 0;
		foreach ( var pass in passes )
		{
			var layers = feature.RunsFor( pass );
			var visible = pass.Visible( feature );

			Assert.AreEqual( scene.CountIn( visible, scene.Opaque, scene.Partial ), layers.Opaque.Sum( r => r.Count ), "opaque, and the partial one as opaque" );
			Assert.AreEqual( scene.CountIn( visible, scene.Faded ), layers.Faded.Sum( r => r.Count ), "faded opaque casters dither" );
			Assert.AreEqual( scene.CountIn( visible, scene.Near, scene.Far ), layers.Translucent.Sum( r => r.Count ), "not the faded or partial ones" );
			translucentCasters += layers.Translucent.Sum( r => r.Count );
		}

		Assert.IsTrue( translucentCasters > 0, "some shadow view took the translucent casters" );
	}
}
