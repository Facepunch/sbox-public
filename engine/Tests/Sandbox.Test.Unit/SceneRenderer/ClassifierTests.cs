using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// The mesh feature's classifiers (<c>MeshRenderFeature.Lists.cs</c>): the one place that decides which of native's layers take
/// an object, from its own state and the view's kind. Pure functions, so every rule can be checked without a frame.
/// </summary>
[TestClass]
public class ClassifierTests
{
	static MeshObject Mesh() => new( TestMesh( false ), Transform.Zero );

	static MeshRuns InMain( MeshObject obj, MeshBlend blend = MeshBlend.Opaque ) => MeshRenderFeature.ListsFor( obj, blend, shadow: false );
	static MeshRuns Shadow( MeshObject obj, MeshBlend blend = MeshBlend.Opaque ) => MeshRenderFeature.ListsFor( obj, blend, shadow: true );

	/// <summary>
	/// Every list is defined once, at the bit layers name it by, and each sort, draw filter and fade is where native has it.
	/// </summary>
	[TestMethod]
	public void EveryListIsDefinedAtItsBit()
	{
		var lists = MeshRenderFeature.Lists;
		for ( int bit = 0; bit < lists.Length; bit++ )
			Assert.AreEqual( (MeshRuns)(1 << bit), lists[bit].Runs );

		Assert.AreEqual( MeshRuns.Faded, lists.Single( l => l.Fade ).Runs, "only the fade list draws with D_OPAQUE_FADE" );
		Assert.AreEqual( MeshRuns.Translucent | MeshRuns.OverlayTranslucent | MeshRuns.AfterUI, MeshRenderFeature.FrameBufferCopyingLists );
		Assert.AreEqual( MeshRenderFeature.DrawFilter.All, lists[4].Draws, "static overlays draw every draw" );
	}

	/// <summary>
	/// An ordinary opaque mesh: the world's opaque list, and the opaque shadow layer.
	/// </summary>
	[TestMethod]
	public void OpaqueMeshesDrawInTheWorldAndShadows()
	{
		Assert.AreEqual( MeshRuns.Opaque, InMain( Mesh() ) );
		Assert.AreEqual( MeshRuns.Opaque, Shadow( Mesh() ) );
		Assert.AreEqual( MeshRuns.OpaqueNoPrepass, InMain( new MeshObject( TestMesh( false ), Transform.Zero ) { DepthPrepass = false } ) );
	}

	/// <summary>
	/// A faded mesh goes to the fade layers, and drops out of every layer without one: the translucent shadow layer, the game
	/// overlay's, bloom and the refraction stencil.
	/// </summary>
	[TestMethod]
	public void FadedMeshesDropOutOfLayersWithoutAFadeLayer()
	{
		var faded = new MeshObject( TestMesh( false ), Transform.Zero ) { Tint = Color.White.WithAlpha( 0.5f ), Overlay = true, Bloom = true, WantsFrameBufferCopy = true };
		Assert.AreEqual( MeshRuns.Faded, InMain( faded ) );
		Assert.AreEqual( MeshRuns.Faded | MeshRuns.Translucent, InMain( faded, MeshBlend.Partial ) );
		Assert.AreEqual( MeshRuns.Faded, Shadow( faded, MeshBlend.Partial ), "no translucent shadow for a faded caster" );
		Assert.AreEqual( (MeshRuns)0, Shadow( faded, MeshBlend.Translucent ) );
	}

	/// <summary>
	/// The translucent shadow layer takes only objects with nothing opaque.
	/// </summary>
	[TestMethod]
	public void TranslucentShadowsAreOnlyForFullyTranslucentCasters()
	{
		Assert.AreEqual( MeshRuns.Translucent, Shadow( Mesh(), MeshBlend.Translucent ) );
		Assert.AreEqual( MeshRuns.Opaque, Shadow( Mesh(), MeshBlend.Partial ) );
		Assert.AreEqual( MeshRuns.Opaque | MeshRuns.Translucent, InMain( Mesh(), MeshBlend.Partial ) );
	}

	/// <summary>
	/// Out of the game layers, a mesh still casts shadows and draws in the layers that take it by flag alone.
	/// </summary>
	[TestMethod]
	public void OutOfTheGameLayersStillCastsAndBlooms()
	{
		var obj = new MeshObject( TestMesh( false ), Transform.Zero ) { GameLayers = false, Bloom = true, AfterUI = true, Decal = true };
		Assert.AreEqual( MeshRuns.Bloom | MeshRuns.AfterUI | MeshRuns.Decal, InMain( obj ) );
		Assert.AreEqual( MeshRuns.Opaque, Shadow( obj ) );
	}

