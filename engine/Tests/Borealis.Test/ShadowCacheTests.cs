using Borealis;
using Borealis.Features;
using Borealis.Gpu;
using BorealisTests.Headless;
using System;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// When a light's cached shadow map, and its static cache, are kept and when they're drawn again.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class ShadowCacheTests
{
	[TestInitialize]
	public void ResetCache() => Borealis.Shadows.ShadowMapper.ResetCache();

	/// <summary>
	/// The views drawing a static cache this frame.
	/// </summary>
	static int StaticCacheViews( RenderSystem system ) => system.ShadowMaps.Passes.ToArray().Count( p => p.StaticFilter == StaticFilter.OnlyStatic );

	static MeshObject StaticBox( RenderWorld world, Vector3 position )
	{
		var box = new MeshObject( null, new Transform( position ) ) { LocalBounds = UnitBox, IsStatic = true };
		world.Add( box );
		return box;
	}

	/// <summary>
	/// A static light draws its static casters once, into a cache. Adding, removing, moving, hiding or changing a static caster
	/// within its reach draws the cache again; a change out of its reach doesn't.
	/// </summary>
	[TestMethod]
	public void StaticCacheRedrawsWhenAStaticCasterInReachChanges()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		var near = StaticBox( world, new Vector3( 100, 0, -20 ) );
		var far = StaticBox( world, new Vector3( 400, 0, -20 ) );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50, IsStatic = true } );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, StaticCacheViews( system ), "drawn once" );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => p.StaticFilter == StaticFilter.OnlyStatic && p.Visible( mesh ).Contains( near.Index ) ) );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, StaticCacheViews( system ), "then kept" );

		void Redraws( string change, Action act, bool expected = true )
		{
			act();
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( expected ? 6 : 0, StaticCacheViews( system ), change );
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( 0, StaticCacheViews( system ), $"kept after {change}" );
		}

		Redraws( "moved out of reach", () => far.Transform = new Transform( new Vector3( 420, 0, -20 ) ), expected: false );
		Redraws( "moved", () => near.Transform = new Transform( new Vector3( 100, 5, -20 ) ) );
		Redraws( "stopped casting", () => near.CastShadows = false );
		Redraws( "cast again", () => near.CastShadows = true );
		Redraws( "faded", () => near.Tint = Color.White.WithAlpha( 0.5f ) );
		Redraws( "hidden", () => near.Mesh = null );
		Redraws( "made dynamic", () => near.IsStatic = false );
		Redraws( "made static", () => near.IsStatic = true );
		Redraws( "removed", () => world.Remove( near ) );
		Redraws( "added", () => StaticBox( world, new Vector3( 100, 0, 20 ) ) );
		Redraws( "added out of reach", () => StaticBox( world, new Vector3( 400, 0, 20 ) ), expected: false );
	}

	/// <summary>
	/// A light baked after its static cache was drawn copies that cache no more: the lightmap holds its static casters, and
	/// its maps draw only the rest (the cache would shadow them twice).
	/// </summary>
	[TestMethod]
	public void ABakedLightCopiesNoStaticCache()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		StaticBox( world, new Vector3( 100, 0, -20 ) );
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50, IsStatic = true };
		world.Add( light );

		var view = LookingDownX();
		using var system = new RenderSystem();
		bool CopiesCache() => system.ShadowMaps.Passes.ToArray().Any( p => p.CachedStatic is not null );

		CollectAndPrepare( system, world, view );
		CollectAndPrepare( system, world, view );
		Assert.IsTrue( CopiesCache(), "a static light copies its cache" );

		// Baked with mixed shadows (its maps draw what moves), and a dynamic caster comes into its reach, so its maps are drawn again
		light.Baked = true;
		light.MixedShadows = true;
		world.Add( new MeshObject( null, new Transform( new Vector3( 100, 10, -20 ) ) ) { LocalBounds = UnitBox } );
		CollectAndPrepare( system, world, view );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Length > 0, "its maps are drawn" );
		Assert.IsFalse( CopiesCache(), "baked, it doesn't" );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => p.StaticFilter != StaticFilter.OnlyStatic && p.StaticFilter != StaticFilter.Any ), "its maps draw no static caster" );
	}

	/// <summary>
	/// A light nothing flags static draws its static casters into a cache once it has kept still as long as a caster takes to
	/// settle, and each update then draws only the casters that aren't static on top of a copy of it. Moving the light drops the
	/// cache, and everything is drawn again until it has kept still once more.
	/// </summary>
	[TestMethod]
	public void LightThatKeptStillCachesItsStaticCasters()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		var box = StaticBox( world, new Vector3( 100, 0, -20 ) );
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50 };
		world.Add( light );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();

		bool Cached() => system.ShadowMaps.Passes.ToArray().Where( p => p.StaticFilter != StaticFilter.OnlyStatic ).All( p => p.CachedStatic is not null );
		bool DrawsBox() => system.ShadowMaps.Passes.ToArray().Any( p => p.StaticFilter != StaticFilter.OnlyStatic && p.Visible( mesh ).Contains( box.Index ) );

		Borealis.Shadows.SettledCasters.Reset();
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, system.ShadowMaps.Passes.Length );
		Assert.AreEqual( 0, StaticCacheViews( system ), "new" );
		Assert.IsTrue( DrawsBox() );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, StaticCacheViews( system ), "not still for long" );
		Assert.IsTrue( DrawsBox() );

		// It has kept still long enough
		Borealis.Shadows.SettledCasters.Reset( settled: true );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, StaticCacheViews( system ), "its static casters, once" );
		Assert.IsTrue( Cached() );
		Assert.IsFalse( DrawsBox(), "left out of the map, which copies the cache" );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, StaticCacheViews( system ), "then kept" );
		Assert.AreEqual( 6, system.ShadowMaps.Passes.Length, "the map still updates" );
		Assert.IsTrue( Cached() );

		light.Transform = new Transform( new Vector3( 100, 5, 0 ) );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, StaticCacheViews( system ), "moved: no cache" );
		Assert.IsFalse( system.ShadowMaps.Passes.ToArray().Any( p => p.CachedStatic is not null ) );
		Assert.IsTrue( DrawsBox(), "everything drawn again" );
	}

	/// <summary>
	/// A static mesh whose material animates vertices (<see cref="GpuMaterial.AnimatesVertices"/>) isn't a static caster: a cached
	/// light draws it live, while a plain static mesh stays in its cache. A material that doesn't know yet (null) leaves it
	/// cached until it does.
	/// </summary>
	[TestMethod]
	public void AStaticMeshThatAnimatesVerticesIsDrawnLiveInACachedLight()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		using var animating = new AnimatingMaterial { Animates = null };
		using var plain = new AnimatingMaterial { Animates = false };
		RenderMesh Box( GpuMaterial material ) => new( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
		var sways = new MeshObject( Box( animating ), new Transform( new Vector3( 100, 0, -20 ) ) ) { IsStatic = true };
		var still = new MeshObject( Box( plain ), new Transform( new Vector3( 100, 20, -20 ) ) ) { IsStatic = true };
		world.Add( sways );
		world.Add( still );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 100, 0, 0 ) ) ) { Radius = 50 } );

		var view = LookingDownX();
		using var system = new RenderSystem();
		var feature = system.GetFeature<MeshRenderFeature>();
		bool Live( RenderObject obj ) => system.ShadowMaps.Passes.ToArray().Any( p => p.StaticFilter == StaticFilter.NoStatic && p.Visible( feature ).Contains( obj.Index ) );
		bool Cached( RenderObject obj ) => system.ShadowMaps.Passes.ToArray().Any( p => p.StaticFilter == StaticFilter.OnlyStatic && p.Visible( feature ).Contains( obj.Index ) );

		Borealis.Shadows.SettledCasters.Reset();
		CollectAndPrepare( system, world, view );
		CollectAndPrepare( system, world, view );
		Borealis.Shadows.SettledCasters.Reset( settled: true );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, StaticCacheViews( system ), "the light caches" );
		Assert.IsTrue( Cached( sways ) && !Live( sways ), "its material doesn't say yet: cached" );

		animating.Animates = true;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, StaticCacheViews( system ), "once it says so, the cache is drawn again without it" );
		Assert.IsTrue( Live( sways ) && !Cached( sways ), "its material animates: drawn live" );
		Assert.IsTrue( Cached( still ) && !Live( still ), "a plain static mesh stays cached" );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, StaticCacheViews( system ), "then the cache is kept" );
	}

	sealed class AnimatingMaterial : GpuMaterial
	{
		public bool? Animates { get; set; }
		public override string Name => "Animating";
		public override bool IsTranslucent => false;
		public override bool IsAlphaTest => false;
		public override bool ReadsFrameBuffer => false;
		public override bool ShadowFastPath => false;
		public override bool NeedsLightProbe => false;
		public override ulong SimilarityKey => 1;
		public override bool? AnimatesVertices => Animates;
		public override void Dispose() { }
	}

	/// <summary>
	/// Run <paramref name="test"/> with only new, moved or changed lights' shadows drawn: none waits its turn for an update.
	/// </summary>
	static void WithoutUpdates( Action test )
	{
		var updates = RenderHost.Current.Shadows.MaxUpdatesPerFrame;
		RenderHost.Current.Shadows.MaxUpdatesPerFrame = 0;
		try
		{
			test();
		}
		finally
		{
			RenderHost.Current.Shadows.MaxUpdatesPerFrame = updates;
		}
	}

	/// <summary>
	/// A child view, rendered and submitted in the middle of its parent's recording, can't use a map its parent drew this frame:
	/// the parent's commands reach the GPU after its own. It draws the map again, into the same texture at the same size, and
	/// once the parent's frame is submitted, views after it reuse the map again.
	/// </summary>
	[TestMethod]
	public void ChildViewRedrawsMapsItsParentHasntSubmitted() => WithoutUpdates( () =>
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( null, new Transform( new Vector3( 300, 0, -20 ) ) ) { LocalBounds = UnitBox } );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ) ) ) { Radius = 50 } );

		var view = LookingDownX();
		using var parent = new RenderSystem();
		using var child = new RenderSystem();
		using var later = new RenderSystem();

		CollectAndPrepare( parent, world, view );
		Assert.AreEqual( 6, parent.ShadowMaps.Passes.Length );
		var map = parent.ShadowMaps.Passes[0].TargetMap;

		// The same frame, the parent not submitted yet
		var stats = new RenderStats();
		child.Collect( world, view, ref stats );
		child.Prepare( world, view );
		Assert.AreEqual( 6, child.ShadowMaps.Passes.Length, "drawn again for the child" );
		Assert.AreSame( map, child.ShadowMaps.Passes[0].TargetMap, "into the parent's map" );
		child.ShadowMaps.Submitted();

		parent.ShadowMaps.Submitted();
		later.Collect( world, view, ref stats );
		later.Prepare( world, view );
		Assert.AreEqual( 0, later.ShadowMaps.Passes.Length, "submitted, it's reused" );
	} );

	/// <summary>
	/// A custom object that renders a child view of its world the first time it draws in the opaque layer, after the frame's
	/// shadow layers, as water renders its refraction from the translucent one.
	/// </summary>
	sealed class ChildViewCustom : CustomObject
	{
		public RenderSystem Child;
		public RenderView View;
		public ViewTarget Target;
		public int Rendered;

		public ChildViewCustom( Vector3 position )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			IsOpaque = true;
			CastShadows = false;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer )
		{
			if ( Rendered > 0 || layer is not OpaqueLayer ) return;
			Rendered++;
			Child.Render( World, View, Target );
		}
	}

	/// <summary>
	/// A child view rendered from the middle of its parent's recording (water's refraction) has the parent's frame submitted
	/// through its shadow layers first, so it uses the maps the parent drew this frame rather than drawing them again.
	/// A reflection doesn't: it keeps to its own.
	/// </summary>
	[TestMethod]
	public void ChildViewUsesTheMapsItsParentDrew() => WithoutUpdates( () =>
	{
		foreach ( var reflection in new[] { false, true } )
		{
			Borealis.Shadows.ShadowMapper.ResetCache();
			var world = new RenderWorld();
			world.Lighting.SunColor = Color.Black;
			for ( int i = 0; i < 200; i++ )
				world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 300 + i % 20, i / 20 - 5, -20 ) ) ) );
			world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ) ) ) { Radius = 50 } );

			using var target = ViewTarget.Create( new Vector2Int( 320, 240 ) );
			using var childTarget = ViewTarget.Create( new Vector2Int( 160, 120 ) );
			using var parent = new RenderSystem { ParallelRecording = true };
			using var child = new RenderSystem { ParallelRecording = true };
			var childView = LookingDownX();
			childView.Reflection = reflection;
			childView.ReflectionShadows = reflection;
			var custom = new ChildViewCustom( new Vector3( 280, 0, 0 ) ) { Child = child, View = childView, Target = childTarget };
			world.Add( custom );

			RenderHost.Current.NextFrame();
			parent.Render( world, LookingDownX(), target );

			Assert.AreEqual( 1, custom.Rendered, "the child rendered from the parent's recording" );
			Assert.AreEqual( 6, parent.ShadowMaps.Passes.Length, "the parent draws the point light's map" );
			if ( reflection ) Assert.AreEqual( 6, child.ShadowMaps.Passes.Length, "a reflection draws it again" );
			else Assert.AreEqual( 0, child.ShadowMaps.Passes.Length, "the child uses the parent's" );
		}
	} );

	/// <summary>
	/// A planar refraction rendered from the middle of its parent's recording takes the sun cascades its parent drew, though it
	/// leaves out a tag (the water surface) the parent draws and is smaller; a plain child view leaving the tag out draws its own.
	/// </summary>
	[TestMethod]
	public void RefractionUsesItsParentsCascades()
	{
		foreach ( var refraction in new[] { true, false } )
		{
			Borealis.Shadows.ShadowMapper.ResetCache();
			var world = new RenderWorld();
			for ( int i = 0; i < 200; i++ )
				world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 300 + i % 20, i / 20 - 5, -20 ) ) ) );

			using var target = ViewTarget.Create( new Vector2Int( 320, 240 ) );
			using var childTarget = ViewTarget.Create( new Vector2Int( 160, 120 ) );
			using var parent = new RenderSystem { ParallelRecording = true };
			using var child = new RenderSystem { ParallelRecording = true };

			var childView = LookingDownX();
			childView.Viewport = new Rect( 0, 0, 500, 500 );
			childView.ExcludeTags = [new StringToken( "water-surface" ).Value];
			if ( refraction )
			{
				var plane = new Plane( new Vector3( 0, 0, -10 ), Vector3.Down );
				var projection = Matrix.CreateProjection( childView.ZNear, childView.ZFar, childView.FieldOfView, 1 );
				childView.Projection = Matrix.CreateObliqueProjection( new Transform( childView.Position, childView.Rotation ), plane, projection );
				childView.ClipPlane = plane;
			}

			var custom = new ChildViewCustom( new Vector3( 280, 0, 0 ) ) { Child = child, View = childView, Target = childTarget };
			world.Add( custom );

			RenderHost.Current.NextFrame();
			parent.Render( world, LookingDownX(), target );

			Assert.AreEqual( 1, custom.Rendered );
			Assert.IsTrue( parent.ShadowMaps.Directional.CascadeCount > 0, "the parent draws cascades" );
			if ( refraction )
			{
				Assert.AreEqual( 0, child.ShadowMaps.Passes.Length, "the refraction draws none" );
				Assert.AreEqual( parent.ShadowMaps.Directional.CascadeCount, child.ShadowMaps.Directional.CascadeCount, "it takes the parent's" );
			}
			else
			{
				Assert.IsTrue( child.ShadowMaps.Passes.Length > 0, "a view leaving out what the parent drew draws its own" );
			}
		}
	}

	/// <summary>
	/// A light's radius, cone and kind make its projection, so changing one draws its shadow again at once, as moving it does,
	/// rather than keeping the old projection's map until its next update.
	/// </summary>
	[TestMethod]
	public void ChangingALightsShapeRedrawsItsShadow() => WithoutUpdates( () =>
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( null, new Transform( new Vector3( 300, 0, -20 ) ) ) { LocalBounds = UnitBox } );
		var light = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ), Rotation.LookAt( Vector3.Down ) ) ) { Radius = 50 };
		world.Add( light );

		var view = LookingDownX();
		using var system = new RenderSystem();

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, system.ShadowMaps.Passes.Length );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "the point light's map is kept" );

		light.Radius = 80;
		CollectAndPrepare( system, world, view );
		var passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 6, passes.Length, "a new radius" );
		Assert.IsTrue( passes.All( p => p.View.ZFar == 80 ) );

		light.Kind = LightObject.LightKind.Spot;
		CollectAndPrepare( system, world, view );
		passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 1, passes.Length, "a spot now" );
		Assert.IsFalse( passes[0].TargetMap.IsCube );

		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "the spot's map is kept" );

		light.ConeOuter = 30;
		CollectAndPrepare( system, world, view );
		passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 1, passes.Length, "a new cone" );
		Assert.AreEqual( 60, passes[0].View.FieldOfView );

		light.Color = Color.Red;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "colour doesn't change the shadow" );
	} );

	/// <summary>
	/// Throws while the main view's forward layer records, as a frame that fails part way does.
	/// </summary>
	sealed class FailingCallbacks : DrawCallbacks
	{
		public bool Fail;

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer, bool after )
		{
			if ( Fail ) throw new InvalidOperationException( "the frame failed" );
		}
	}

	/// <summary>
	/// A frame that fails before it's submitted draws nothing, so the maps it would have drawn, and a static cache it would
	/// have filled, aren't kept: the next frame draws them.
	/// </summary>
	[TestMethod]
	public void AFrameThatIsNeverSubmittedLeavesItsShadowsUndrawn() => WithoutUpdates( () =>
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );

		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 300, 0, -20 ) ) ) { IsStatic = true } );
		var callbacks = new FailingCallbacks { Before = true, Fail = true };
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 300, 0, 20 ) ) ) { Callbacks = callbacks } );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ) ) ) { Radius = 50, IsStatic = true } );

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem();

		void Render()
		{
			using var frame = HeadlessRenderHost.Headless.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
		}

		Assert.ThrowsException<InvalidOperationException>( Render );
		Assert.AreEqual( 6, StaticCacheViews( system ), "the failed frame would have filled the static cache" );

		callbacks.Fail = false;
		Render();
		Assert.AreEqual( 12, system.ShadowMaps.Passes.Length, "so the next one fills it, and draws the map" );
		Assert.AreEqual( 6, StaticCacheViews( system ) );

		Render();
		Assert.AreEqual( 0, system.ShadowMaps.Passes.Length, "a submitted frame's are kept" );
	} );

	/// <summary>
	/// A camera's tags filter its shadows' casters, as native's shadow views inherit their camera's. A light seen by two
	/// cameras with different tags keeps a map for each, static cache and all, rather than whichever drew first.
	/// </summary>
	[TestMethod]
	public void CamerasWithOtherTagsKeepTheirOwnShadows() => WithoutUpdates( () =>
	{
		var viewer = new StringToken( "viewer" ).Value;
		var ui = new StringToken( "ui" ).Value;
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		var body = new MeshObject( null, new Transform( new Vector3( 300, 0, -20 ) ) ) { LocalBounds = UnitBox, Tags = [viewer] };
		var statue = StaticBox( world, new Vector3( 300, 0, 20 ) );
		statue.Tags = [viewer];
		world.Add( body );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ) ) ) { Radius = 50, IsStatic = true } );

		var firstPerson = LookingDownX();
		firstPerson.ExcludeTags = [viewer, ui];
		var spectator = LookingDownX();

		using var first = new RenderSystem();
		using var second = new RenderSystem();
		var mesh = first.GetFeature<MeshRenderFeature>();

		static int[] Drawn( RenderSystem system, MeshRenderFeature mesh ) => system.ShadowMaps.Passes.ToArray().SelectMany( p => p.Visible( mesh ) ).ToArray();

		// Both cameras in one frame
		RenderHost.Current.NextFrame();
		Prepare( first, world, firstPerson );
		Assert.AreEqual( 12, first.ShadowMaps.Passes.Length, "the cube and its static cache" );
		CollectionAssert.DoesNotContain( Drawn( first, mesh ), body.Index );
		CollectionAssert.DoesNotContain( Drawn( first, mesh ), statue.Index );

		Prepare( second, world, spectator );
		Assert.AreEqual( 12, second.ShadowMaps.Passes.Length, "the spectator's own cube and static cache" );
		CollectionAssert.Contains( Drawn( second, mesh ), body.Index );
		CollectionAssert.Contains( Drawn( second, mesh ), statue.Index );

		// Then each keeps its own
		RenderHost.Current.NextFrame();
		Prepare( first, world, firstPerson );
		Prepare( second, world, spectator );
		Assert.AreEqual( 0, first.ShadowMaps.Passes.Length );
		Assert.AreEqual( 0, second.ShadowMaps.Passes.Length );

		// The same tags in another order are the same filter
		var again = LookingDownX();
		again.ExcludeTags = [ui, viewer];
		RenderHost.Current.NextFrame();
		Prepare( first, world, again );
		Assert.AreEqual( 0, first.ShadowMaps.Passes.Length );
	} );

	/// <summary>
	/// A static cache is kept while the camera moves, so it keeps casters too small to see from where the camera was when it
	/// was drawn. Maps that are drawn again as they update still size cull from the camera.
	/// </summary>
	[TestMethod]
	public void StaticCachesKeepSmallCasters()
	{
		var world = new RenderWorld();
		world.Lighting.SunColor = Color.Black;
		var small = new BBox( new Vector3( -0.1f ), new Vector3( 0.1f ) );
		var pebble = new MeshObject( null, new Transform( new Vector3( 900, 0, -20 ) ) ) { LocalBounds = small, IsStatic = true };
		var marble = new MeshObject( null, new Transform( new Vector3( 900, 0, 20 ) ) ) { LocalBounds = small };
		world.Add( pebble );
		world.Add( marble );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 900, 0, 0 ) ) ) { Radius = 50, IsStatic = true } );

		using var system = new RenderSystem();
		var mesh = system.GetFeature<MeshRenderFeature>();
		CollectAndPrepare( system, world, LookingDownX() );

		var passes = system.ShadowMaps.Passes.ToArray();
		Assert.AreEqual( 12, passes.Length );
		CollectionAssert.Contains( passes.Where( p => p.StaticFilter == StaticFilter.OnlyStatic ).SelectMany( p => p.Visible( mesh ) ).ToArray(), pebble.Index );
		CollectionAssert.DoesNotContain( passes.SelectMany( p => p.Visible( mesh ) ).ToArray(), marble.Index, "too small from here" );
	}

	/// <summary>
	/// A second camera in the same frame reuses the first's sun cascades only if it shows the same casters.
	/// </summary>
	[TestMethod]
	public void CascadesAreSharedOnlyWithTheSameTags()
	{
		var viewer = new StringToken( "viewer" ).Value;
		var world = new RenderWorld();
		var body = new MeshObject( null, new Transform( new Vector3( 10, 0, 0 ) ) ) { LocalBounds = UnitBox, Tags = [viewer] };
		world.Add( body );

		var firstPerson = LookingDownX();
		firstPerson.ExcludeTags = [viewer];

		using var first = new RenderSystem();
		using var second = new RenderSystem();
		using var third = new RenderSystem();
		var mesh = first.GetFeature<MeshRenderFeature>();

		RenderHost.Current.NextFrame();
		Prepare( first, world, firstPerson );
		Assert.AreNotEqual( 0, first.ShadowMaps.Passes.Length );

		Prepare( second, world, LookingDownX() );
		Assert.AreEqual( first.ShadowMaps.Passes.Length, second.ShadowMaps.Passes.Length, "its own cascades" );
		CollectionAssert.Contains( second.ShadowMaps.Passes.ToArray().SelectMany( p => p.Visible( mesh ) ).ToArray(), body.Index );

		Prepare( third, world, LookingDownX() );
		Assert.AreEqual( 0, third.ShadowMaps.Passes.Length, "the same tags share the last cascades" );
	}

	/// <summary>
	/// A camera's sub-worlds (another world drawn in its view, like gizmos) cast its shadows too, so a light seen by a camera
	/// that draws one and a camera that doesn't keeps a map for each, and their cascades aren't shared.
	/// </summary>
	[TestMethod]
	public void CamerasWithOtherSubWorldsKeepTheirOwnShadows() => WithoutUpdates( () =>
	{
		var world = new RenderWorld();
		var gizmo = new MeshObject( null, new Transform( new Vector3( 300, 0, -20 ) ) ) { LocalBounds = UnitBox, SubWorld = 1 };
		var statue = StaticBox( world, new Vector3( 300, 0, 20 ) );
		statue.SubWorld = 1;
		world.Add( gizmo );
		world.Add( new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 300, 0, 0 ) ) ) { Radius = 50, IsStatic = true } );

		var game = LookingDownX();
		var editor = LookingDownX();
		editor.SubWorlds = 1;

		using var first = new RenderSystem();
		using var second = new RenderSystem();
		using var third = new RenderSystem();
		var mesh = first.GetFeature<MeshRenderFeature>();

		static int[] Drawn( RenderSystem system, MeshRenderFeature mesh ) => system.ShadowMaps.Passes.ToArray().SelectMany( p => p.Visible( mesh ) ).ToArray();

		RenderHost.Current.NextFrame();
		Prepare( first, world, game );
		CollectionAssert.DoesNotContain( Drawn( first, mesh ), gizmo.Index );
		CollectionAssert.DoesNotContain( Drawn( first, mesh ), statue.Index );

		Prepare( second, world, editor );
		CollectionAssert.Contains( Drawn( second, mesh ), gizmo.Index, "its own cube, or cascades" );
		CollectionAssert.Contains( Drawn( second, mesh ), statue.Index, "and its own static cache" );

		// The same sub-worlds share the light's maps and the frame's cascades
		var again = LookingDownX();
		again.SubWorlds = 1;
		Prepare( third, world, again );
		Assert.AreEqual( 0, third.ShadowMaps.Passes.Length );
	} );

	/// <summary>
	/// A top-level view's frame in the frame under way: prepared, and submitted before the next view renders.
	/// </summary>
	static void Prepare( RenderSystem system, RenderWorld world, RenderView view )
	{
		var stats = new RenderStats();
		system.Collect( world, view, ref stats );
		system.Prepare( world, view );
		system.ShadowMaps.Submitted();
	}
}
