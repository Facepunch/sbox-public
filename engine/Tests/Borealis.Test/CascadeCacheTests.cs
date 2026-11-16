using Borealis;
using Borealis.Culling;
using Borealis.Features;
using Borealis.Shadows;
using BorealisTests.Headless;
using System;
using System.Collections.Generic;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// The sun's cascades: casters sized by each cascade's texels, and the cached far cascade, kept while the camera turns and
/// zooms, drawn again a strip at a time as it walks.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose caches and frame counter are static
[TestClass]
[DoNotParallelize]
public class CascadeCacheTests
{
	bool wasCached;

	[TestInitialize]
	public void Start()
	{
		ShadowMapper.ResetCache();
		wasCached = ShadowMapper.FarCascadeCache;
		ShadowMapper.FarCascadeCache = true;
	}

	[TestCleanup]
	public void Finish()
	{
		ShadowMapper.FarCascadeCache = wasCached;
		ShadowMapper.ResetCache();
	}

	static ViewPass[] CacheViews( RenderSystem system ) => system.ShadowMaps.Passes.ToArray().Where( p => p.View.Name == "CSM Far Cache" ).ToArray();

	static ViewPass FarView( RenderSystem system ) => system.ShadowMaps.Passes.ToArray().Single( p => p.StaticFilter == StaticFilter.Unsettled );

	static RenderWorld SunWorld()
	{
		var world = new RenderWorld();
		world.Lighting.SunDirection = new Angles( 60, 30, 0 ).Forward;
		return world;
	}

	static MeshObject Box( RenderWorld world, Vector3 position, bool isStatic )
	{
		var box = new MeshObject( null, new Transform( position ) ) { LocalBounds = new BBox( new Vector3( -20 ), new Vector3( 20 ) ), IsStatic = isStatic };
		world.Add( box );
		return box;
	}

	/// <summary>
	/// A cascade culls casters narrower than one of its texels, wherever they are: not by how big they are on the camera's
	/// screen.
	/// </summary>
	[TestMethod]
	public void CascadeCullsByItsTexelsWhereverTheCameraIs()
	{
		var view = new RenderView { Orthographic = true, OrthoSize = new Vector2( 2048 ), Viewport = new Rect( 0, 0, 1024, 1024 ) };
		view.Update();
		var pass = new ViewPass { View = view, Root = LookingDownX(), TexelSized = true };
		var size = SizeCull.ForPass( pass );

		// Two units a texel: a sphere of radius under one is culled, near the camera or not
		Assert.IsTrue( size.Culls( new Vector3( 5, 0, 0 ), new Vector3( 0.5f ) ) );
		Assert.IsFalse( size.Culls( new Vector3( 1e5f, 0, 0 ), new Vector3( 1.0f ) ) );
		Assert.IsTrue( size.CameraAndThreshold.w < 0, "the GPU cull gets the texel test" );

		pass.TexelSized = false;
		Assert.IsFalse( SizeCull.ForPass( pass ).Culls( new Vector3( 5, 0, 0 ), new Vector3( 0.5f ) ), "the camera's screen keeps it" );
	}