	/// <summary>
	/// An overlay prepasses on its own and draws again over everything; its translucent draws go in both translucent layers.
	/// </summary>
	[TestMethod]
	public void OverlaysPrepassOnTheirOwnAndDrawAgain()
	{
		var overlay = new MeshObject( TestMesh( false ), Transform.Zero ) { Overlay = true };
		Assert.AreEqual( MeshRuns.OverlayPrepass | MeshRuns.OverlayOpaque, InMain( overlay ) );
		Assert.AreEqual( MeshRuns.Translucent | MeshRuns.OverlayTranslucent, InMain( overlay, MeshBlend.Translucent ) );
		Assert.AreEqual( MeshRuns.Opaque, Shadow( overlay ), "shadow views have no overlay layers" );
	}

	/// <summary>
	/// A matched mesh draws in its layer and no other - whatever its flags say, and in no shadow map, so it isn't even culled
	/// into one.
	/// </summary>
	[TestMethod]
	public void MatchedMeshesDrawOnlyInTheirLayer()
	{
		var feature = new MeshRenderFeature();
		static MeshObject Matched( LayerMatch match ) => new( TestMesh( false ), Transform.Zero ) { LayerMatch = match, Overlay = true, Bloom = true, AfterUI = true, Decal = true, WantsFrameBufferCopy = true };

		Assert.AreEqual( MeshRuns.StaticOverlay, InMain( Matched( LayerMatch.StaticOverlay ) ) );
		Assert.AreEqual( MeshRuns.OverlayWithDepth, InMain( Matched( LayerMatch.OverlayWithDepth ) ) );
		Assert.AreEqual( MeshRuns.OverlayWithoutDepth, InMain( Matched( LayerMatch.OverlayWithoutDepth ), MeshBlend.Translucent ) );
		Assert.AreEqual( (MeshRuns)0, InMain( Matched( LayerMatch.Unsupported ) ), "a layer this renderer lacks draws it nowhere" );

		foreach ( var match in new[] { LayerMatch.StaticOverlay, LayerMatch.OverlayWithDepth, LayerMatch.OverlayWithoutDepth, LayerMatch.Unsupported } )
		{
			Assert.AreEqual( (MeshRuns)0, Shadow( Matched( match ) ), $"{match}: no shadow layer" );
			Assert.IsFalse( feature.CastsShadow( Matched( match ) ), $"{match}: not a caster" );
		}

		Assert.IsTrue( feature.CastsShadow( Mesh() ) );
		Assert.AreEqual( (MeshRuns)0, InMain( new MeshObject( TestMesh( false ), Transform.Zero ) { LayerMatch = LayerMatch.StaticOverlay, GameLayers = false } ), "static overlays are game layers" );
	}

	/// <summary>
	/// A mesh flagged neither opaque nor translucent - a map's light blocker - draws nowhere; the refraction stencil takes what
	/// reads the frame buffer copy and is translucent.
	/// </summary>
	[TestMethod]
	public void FlagsDecideWhichDrawsAreTaken()
	{
		var blocker = new MeshObject( TestMesh( false ), Transform.Zero ) { DrawsOpaque = false, DrawsTranslucent = false };
		Assert.AreEqual( (MeshRuns)0, InMain( blocker, MeshBlend.Partial ) );
		Assert.AreEqual( (MeshRuns)0, Shadow( blocker, MeshBlend.Partial ) );

		var glass = new MeshObject( TestMesh( true ), Transform.Zero ) { WantsFrameBufferCopy = true };
		Assert.AreEqual( MeshRuns.Translucent | MeshRuns.Refraction, InMain( glass, MeshBlend.Translucent ) );
		Assert.AreEqual( MeshRuns.Opaque, InMain( glass, MeshBlend.Opaque ), "not while it's opaque" );
	}

	/// <summary>
	/// A custom object goes in the world's opaque or translucent list by <see cref="CustomObject.IsOpaque"/>, and nowhere while
	/// hidden or matched to a layer this renderer lacks.
	/// </summary>
	[TestMethod]
	public void CustomObjectsSortWithTheMeshes()
	{
		var custom = new TestCustomObject { IsOpaque = false, Overlay = true, Bloom = true };
		Assert.AreEqual( MeshRuns.Translucent | MeshRuns.OverlayTranslucent | MeshRuns.Bloom, MeshRenderFeature.ListsFor( custom, shadow: false ) );
		Assert.AreEqual( MeshRuns.Translucent, MeshRenderFeature.ListsFor( custom, shadow: true ) );

		custom.LayerMatch = LayerMatch.OverlayWithoutDepth;
		Assert.AreEqual( MeshRuns.OverlayWithoutDepth, MeshRenderFeature.ListsFor( custom, shadow: false ) );
		custom.LayerMatch = LayerMatch.Unsupported;
		Assert.AreEqual( (MeshRuns)0, MeshRenderFeature.ListsFor( custom, shadow: false ) );

		custom.LayerMatch = LayerMatch.None;
		custom.Visible = false;
		Assert.AreEqual( (MeshRuns)0, MeshRenderFeature.ListsFor( custom, shadow: false ) );
	}

	sealed class TestCustomObject : CustomObject
	{
		internal override void Render( Sandbox.SceneRenderer.Gpu.RenderContext context, ViewPass view, MeshLayer layer ) { }
	}
}
