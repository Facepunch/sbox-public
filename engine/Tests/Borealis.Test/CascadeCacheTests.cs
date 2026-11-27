using Borealis.Gpu;
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

	static object LastCascadesMapper()
	{
		var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
		var last = typeof( ShadowMapper ).GetField( "LastCascades", flags ).GetValue( null );
		return last.GetType().GetField( "Mapper", flags ).GetValue( last );
	}

	/// <summary>
	/// The last cascade set is reused only within its frame: once that has passed it's let go of, with the mapper (and the
	/// render system and world) that drew it. A closed game's world stayed behind it at the menu, where nothing renders.
	/// </summary>
	[TestMethod]
	public void StaleCascadesAreLetGo()
	{
		var world = SunWorld();
		Box( world, new Vector3( 300, 0, 0 ), isStatic: true );
		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.IsNotNull( LastCascadesMapper(), "the frame's cascades are kept for other views in it" );

		ShadowMapper.ForgetStaleCascades();
		Assert.IsNotNull( LastCascadesMapper(), "kept within their frame" );

		RenderHost.Current.NextFrame();
		ShadowMapper.ForgetStaleCascades();
		Assert.IsNull( LastCascadesMapper(), "let go once it has passed" );
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
	/// A static caster whose material animates vertices is never in the far cascade's cache, so the view drawn over it takes
	/// it, however long ago it last moved: its tree query doesn't leave it out with the settled ones.
	/// </summary>
	[TestMethod]
	public void AStaticCasterThatAnimatesVerticesIsDrawnOverTheFarCache()
	{
		var world = SunWorld();
		using var animating = new AnimatingMaterial();
		var bounds = new BBox( new Vector3( -20 ), new Vector3( 20 ) );
		var sways = new MeshObject( new RenderMesh( new HeadlessMesh( "box" ), bounds, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, animating )]] ), new Transform( new Vector3( 300, 0, 0 ) ) ) { IsStatic = true };
		world.Add( sways );
		for ( int i = 0; i < 10; i++ ) Box( world, new Vector3( 100 + i * 50, 100, 0 ), isStatic: true );
		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		SettledCasters.Tick( 1e6f );
		SettledCasters.Reset( settled: true );
		CollectAndPrepare( system, world, view );
		var far = ShadowMapper.FarCaches.Single();
		far.DrawnAt -= SettledCasters.Seconds;
		CollectAndPrepare( system, world, view );
		CollectAndPrepare( system, world, view );

		Assert.IsTrue( sways.AnimatesVertices );
		Assert.IsFalse( CacheViews( system ).Any( p => p.Visible( mesh ).Contains( sways.Index ) ), "not in the cache" );
		Assert.IsTrue( FarView( system ).Visible( mesh ).Contains( sways.Index ), "drawn over it" );
	}

	sealed class AnimatingMaterial : GpuMaterial
	{
		public override string Name => "Animating";
		public override bool IsTranslucent => false;
		public override bool IsAlphaTest => false;
		public override bool ReadsFrameBuffer => false;
		public override bool ShadowFastPath => false;
		public override bool NeedsLightProbe => false;
		public override ulong SimilarityKey => 1;
		public override bool? AnimatesVertices => true;
		public override void Dispose() { }
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
			return commands.Count( c => (c.Op == GpuOp.Clear && maps.Contains( c.Depth )) || (c.Op == GpuOp.CopyTexture && maps.Contains( c.Texture ))
				|| (c.Op == GpuOp.DrawFullscreen && c.Material?.Name == "shaders/borealis_shadow_cache_copy.shader" && maps.Contains( c.Depth )) );
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
	/// Systems that share their cascade targets (a cube capture's faces) draw into the same maps, and one never keeps a cascade
	/// that another drew over since it last drew it.
	/// </summary>
	[TestMethod]
	public void SharedCascadesAreDrawnAgainAfterAnotherSystemDrewThem()
	{
		var empty = SunWorld();
		var busy = SunWorld();
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null )]] );
		busy.Add( new MeshObject( mesh, new Transform( new Vector3( 40, 0, 0 ) ) ) { LocalBounds = new BBox( new Vector3( -20 ), new Vector3( 20 ) ) } );
		var view = LookingDownX();
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var shared = new SharedShadowTargets();
		using var a = new RenderSystem();
		using var b = new RenderSystem();
		a.ShadowMaps.Shared = shared;
		b.ShadowMaps.Shared = shared;

		void Frame( RenderSystem system, RenderWorld world )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		static ViewPass Cascade0( RenderSystem system ) => system.ShadowMaps.Passes.ToArray().Single( p => p.View.Name == "CSM Cascade 0" );
		static bool DrewCascade0( RenderSystem system ) => system.ShadowMaps.Drawn.ToArray().Any( p => p.View.Name == "CSM Cascade 0" );

		Frame( a, empty );
		Frame( a, empty );
		Assert.IsFalse( DrewCascade0( a ), "kept while nothing else draws into it" );

		Frame( b, busy );
		Assert.AreSame( Cascade0( a ).TargetMap, Cascade0( b ).TargetMap, "one map for both" );
		Assert.IsTrue( DrewCascade0( b ), "the other system draws its caster into it" );

		Frame( a, empty );
		Assert.IsTrue( DrewCascade0( a ), "so it's drawn again, not kept with the other's caster in it" );

		Frame( a, empty );
		Assert.IsFalse( DrewCascade0( a ), "then kept" );
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

	/// <summary>
	/// A tree whose LODs 0 and 1 sway and LOD 2 doesn't. By the far cascade's texels, a placement of scale 1 takes LOD 2 and
	/// one of scale 2 takes LOD 0 (<paramref name="metric"/> is what its switch distances are measured against).
	/// </summary>
	static RenderMesh Tree( GpuMaterial sways, GpuMaterial still, float metric )
	{
		static RenderMesh.Draw Draw( GpuMaterial material ) => new( 0, 0, 0, 36, 24, 0, material );
		var bounds = new BBox( new Vector3( -50 ), new Vector3( 50 ) );
		return new RenderMesh( new HeadlessMesh( "tree" ), bounds, [[Draw( sways )], [Draw( sways )], [Draw( still )]], [0, metric * 0.6f, metric * 0.9f] );
	}

	/// <summary>
	/// Placements the views draw at each LOD of <see cref="Tree"/>.
	/// </summary>
	static int[] LodsDrawn( RenderSystem system, IEnumerable<ViewPass> passes )
	{
		var drawn = new int[3];
		foreach ( var pass in passes )
		{
			foreach ( var run in system.GetFeature<MeshRenderFeature>().RunsFor( pass ).Opaque ) drawn[run.Lod] += run.Count;
		}

		return drawn;
	}

	/// <summary>
	/// A world whose first frame has made the far cascade, and the metric its texels give LODs.
	/// </summary>
	static float FirstFrame( RenderSystem system, RenderWorld world, RenderView view )
	{
		Box( world, new Vector3( 100, 0, 0 ), isStatic: true );
		CollectAndPrepare( system, world, view );
		return 50.0f / FarView( system ).LodTexelsPerUnit;
	}

	/// <summary>
	/// A static caster that sways at LODs 0 and 1 and not at LOD 2 is held by the far cascade's cache at LOD 2 and drawn over
	/// it at LOD 0, by each placement's own texel LOD: the cache's parts draw the placement that takes LOD 2 and not the one
	/// that takes LOD 0, the view over it the reverse. Instances and plain meshes both.
	/// </summary>
	[TestMethod]
	public void FarCascadeHoldsTheLodsOfACasterThatDontAnimate()
	{
		var world = SunWorld();
		var view = LookingDownX();
		using var system = new RenderSystem();
		var metric = FirstFrame( system, world, view );

		using var sways = new ShadowCacheTests.AnimatingMaterial { Animates = true };
		using var still = new ShadowCacheTests.AnimatingMaterial { Animates = false };
		var tree = Tree( sways, still, metric );
		var instanced = new InstancedMeshObject { Mesh = tree, IsStatic = true };
		instanced.SetInstances( [new Transform( new Vector3( 100, -300, 0 ) ), new Transform( new Vector3( 100, 300, 0 ), Rotation.Identity, 2 )], default );
		world.Add( instanced );
		CollectAndPrepare( system, world, view );

		var cache = LodsDrawn( system, CacheViews( system ) );
		var live = LodsDrawn( system, [FarView( system )] );
		Assert.IsTrue( cache[2] > 0 && cache[0] == 0 && cache[1] == 0, $"the cache draws the LOD 2 placement only: {string.Join( ",", cache )}" );
		Assert.IsTrue( live[0] == 1 && live[1] == 0 && live[2] == 0, $"the view over it draws the LOD 0 placement only: {string.Join( ",", live )}" );

		// Plain meshes: LOD by the object's scale
		world.Remove( instanced );
		var small = new MeshObject( tree, new Transform( new Vector3( 100, -300, 0 ) ) ) { IsStatic = true };
		var large = new MeshObject( tree, new Transform( new Vector3( 100, 300, 0 ), Rotation.Identity, 2 ) ) { IsStatic = true };
		world.Add( small );
		world.Add( large );
		CollectAndPrepare( system, world, view );

		var mesh = system.GetFeature<MeshRenderFeature>();
		Assert.IsTrue( CacheViews( system ).Any( p => p.Visible( mesh ).Contains( small.Index ) ), "the cache holds the one at LOD 2" );
		Assert.IsTrue( FarView( system ).Visible( mesh ).Contains( small.Index ), "the view over it collects it too, and draws only the LODs that animate" );
		cache = LodsDrawn( system, CacheViews( system ) );
		live = LodsDrawn( system, [FarView( system )] );
		Assert.IsTrue( cache[2] > 0 && cache[0] == 0 && cache[1] == 0, $"meshes, the cache draws LOD 2 only: {string.Join( ",", cache )}" );
		Assert.IsTrue( live[0] == 1 && live[1] == 0 && live[2] == 0, $"meshes, the view over it draws LOD 0 only: {string.Join( ",", live )}" );
	}

	/// <summary>
	/// A material that wasn't known to animate and then does moves its LOD out of the cache, which is drawn again where the
	/// caster is, and into the view over it.
	/// </summary>
	[TestMethod]
	public void FarCascadeRedrawsWhenALodStartsToAnimate()
	{
		var world = SunWorld();
		var view = LookingDownX();
		using var system = new RenderSystem();
		var metric = FirstFrame( system, world, view );

		using var sways = new ShadowCacheTests.AnimatingMaterial { Animates = true };
		using var still = new ShadowCacheTests.AnimatingMaterial { Animates = null };
		var instanced = new InstancedMeshObject { Mesh = Tree( sways, still, metric ), IsStatic = true };
		instanced.SetInstances( [new Transform( new Vector3( 100, -300, 0 ) )], default );
		world.Add( instanced );
		CollectAndPrepare( system, world, view );
		CollectAndPrepare( system, world, view );
		var far = ShadowMapper.FarCaches.Single();
		var drawn = far.Draws + far.CellsDrawn;
		Assert.AreEqual( 0, CacheViews( system ).Length, "kept" );
		Assert.AreEqual( 0, LodsDrawn( system, [FarView( system )] )[2], "the material doesn't say yet: LOD 2 isn't drawn over the cache" );

		still.Animates = true;
		CollectAndPrepare( system, world, view );
		Assert.IsTrue( CacheViews( system ).Length > 0, "drawn again without it" );
		Assert.IsTrue( far.Draws + far.CellsDrawn > drawn );
		Assert.AreEqual( 0, LodsDrawn( system, CacheViews( system ) ).Sum(), "the cache holds none of it: every LOD animates" );
		Assert.AreEqual( 1, LodsDrawn( system, [FarView( system )] )[2], "the view over it draws LOD 2 now" );
	}

	/// <summary>
	/// What a resident list of the far cascade's cache makes of such a tree: its group leaves out the LODs that animate, with
	/// no device batch for them, and the view over the cache has no list and draws them itself.
	/// </summary>
	[TestMethod]
	public void ResidentFarCacheLeavesOutTheLodsThatAnimate()
	{
		var device = HeadlessRenderHost.Headless;
		(device.ResidentAggregates, device.ResidentMeshes) = (true, true);
		var wasWhole = ShadowMapper.FarCacheRedrawWhole;
		ShadowMapper.FarCacheRedrawWhole = true;
		try
		{
			var world = SunWorld();
			var view = LookingDownX();
			using var system = new RenderSystem { DepthPrepass = false };
			var metric = FirstFrame( system, world, view );

			using var sways = new ShadowCacheTests.AnimatingMaterial { Animates = true };
			using var still = new ShadowCacheTests.AnimatingMaterial { Animates = false };
			var instanced = new InstancedMeshObject { Mesh = Tree( sways, still, metric ), IsStatic = true };
			instanced.SetInstances( [new Transform( new Vector3( 100, -300, 0 ) ), new Transform( new Vector3( 100, 300, 0 ), Rotation.Identity, 2 )], default );
			world.Add( instanced );

			// Its run of transforms is made the frame after it's added
			for ( int i = 0; i < 3; i++ ) CollectAndPrepare( system, world, view );

			var aggregates = system.GetFeature<AggregateRenderFeature>();
			var list = CacheViews( system ).Select( aggregates.ResidentFor ).First( l => l is { MeshGroups.Count: 1 } );
			var group = list.MeshGroups.Single();
			Assert.AreEqual( 3UL, group.LiveLods, "a list that splits LODs leaves out LODs 0 and 1" );
			Assert.AreEqual( 3, list.DeviceCount, "a batch a LOD" );

			var batches = new GpuContext.GpuDrivenBatch[list.DeviceCount];
			Assert.IsTrue( aggregates.MeshDeviceBatches( list.MeshGroups, false, false, batches ) );
			Assert.IsTrue( batches[0].Material is null && batches[1].Material is null, "none for the LODs that animate" );
			Assert.IsTrue( batches[2].Material is not null && batches[2].Lod == 2, "one for the LOD that doesn't" );

			Assert.IsNull( aggregates.ResidentFor( FarView( system ) ), "the view over the cache has no list" );
			Assert.AreEqual( 1, LodsDrawn( system, [FarView( system )] )[0], "it draws the LOD that animates itself" );
			Assert.AreEqual( 0, LodsDrawn( system, [FarView( system )] )[2] );
		}
		finally
		{
			ShadowMapper.FarCacheRedrawWhole = wasWhole;
			(device.ResidentAggregates, device.ResidentMeshes) = (false, false);
		}
	}
}
