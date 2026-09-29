using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System.Linq;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Which of the frame's layers an object draws in: the world's, the game overlay's, or only shadow maps - as native's
/// layers take objects by their flags.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class LayerTests
{
	static MeshRenderFeature.RunLists MainRuns( RenderSystem system ) => system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass );

	/// <summary>
	/// An overlay draws where it is, in the world's layers, and again in the overlay's, as native's does.
	/// </summary>
	[TestMethod]
	public void OverlayDrawsInTheWorldAndOverIt()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Overlay = true } );
		world.Add( new MeshObject( TestMesh( true ), new Transform( new Vector3( 30, 0, 0 ) ) ) { Overlay = true } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 1, runs.OverlayPrepass.Count, "the opaque one in the world, prepassed on its own" );
		Assert.AreEqual( 1, runs.Translucent.Count, "the translucent one in the world" );
		Assert.AreEqual( 1, runs.OverlayOpaque.Count, "the opaque one over it" );
		Assert.AreEqual( 1, runs.OverlayTranslucent.Count, "the translucent one over it" );

		var layers = system.Layers;
		Assert.IsTrue( layers.OfType<GameOverlayLayer>().All( l => l.IsNeeded( system.Frame ) ), "both overlay layers draw" );
	}

	/// <summary>
	/// An overlay in the prepass prepasses on its own, first, as native's overlay prepass does, and draws with the world in
	/// forward; the world's prepass then tests the overlay stencil bit.
	/// </summary>
	[TestMethod]
	public void OverlayPrepassesFirst()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Overlay = true } );
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 1, runs.OverlayPrepass.Count, "the overlay in its own prepass" );
		Assert.AreEqual( 1, runs.Opaque.Count, "the world's prepass without it" );

		var view = system.MainPass;
		var overlayPrepass = system.Layers.OfType<OverlayDepthPrepassLayer>().Single();
		var opaque = system.Layers.OfType<OpaqueLayer>().Single();
		Assert.IsTrue( overlayPrepass.IsNeeded( system.Frame ) );
		Assert.AreEqual( 2, system.RunCount( view, opaque ), "both in forward" );
		var worldPrepass = system.Layers.OfType<DepthPrepassLayer>().Single( l => l is not OverlayDepthPrepassLayer );
		Assert.IsTrue( System.Array.IndexOf( system.Layers, overlayPrepass ) < System.Array.IndexOf<RenderLayer>( system.Layers, worldPrepass ), "overlays first" );
	}

	/// <summary>
	/// Native's overlay layers have no fade layer, so a faded overlay only draws in the world's.
	/// </summary>
	[TestMethod]
	public void FadedOverlayOnlyDrawsInTheWorld()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Overlay = true, Tint = Color.White.WithAlpha( 0.5f ) } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 1, runs.Faded.Count );
		Assert.AreEqual( 0, runs.OverlayOpaque.Count );
		Assert.IsFalse( system.Layers.OfType<GameOverlayLayer>().Any( l => l.IsNeeded( system.Frame ) ), "no overlay layer draws" );
	}

	/// <summary>
	/// An object that's neither opaque nor translucent to native - a map's light blocker - draws nowhere, shadows included:
	/// native's shadow layers need one or the other too.
	/// </summary>
	[TestMethod]
	public void LightBlockerDrawsNowhere()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { DrawsOpaque = false, DrawsTranslucent = false } );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var feature = system.GetFeature<MeshRenderFeature>();
		foreach ( var pass in system.ShadowMaps.Passes.ToArray().Append( system.MainPass ) )
			Assert.AreEqual( 0, feature.RunsFor( pass ).ByBit.Sum( l => l.Count ), "no runs in any view" );
	}

	/// <summary>
	/// An object kept out of the prepass still draws in the opaque layer, and casts.
	/// </summary>
	[TestMethod]
	public void NoPrepassObjectDrawsInForwardOnly()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { DepthPrepass = false } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 0, runs.Opaque.Count, "not in the list the prepass draws" );
		Assert.AreEqual( 1, runs.OpaqueNoPrepass.Count );

		var view = system.MainPass;
		var prepass = system.Layers.OfType<DepthPrepassLayer>().Single( l => l is not OverlayDepthPrepassLayer );
		var opaque = system.Layers.OfType<OpaqueLayer>().Single();
		Assert.AreEqual( 0, system.RunCount( view, prepass ) );
		Assert.AreEqual( 1, system.RunCount( view, opaque ) );
	}

	/// <summary>
	/// Static overlays draw only in their own layer, lowest render order first, and in no prepass or shadow map.
	/// </summary>
	[TestMethod]
	public void StaticOverlaysDrawInRenderOrder()
	{
		var world = new RenderWorld();
		var mesh = TestMesh( false );
		var late = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = 5 };
		var early = new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) ) { LayerMatch = LayerMatch.StaticOverlay, RenderOrder = -2 };
		world.Add( late );
		world.Add( early );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 2, runs.StaticOverlay.Count );
		Assert.AreEqual( 0, runs.Opaque.Count + runs.OpaqueNoPrepass.Count + runs.Faded.Count + runs.Translucent.Count, "in no other list" );
		Assert.AreSame( early.Mesh, runs.StaticOverlay[0].Mesh, "the lower render order first" );
		Assert.AreSame( late.Mesh, runs.StaticOverlay[1].Mesh );

		var feature = system.GetFeature<MeshRenderFeature>();
		foreach ( var pass in system.ShadowMaps.Passes )
			Assert.AreEqual( 0, feature.RunsFor( pass ).ByBit.Sum( l => l.Count ), "no shadows" );
	}

	/// <summary>
	/// An object left out of the game layers draws in none of the main view's, but still casts, as native's shadow views
	/// don't filter by it.
	/// </summary>
	[TestMethod]
	public void ObjectOutOfTheGameLayersOnlyCasts()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { GameLayers = false } );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 0, runs.Opaque.Count + runs.Faded.Count + runs.Translucent.Count + runs.OverlayOpaque.Count, "nothing in the main view" );

		var feature = system.GetFeature<MeshRenderFeature>();
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => feature.RunsFor( p ).Opaque.Count > 0 ), "a cascade drew it" );
	}

	/// <summary>
	/// A bloom object out of the game layers draws only in the bloom layer, all of its draws whatever they blend, and a bloom
	/// object in the game layers draws in both - as native's bloom layer takes objects by its flag alone.
	/// </summary>
	[TestMethod]
	public void BloomObjectsDrawInTheBloomLayer()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { GameLayers = false, Bloom = true } );
		world.Add( new MeshObject( TestMesh( true ), new Transform( new Vector3( 30, 0, 0 ) ) ) { GameLayers = false, Bloom = true } );
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) { Bloom = true } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 3, runs.Bloom.Sum( r => r.Count ), "all three in the bloom layer" );
		Assert.IsTrue( runs.Bloom.All( r => r.Filter == MeshRenderFeature.DrawFilter.All ), "every draw of each" );
		Assert.AreEqual( 1, runs.Opaque.Sum( r => r.Count ), "only the one in the game layers in the world" );
		Assert.AreEqual( 0, runs.Translucent.Count, "the translucent one only glows" );

		var bloom = system.Layers.OfType<BloomLayer>().Single();
		Assert.AreEqual( runs.Bloom.Count, system.RunCount( system.MainPass, bloom ) );
		Assert.IsTrue( bloom.IsNeeded( system.Frame ), "it has objects to draw" );
		Assert.IsFalse( Plans<BloomLayer>( system ), "but nothing reads it without a camera's stages, whose bloom would" );
	}

	/// <summary>
	/// Whether the main frame's plan schedules a layer of this type (<see cref="FrameRecorder"/>).
	/// </summary>
	static bool Plans<T>( RenderSystem system ) where T : RenderLayer
	{
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: false );
		return recorder.Planned.Any( p => p.Layer is T );
	}

	/// <summary>
	/// A layer that makes something is left out of the plan when nothing after it reads it - as Unity's render graph culls
	/// passes - and kept once something does: the depth chain, with no camera stages, until glass copies the frame.
	/// </summary>
	[TestMethod]
	public void ProducersRunOnlyWhenRead()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( true ), new Transform( new Vector3( 20, 0, 0 ) ) ) );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var chain = system.Layers.OfType<DepthChainLayer>().Single();
		Assert.IsTrue( chain.IsNeeded( system.Frame ), "there's a prepass to build it from" );
		Assert.IsFalse( Plans<DepthChainLayer>( system ), "but nothing reads it" );

		world.Add( new MeshObject( TestMesh( true ), new Transform( new Vector3( 30, 0, 0 ) ) ) { WantsFrameBufferCopy = true } );
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.IsTrue( Plans<DepthChainLayer>( system ), "glass's frame copy reads it" );
		Assert.IsTrue( Plans<RefractionStencilLayer>( system ), "and glass's stencil" );
	}

	/// <summary>
	/// Native's bloom layer has no fade layer, so a faded bloom object drops out of it.
	/// </summary>
	[TestMethod]
	public void FadedBloomObjectDoesntGlow()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Bloom = true, Tint = Color.White.WithAlpha( 0.5f ) } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 1, runs.Faded.Count );
		Assert.AreEqual( 0, runs.Bloom.Count );
	}

	/// <summary>
	/// After-UI objects draw again over the UI, faded ones too since native's layer is its own fade layer, nearest first; one out
	/// of the game layers draws nowhere else.
	/// </summary>
	[TestMethod]
	public void AfterUIObjectsDrawOverTheUI()
	{
		var world = new RenderWorld();
		var far = new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) { AfterUI = true };
		var near = new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { AfterUI = true, GameLayers = false, Tint = Color.White.WithAlpha( 0.5f ) };
		world.Add( far );
		world.Add( near );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 2, runs.AfterUI.Count, "both, the faded one too" );
		Assert.AreSame( near.Mesh, runs.AfterUI[0].Mesh, "nearest first" );
		Assert.AreEqual( 1, runs.Opaque.Count + runs.Faded.Count, "only the one in the game layers in the world" );

		var layer = system.Layers.OfType<AfterUILayer>().Single();
		Assert.IsTrue( layer.IsNeeded( system.Frame ) );
	}

	/// <summary>
	/// Glass - translucent and reading the frame buffer copy - goes into the refraction stencil as well as the translucent
	/// layer, which then copies the frame for it. A faded one still draws, and still has the frame copied, but drops out of
	/// the stencil, which has no fade layer; an opaque object reading the copy isn't in the stencil at all.
	/// </summary>
	[TestMethod]
	public void GlassGoesIntoTheRefractionStencil()
	{
		var world = new RenderWorld();
		var glass = new MeshObject( TestMesh( true ), new Transform( new Vector3( 20, 0, 0 ) ) ) { WantsFrameBufferCopy = true };
		world.Add( glass );
		world.Add( new MeshObject( TestMesh( true ), new Transform( new Vector3( 30, 0, 0 ) ) ) { WantsFrameBufferCopy = true, Tint = Color.White.WithAlpha( 0.5f ) } );
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) { WantsFrameBufferCopy = true } );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 1, runs.Refraction.Count, "only the unfaded translucent one" );
		Assert.AreSame( glass.Mesh, runs.Refraction[0].Mesh );
		Assert.AreEqual( 2, runs.Translucent.Count );
		Assert.IsTrue( runs.Translucent.All( r => r.ReadsFrameBuffer ), "both translucent runs copy the frame first" );
		Assert.AreEqual( MeshRuns.Translucent, runs.FrameBufferReads, "only the translucent layer copies" );
		Assert.IsTrue( Plans<RefractionStencilLayer>( system ), "the copy reads its stencil" );
	}

	/// <summary>
	/// Decal geometry - flagged neither opaque nor translucent, as a map's is - draws only in the decal layer, faded or not,
	/// nearest first, and casts no shadow; an object flagged a decal and opaque draws in both.
	/// </summary>
	[TestMethod]
	public void DecalGeometryDrawsInTheDecalLayer()
	{
		var world = new RenderWorld();
		var far = new MeshObject( TestMesh( false ), new Transform( new Vector3( 40, 0, 0 ) ) ) { Decal = true, DrawsOpaque = false, DrawsTranslucent = false, CastShadows = false, Tint = Color.White.WithAlpha( 0.5f ) };
		var near = new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { Decal = true };
		world.Add( far );
		world.Add( near );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 2, runs.Decal.Count, "both, the faded one too" );
		Assert.AreSame( near.Mesh, runs.Decal[0].Mesh, "nearest first" );
		Assert.AreEqual( 1, runs.Opaque.Count, "the one also flagged opaque in the world" );
		Assert.IsTrue( system.Layers.OfType<DecalLayer>().Single().IsNeeded( system.Frame ) );
	}

	/// <summary>
	/// A frame with nothing reading the copy has no refraction stencil and copies nothing.
	/// </summary>
	[TestMethod]
	public void NoGlassNoRefractionStencil()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( true ), new Transform( new Vector3( 20, 0, 0 ) ) ) );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		Assert.AreEqual( (MeshRuns)0, MainRuns( system ).FrameBufferReads );
		Assert.IsFalse( Plans<RefractionStencilLayer>( system ), "nothing reads its stencil" );
	}

	/// <summary>
	/// Objects matched to an overlay layer after post processing draw only in it, translucent ones farthest first, and cast no
	/// shadow.
	/// </summary>
	[TestMethod]
	public void ScreenOverlayObjectsDrawOnlyThere()
	{
		var world = new RenderWorld();
		var opaque = new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) { LayerMatch = LayerMatch.OverlayWithDepth };
		var nearGlass = new MeshObject( TestMesh( true ), new Transform( new Vector3( 30, 0, 0 ) ) ) { LayerMatch = LayerMatch.OverlayWithDepth };
		var farGlass = new MeshObject( TestMesh( true ), new Transform( new Vector3( 50, 0, 0 ) ) ) { LayerMatch = LayerMatch.OverlayWithDepth };
		world.Add( nearGlass );
		world.Add( opaque );
		world.Add( farGlass );
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 25, 5, 0 ) ) ) { LayerMatch = LayerMatch.OverlayWithoutDepth } );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var runs = MainRuns( system );
		Assert.AreEqual( 3, runs.OverlayWithDepth.Count );
		Assert.AreSame( opaque.Mesh, runs.OverlayWithDepth[0].Mesh, "the opaque one first" );
		Assert.AreSame( farGlass.Mesh, runs.OverlayWithDepth[1].Mesh, "then the translucent ones, farthest first" );
		Assert.AreSame( nearGlass.Mesh, runs.OverlayWithDepth[2].Mesh );
		Assert.AreEqual( 1, runs.OverlayWithoutDepth.Count );
		Assert.AreEqual( 0, runs.Opaque.Count + runs.Faded.Count + runs.Translucent.Count, "in no world layer" );

		var feature = system.GetFeature<MeshRenderFeature>();
		foreach ( var pass in system.ShadowMaps.Passes )
			Assert.AreEqual( 0, feature.RunsFor( pass ).ByBit.Sum( l => l.Count ), "no shadows" );
	}
}