	/// <summary>
	/// The cached far cascade is drawn once, then kept while the camera turns, zooms and moves within a cell. Crossing a cell
	/// draws one strip of cells; the sun turning draws all of it again.
	/// </summary>
	[TestMethod]
	public void FarCascadeIsKeptAndScrolls()
	{
		var world = SunWorld();
		Box( world, new Vector3( 100, 0, 0 ), isStatic: true );
		var view = LookingDownX();
		using var system = new RenderSystem();

		CollectAndPrepare( system, world, view );
		Assert.IsTrue( CacheViews( system ).Length > 0, "drawn the first time" );
		var far = ShadowMapper.FarCaches.Single();
		Assert.AreEqual( 1, far.Draws );

		void Kept( string change, Action act )
		{
			act();
			view.Update();
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( 0, CacheViews( system ).Length, change );
			Assert.AreEqual( 1, far.Draws, change );
		}

		Kept( "nothing", () => { } );
		Kept( "turned", () => view.Rotation = Rotation.From( 10, 120, 0 ) );
		Kept( "zoomed", () => view.FieldOfView = 30 );

		// A cell is an eighth of the cache, about 2,570 units on the default settings
		var rotation = world.Lighting.SunDirection.EulerAngles.ToRotation();
		var grid = RenderHost.Current.Shadows.CascadeDistance * ShadowMapper.FarCacheDistanceScale * 2 / (1 - 1.0f / ShadowMapper.FarCacheCells) / ShadowMapper.FarCacheCells;
		Kept( "moved within a cell", () => view.Position = rotation.Right * grid * 0.3f );

		view.Position = rotation.Right * grid * 1.2f;
		view.Update();
		CollectAndPrepare( system, world, view );
		Assert.IsTrue( CacheViews( system ).Length > 0, "crossed a cell" );
		Assert.AreEqual( 1, far.Draws, "not drawn whole" );
		Assert.AreEqual( ShadowMapper.FarCacheCells, far.CellsDrawn, "one strip of cells" );
		Assert.IsTrue( FarView( system ).CacheOffset != default, "the cascade unwraps it" );

		world.Lighting.SunDirection = new Angles( 61, 30, 0 ).Forward;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 2, far.Draws, "the sun turned" );
	}

	/// <summary>
	/// A walk through a level: 30 seconds at 60 frames a second and 320 units a second, turning as it goes, looking around.
	/// Under one frame in a hundred draws any of the cache, and never all of it.
	/// </summary>
	[TestMethod]
	public void WalkingRarelyDrawsTheFarCascade()
	{
		var world = SunWorld();
		Box( world, new Vector3( 100, 0, 0 ), isStatic: true );
		var view = LookingDownX();
		using var system = new RenderSystem();
		CollectAndPrepare( system, world, view );
		var far = ShadowMapper.FarCaches.Single();

		var position = Vector3.Zero;
		var drawing = 0;
		const int frames = 30 * 60;
		for ( int frame = 0; frame < frames; frame++ )
		{
			var heading = 40.0f * MathF.Sin( frame * 0.004f ) + frame * 0.05f;
			position += Rotation.From( 0, heading, 0 ).Forward * (320.0f / 60.0f);
			view.Position = position;
			view.Rotation = Rotation.From( 10.0f * MathF.Sin( frame * 0.03f ), heading + 30.0f * MathF.Sin( frame * 0.02f ), 0 );
			view.Update();
			CollectAndPrepare( system, world, view );
			if ( CacheViews( system ).Length > 0 ) drawing++;
		}

		Assert.AreEqual( 1, far.Draws, "never drawn whole" );
		Assert.IsTrue( drawing * 100 < frames, $"drew in {drawing} of {frames} frames" );
	}

	/// <summary>
	/// A static caster changing in the cache draws only the cells it covers again.
	/// </summary>
	[TestMethod]
	public void FarCascadeRedrawsOnlyChangedCells()
	{
		var world = SunWorld();
		var box = Box( world, new Vector3( 100, 0, 0 ), isStatic: true );
		var view = LookingDownX();
		using var system = new RenderSystem();
		CollectAndPrepare( system, world, view );
		CollectAndPrepare( system, world, view );
		var far = ShadowMapper.FarCaches.Single();

		box.Transform = new Transform( new Vector3( 110, 0, 0 ) );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 1, far.Draws );
		Assert.IsTrue( far.CellsDrawn is > 0 and <= 4, $"{far.CellsDrawn} cells" );

		var mesh = system.GetFeature<MeshRenderFeature>();
		Assert.IsTrue( CacheViews( system ).Any( p => p.Visible( mesh ).Contains( box.Index ) ) );
	}

	/// <summary>
	/// A caster that isn't static is drawn over the cache every frame until it has settled, then the cache takes it, until it
	/// moves again.
	/// </summary>
	[TestMethod]
	public void SettledCastersMoveIntoTheCache()
	{
		var world = SunWorld();
		var box = Box( world, new Vector3( 100, 0, 0 ), isStatic: false );
		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		CollectAndPrepare( system, world, view );
		Assert.IsTrue( FarView( system ).Visible( mesh ).Contains( box.Index ), "drawn on top while it might move" );
		Assert.IsFalse( CacheViews( system ).Any( p => p.Visible( mesh ).Contains( box.Index ) ) );

		// It has stayed put long enough, and so has the cache
		SettledCasters.Reset( settled: true );
		CollectAndPrepare( system, world, view );
		var far = ShadowMapper.FarCaches.Single();
		far.DrawnAt -= SettledCasters.Seconds;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 2, far.Draws, "drawn again to take it" );
		Assert.IsTrue( CacheViews( system ).Any( p => p.Visible( mesh ).Contains( box.Index ) ) );
		Assert.IsFalse( FarView( system ).Visible( mesh ).Contains( box.Index ), "not on top any more" );

		box.Transform = new Transform( new Vector3( 140, 0, 0 ) );
		CollectAndPrepare( system, world, view );
		Assert.IsTrue( FarView( system ).Visible( mesh ).Contains( box.Index ), "moved: on top again" );
		Assert.IsTrue( far.CellsDrawn > 0, "and its old place drawn again" );
	}

	/// <summary>
	/// The view drawn over the cache queries only the casters the cache doesn't hold: its tree query leaves the settled ones
	/// out, and it draws what it draws when it culls each of them.
	/// </summary>
	[TestMethod]
	public void FarViewQueriesOnlyUnsettledCasters()
	{
		var world = SunWorld();
		var boxes = new List<MeshObject>();
		for ( int i = 0; i < 20; i++ ) boxes.Add( Box( world, new Vector3( 100 + i * 50, 0, 0 ), isStatic: false ) );
		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		// All of them settled into the cache, then one moves
		SettledCasters.Tick( 1e6f );
		SettledCasters.Reset( settled: true );
		CollectAndPrepare( system, world, view );
		var far = ShadowMapper.FarCaches.Single();
		far.DrawnAt -= SettledCasters.Seconds;
		CollectAndPrepare( system, world, view );
		var moving = boxes[3];
		moving.Transform = new Transform( new Vector3( 260, 10, 0 ) );
		CollectAndPrepare( system, world, view );

		var pass = FarView( system );
		Assert.IsNotNull( pass.TreeKept, "marked" );
		var candidates = new List<int>();
		RenderSystem.FindCandidates( world, pass, candidates, out var inTree, out _ );
		CollectionAssert.AreEqual( new[] { moving.Index }, candidates.Take( inTree ).Select( c => c >> 1 ).ToArray(), "only the one that moved" );
		var marked = pass.Visible( mesh ).ToArray();
		CollectionAssert.AreEqual( new[] { moving.Index }, marked );

		ShadowSystem.MarksUnsettled = false;
		try
		{
			CollectAndPrepare( system, world, view );
			Assert.IsNull( FarView( system ).TreeKept );
			CollectionAssert.AreEquivalent( marked, FarView( system ).Visible( mesh ).ToArray(), "the same casters culled one by one" );
		}
		finally
		{
			ShadowSystem.MarksUnsettled = true;
		}
	}

	/// <summary>
	/// A cascade view that would leave its map as it is, drawing no casters over a clear or over the copy of an unchanged
	/// cache, is left out of the frame, and so are its tiles. A caster, the frame after one and a changed cache draw it again.
	/// The sun's constants read the same either way.
	/// </summary>
	[TestMethod]
	public void UnchangedCascadesAreKept()
	{
		var world = SunWorld();
		var view = LookingDownX();
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null )]] );

		List<GpuCommand> Frame()
		{
			using var frame = HeadlessRenderHost.Headless.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			return frame.Commands.ToList();
		}

		static ViewPass[] Cascades( ReadOnlySpan<ViewPass> passes ) => passes.ToArray().Where( p => p.View.Name.StartsWith( "CSM Cascade" ) ).ToArray();
		static int Tiles( List<GpuCommand> commands ) => commands.Count( c => c.Op == GpuOp.Dispatch && c.Material?.Name == "shaders/borealis_shadow_tiles_cs.shader" );
		int IntoCascades( List<GpuCommand> commands )
		{
			var maps = Cascades( system.ShadowMaps.Passes ).Select( p => p.TargetMap.Texture ).ToHashSet();
			return commands.Count( c => (c.Op == GpuOp.Clear && maps.Contains( c.Depth )) || (c.Op == GpuOp.CopyTexture && maps.Contains( c.Texture )) );
		}

		var commands = Frame();
		var cascades = Cascades( system.ShadowMaps.Passes ).Length;
		Assert.IsTrue( cascades > 1 );
		Assert.AreEqual( cascades, Cascades( system.ShadowMaps.Drawn ).Length, "drawn the first time" );
		Assert.IsTrue( IntoCascades( commands ) > 0, "cleared and copied into" );
		Assert.IsTrue( Tiles( commands ) > 0, "with the far cascade's tiles" );
		var flags = system.ShadowMaps.Directional.Flags;

		commands = Frame();
		Assert.AreEqual( cascades, Cascades( system.ShadowMaps.Passes ).Length, "the views are chosen as before" );
		Assert.AreEqual( 0, Cascades( system.ShadowMaps.Drawn ).Length, "but none is drawn" );
		Assert.AreEqual( 0, IntoCascades( commands ), "no clear and no copy" );
		Assert.AreEqual( 0, Tiles( commands ), "and no tiles" );
		Assert.AreEqual( flags, system.ShadowMaps.Directional.Flags, "the same empty cascades and tiles" );

		// A caster that moves, in the nearest cascade
		var caster = new MeshObject( mesh, new Transform( new Vector3( 40, 0, 0 ) ) ) { LocalBounds = new BBox( new Vector3( -20 ), new Vector3( 20 ) ) };
		world.Add( caster );
		commands = Frame();
		Assert.IsTrue( Cascades( system.ShadowMaps.Drawn ).Any( p => p.View.Name == "CSM Cascade 0" ), "a caster draws the cascade" );
		Assert.IsTrue( Tiles( commands ) > 0, "and its tiles" );

		world.Remove( caster );
		Frame();
		Assert.IsTrue( Cascades( system.ShadowMaps.Drawn ).Any( p => p.View.Name == "CSM Cascade 0" ), "the frame after clears it again" );

		Frame();
		commands = Frame();
		Assert.AreEqual( 0, Cascades( system.ShadowMaps.Drawn ).Length, "then it's kept" );
		Assert.AreEqual( 0, Tiles( commands ) );
		Assert.AreEqual( flags, system.ShadowMaps.Directional.Flags );

		// A static caster changes the far cascade's cache, which is copied in again
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 100, 0, 0 ) ) ) { LocalBounds = new BBox( new Vector3( -20 ), new Vector3( 20 ) ), IsStatic = true } );
		commands = Frame();
		Assert.IsTrue( CacheViews( system ).Length > 0, "the cache drew it" );
		Assert.IsTrue( system.ShadowMaps.Drawn.ToArray().Contains( FarView( system ) ), "so the far cascade is copied again" );
		Assert.IsTrue( Tiles( commands ) > 0, "and its tiles made again" );
	}

	/// <summary>
	/// A caster's stamp comes from the settling clock's own count of frames, not the render host's: a caster added after other
	/// frames have settled hasn't, whatever host or world it's in (a render host's frame count starts again at 0).
	/// </summary>
	[TestMethod]
	public void ANewCasterHasNotSettled()
	{
		SettledCasters.Reset();
		var time = 1e6f;
		for ( int i = 0; i < 5; i++ ) SettledCasters.Tick( time += 10 );

		var world = SunWorld();
		var box = Box( world, Vector3.Zero, isStatic: false );
		Assert.IsFalse( SettledCasters.Settled( world.MovedStamps[box.Index] ) );
	}

	/// <summary>
	/// A caster settles after a short quiet; after moving twice once settled it's restless, and settles only after a long one,
	/// after which it starts over.
	/// </summary>
	[TestMethod]
	public void RestlessCastersSettleAfterALongerQuiet()
	{
		SettledCasters.Reset();
		var world = SunWorld();
		var box = Box( world, Vector3.Zero, isStatic: false );
		var time = 1e6f;
		bool Settled() => SettledCasters.Settled( world.MovedStamps[box.Index] );

		// Two frames a quiet: the move came after the first frame began
		void Quiet( float seconds )
		{
			SettledCasters.Tick( time += 0.01f );
			SettledCasters.Tick( time += seconds );
		}

		void Move() => box.Transform = new Transform( box.Transform.Position + Vector3.Up );

		Quiet( SettledCasters.Seconds + 1 );
		Assert.IsTrue( Settled(), "settled" );

		Move();
		Quiet( SettledCasters.Seconds + 1 );
		Assert.IsTrue( Settled(), "settled again after a move" );

		Move();
		Quiet( SettledCasters.Seconds + 1 );
		Assert.IsFalse( Settled(), "restless after its second move" );
		Quiet( SettledCasters.RestlessSeconds );
		Assert.IsTrue( Settled(), "settled after a long quiet" );

		Move();
		Quiet( SettledCasters.Seconds + 1 );
		Assert.IsTrue( Settled(), "and starts over" );
	}
}
