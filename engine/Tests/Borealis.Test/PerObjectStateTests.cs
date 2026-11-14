using Borealis;
using Borealis.Features;
using BorealisTests.Headless;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// What one object draws differently from others with its model: body groups and materials (mesh variants), the
/// tags a view filters by, and render attributes of its own.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class PerObjectStateTests
{
	static uint Token( string tag ) => new StringToken( tag ).Value;

	[TestMethod]
	public void VariantHidesAndChangesDraws()
	{
		var mesh = TestMesh( false, false, false );

		// Hide draw 1 (a body group that's off), make draw 2 blend (a translucent material in its place)
		RenderMesh.DrawState[] states =
		[
			new( 0, 0, null, Visible: true, Translucent: false, ShadowFastPath: false ),
			new( 0, 1, null, Visible: false, Translucent: false, ShadowFastPath: false ),
			new( 0, 2, null, Visible: true, Translucent: true, ShadowFastPath: false ),
		];

		var variant = mesh.WithDraws( states );
		Assert.AreNotSame( mesh, variant );

		var draws = variant.DrawsForLod( 0 ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 2 }, draws.Select( d => d.DrawCall ).ToArray(), "the hidden draw is left out" );
		Assert.IsTrue( draws[1].Translucent );
		Assert.AreEqual( 24, variant.TrianglesForLod( 0 ), "two draws of twelve triangles" );
		Assert.AreEqual( MeshBlend.Partial, variant.BlendForLod( 0 ), "some draws blend now" );
		Assert.AreEqual( MeshBlend.Opaque, mesh.BlendForLod( 0 ), "the model's own mesh is untouched" );
	}

	[TestMethod]
	public void VariantOfSameStateIsTheMesh()
	{
		// An object drawn as the model is shares its mesh, and so instances with every other one
		var mesh = TestMesh( false, true );
		RenderMesh.DrawState[] states =
		[
			new( 0, 0, null, true, false, false ),
			new( 0, 1, null, true, true, false ),
		];

		Assert.AreSame( mesh, mesh.WithDraws( states ) );
		Assert.AreSame( mesh, mesh.WithDraws( [] ), "draws native didn't report are drawn as they are" );
	}

	[TestMethod]
	public void VariantKeepsTheDrawsMaterialWhenTheStateHasNone()
	{
		// On a Borealis.Gpu device a material made in code has no file to load: the draw keeps the model's
		using var model = new HeadlessMaterial( "model", "shaders/complex.shader" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, model )]] );

		var variant = mesh.WithDraws( [new RenderMesh.DrawState( 0, 0, null, true, true, false )] );

		Assert.AreNotSame( mesh, variant, "the state still changes the draw's blending" );
		Assert.AreSame( model, variant.DrawsForLod( 0 ).ToArray()[0].Material );
	}

	[TestMethod]
	public void VariantKeepsSkinning()
	{
		var mesh = TestSkinnedMesh( 2 );
		var variant = mesh.WithDraws( [new RenderMesh.DrawState( 0, 0, null, true, true, false )] );

		Assert.AreNotSame( mesh, variant );
		Assert.IsTrue( variant.IsSkinned );
		Assert.AreSame( mesh.SkinFor( 0 ), variant.SkinFor( 0 ) );
		Assert.AreSame( mesh.BindPose, variant.BindPose );
	}

	[TestMethod]
	public void TagsFilterAsNativeDoes()
	{
		var view = LookingDownX();
		var tagged = new TestObject( UnitBox, Transform.Zero ) { Tags = [Token( "player" )] };
		var untagged = new TestObject( UnitBox, Transform.Zero );
		var world = new TestObject( UnitBox, Transform.Zero ) { IsWorld = true };
		var worldTagged = new TestObject( UnitBox, Transform.Zero ) { IsWorld = true, Tags = [Token( "player" )] };

		// No filter: everything
		Assert.IsTrue( view.Shows( tagged ) && view.Shows( untagged ) && view.Shows( world ) );

		// Render tags need one of them - world objects need "world" instead of their own
		view.RenderTags = [Token( "player" )];
		Assert.IsTrue( view.Shows( tagged ) );
		Assert.IsFalse( view.Shows( untagged ) );
		Assert.IsFalse( view.Shows( worldTagged ), "a world object's own tags don't count for render tags" );
		view.RenderTags = [Token( "world" )];
		Assert.IsTrue( view.Shows( world ) );
		Assert.IsFalse( view.Shows( tagged ) );
		view.RenderTags = [];

		// Exclude tags drop any object with one, and world objects when one is "world"
		view.ExcludeTags = [Token( "player" )];
		Assert.IsFalse( view.Shows( tagged ) );
		Assert.IsFalse( view.Shows( worldTagged ), "a world object's own tags still exclude it" );
		Assert.IsTrue( view.Shows( untagged ) && view.Shows( world ) );
		view.ExcludeTags = [Token( "world" )];
		Assert.IsFalse( view.Shows( world ) );
		Assert.IsTrue( view.Shows( tagged ) && view.Shows( untagged ) );
	}

	[TestMethod]
	public void ExcludedObjectsAreNotCollected()
	{
		var mesh = TestMesh( false );
		var world = new RenderWorld();
		var shown = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		var hidden = new MeshObject( mesh, new Transform( new Vector3( 20, 5, 0 ) ) ) { Tags = [Token( "viewer" )] };
		world.Add( shown );
		world.Add( hidden );

		var view = LookingDownX();
		view.ExcludeTags = [Token( "viewer" )];
		using var system = new RenderSystem();
		CollectAndPrepare( system, world, view );

		var visible = system.MainPass.Visible( system.GetFeature<MeshRenderFeature>() );
		Assert.AreEqual( 1, visible.Count );
		Assert.AreSame( shown, world.Objects[visible[0]] );
	}

	[TestMethod]
	public void SubWorldsShowOnlyInViewsThatDrawThem()
	{
		var view = LookingDownX();
		var own = new TestObject( UnitBox, Transform.Zero );
		var first = new TestObject( UnitBox, Transform.Zero ) { SubWorld = 1 };
		var last = new TestObject( UnitBox, Transform.Zero ) { SubWorld = RenderWorld.MaxSubWorlds, Tags = [Token( "gizmo" )] };

		Assert.IsTrue( view.Shows( own ) );
		Assert.IsFalse( view.Shows( first ), "a view draws no sub-world unless it says so" );
		Assert.IsFalse( view.Shows( last ) );

		view.SubWorlds = 1;
		Assert.IsTrue( view.Shows( first ) && view.Shows( own ) );
		Assert.IsFalse( view.Shows( last ) );

		view.SubWorlds = 1UL << (RenderWorld.MaxSubWorlds - 1);
		Assert.IsTrue( view.Shows( last ) );
		Assert.IsFalse( view.Shows( first ) );

		// Tags still apply to a sub-world's objects
		view.ExcludeTags = [Token( "gizmo" )];
		Assert.IsFalse( view.Shows( last ) );

		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => own.SubWorld = RenderWorld.MaxSubWorlds + 1 );
		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => own.SubWorld = -1 );
	}

	[TestMethod]
	public void SubWorldObjectsAreCollectedAndCastShadowsOnlyWhenDrawn()
	{
		var mesh = TestMesh( false );
		var world = new RenderWorld();
		var own = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		var extra = new MeshObject( mesh, new Transform( new Vector3( 20, 5, 0 ) ) ) { SubWorld = 3 };
		world.Add( own );
		world.Add( extra );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var feature = system.GetFeature<MeshRenderFeature>();

		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );
		CollectionAssert.AreEqual( new[] { own.Index }, system.MainPass.Visible( feature ).ToArray() );
		Assert.IsTrue( ShadowCasters( system, feature ).Contains( own.Index ), "the sun's cascades draw the world" );
		Assert.IsFalse( ShadowCasters( system, feature ).Contains( extra.Index ), "nor in the sun's shadow views" );

		view.SubWorlds = 1UL << 2;
		CollectAndPrepare( system, world, view );
		CollectionAssert.AreEquivalent( new[] { own.Index, extra.Index }, system.MainPass.Visible( feature ).ToArray() );
		Assert.IsTrue( ShadowCasters( system, feature ).Contains( extra.Index ), "a drawn sub-world casts shadows, as native's shadow views copy their view's worlds" );
	}

	static System.Collections.Generic.HashSet<int> ShadowCasters( RenderSystem system, MeshRenderFeature feature )
	{
		var casters = new System.Collections.Generic.HashSet<int>();
		foreach ( var pass in system.ShadowMaps.Passes ) casters.UnionWith( pass.Visible( feature ) );
		return casters;
	}

	[TestMethod]
	public void ObjectsWithAttributesDrawAlone()
	{
		// Three objects sharing a mesh: two plain ones instance together, the one with attributes of its own doesn't
		var mesh = TestMesh( false );
		var world = new RenderWorld();
		var attributes = new HeadlessParameters();
		for ( int i = 0; i < 3; i++ )
		{
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 20, i * 3, 0 ) ) ) { Attributes = i == 1 ? attributes : null } );
		}

		var view = LookingDownX();
		using var system = new RenderSystem();
		CollectAndPrepare( system, world, view );

		var runs = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque;
		Assert.AreEqual( 2, runs.Count );
		Assert.AreEqual( 2, runs.Single( r => r.Attributes is null ).Count );
		Assert.AreEqual( 1, runs.Single( r => r.Attributes == attributes ).Count );
	}

	/// <summary>
	/// A draw's own tint multiplies into its object's, per draw, as native's instances carry them: an object whose mesh has one
	/// gets an entry per draw, and each draw of its run reads its own block of ids.
	/// </summary>
	[TestMethod]
	public void DrawTintsMultiplyPerDraw()
	{
		var plain = new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null );
		var tinted = new RenderMesh.Draw( 0, 1, 0, 36, 24, 0, null, Tint: new Vector3( 0.5f, 1, 0.25f ) );
		var mesh = new RenderMesh( UnitBox, [[plain, tinted]] );
		Assert.IsTrue( mesh.HasDrawTints );

		var world = new RenderWorld();
		var red = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) ) { Tint = new Color( 1, 0, 0 ) };
		var white = new MeshObject( mesh, new Transform( new Vector3( 20, 3, 0 ) ) );
		world.Add( red );
		world.Add( white );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var run = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque.Single();
		Assert.IsTrue( run.Tinted );
		Assert.AreEqual( 2, run.Count, "both objects still instance together" );

		// Draw 0 reads the first block, draw 1 the next: each instance's entry for that draw
		var transforms = system.Transforms;
		var tints = new System.Collections.Generic.HashSet<uint>();
		for ( int i = 0; i < run.Count; i++ )
		{
			Assert.AreEqual( transforms.InstanceSlot( run.FirstInstance + i ) + 1, transforms.InstanceSlot( run.FirstInstance + run.Count + i ), "the next entry" );
			tints.Add( transforms[transforms.InstanceSlot( run.FirstInstance + run.Count + i )].TintRgb888 );
		}

		// Red times the draw's tint is (0.5, 0, 0); white times it is the draw's own
		CollectionAssert.AreEquivalent( new[] { 0x800000u, 0x80FF40u }, tints.ToArray() );
	}

	/// <summary>
	/// Attributes read as objects draw (<see cref="MeshObject.AttributesFrom"/>): asked once a render for an object a view
	/// draws, never for one off screen, and counted by the world so one without any doesn't look for them.
	/// </summary>
	[TestMethod]
	public void AttributesAreReadOnceARenderForObjectsDrawn()
	{
		var world = new RenderWorld();
		var onScreen = new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) );
		var offScreen = new MeshObject( TestMesh( false ), new Transform( new Vector3( -30, 0, 0 ) ) );
		world.Add( onScreen );
		world.Add( offScreen );
		Assert.AreEqual( 0, world.AttributeSources );

		var source = new CountingSource();
		onScreen.AttributesFrom = source;
		offScreen.AttributesFrom = source;
		Assert.AreEqual( 2, world.AttributeSources );

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		// Without shadows, whose cascades would draw the one behind the camera
		using var system = new RenderSystem { Shadows = false };
		for ( int frame = 1; frame <= 2; frame++ )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
			Assert.AreEqual( frame, source.ReadsOf( onScreen ), "once a render, whichever views draw it" );
			Assert.AreEqual( 0, source.ReadsOf( offScreen ), "nothing draws it" );
		}

		world.Remove( offScreen );
		Assert.AreEqual( 1, world.AttributeSources );
		offScreen.AttributesFrom = null;
		onScreen.AttributesFrom = null;
		Assert.AreEqual( 0, world.AttributeSources );
	}

	sealed class CountingSource : IAttributeSource
	{
		public readonly System.Collections.Generic.Dictionary<MeshObject, int> Reads = new();

		public int ReadsOf( MeshObject mesh ) => Reads.TryGetValue( mesh, out var reads ) ? reads : 0;

		public void Refresh( MeshObject mesh ) => Reads[mesh] = ReadsOf( mesh ) + 1;
	}
}
