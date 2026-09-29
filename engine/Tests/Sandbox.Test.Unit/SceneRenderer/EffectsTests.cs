using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using Sandbox.SceneRenderer.Gpu;
using System.Linq;
using System.Runtime.InteropServices;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// Custom objects (sprites, particles, world panels) sorting with the meshes, and decals binned in their sort order.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class EffectsTests
{
	/// <summary>
	/// A custom object that draws nothing: preparing a frame never calls it.
	/// </summary>
	internal sealed class TestCustom : CustomObject
	{
		public TestCustom( Vector3 position, float size = 1 )
		{
			LocalBounds = new BBox( new Vector3( -size ), new Vector3( size ) );
			Transform = new Transform( position );
		}

		internal override void Render( RenderContext context, ViewPass view, MeshLayer layer ) { }
	}

	[TestMethod]
	public void CustomObjectsSortWithTranslucentMeshes()
	{
		var world = new RenderWorld();
		var translucent = TestMesh( true );
		var near = new MeshObject( translucent, new Transform( new Vector3( 20, 0, 0 ) ) );
		var far = new MeshObject( translucent, new Transform( new Vector3( 60, 0, 0 ) ) );
		var custom = new TestCustom( new Vector3( 40, 0, 0 ) );
		var opaque = new TestCustom( new Vector3( 30, 5, 0 ) ) { IsOpaque = true };
		var hidden = new TestCustom( new Vector3( 50, 0, 0 ) ) { Visible = false };
		world.Add( near );
		world.Add( far );
		world.Add( custom );
		world.Add( opaque );
		world.Add( hidden );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var layers = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass );

		// Back to front, the custom object between the meshes; the hidden one nowhere
		Assert.AreEqual( 3, layers.Translucent.Count );
		Assert.IsNull( layers.Translucent[0].Custom );
		Assert.AreSame( custom, layers.Translucent[1].Custom );
		Assert.IsNull( layers.Translucent[2].Custom );

		// The opaque one in the opaque layer, alone in its run
		Assert.AreEqual( 1, layers.Opaque.Count );
		Assert.AreSame( opaque, layers.Opaque[0].Custom );
		Assert.AreEqual( 1, layers.Opaque[0].Count );

		Assert.IsFalse( layers.Opaque.Concat( layers.Faded ).Concat( layers.Translucent ).Any( r => r.Custom == hidden ) );
	}

	[TestMethod]
	public void CustomObjectsAreNeverSizeCulled()
	{
		// A hundredth of a unit across at 900 units: far under the size cull threshold, but still drawn
		var world = new RenderWorld();
		var tiny = new TestCustom( new Vector3( 900, 0, 0 ), 0.01f );
		world.Add( tiny );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );

		var layers = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass );
		Assert.AreEqual( 1, layers.Translucent.Count );
		Assert.AreSame( tiny, layers.Translucent[0].Custom );
	}

	[TestMethod]
	public void DecalsBinInSortOrder()
	{
		var world = new RenderWorld();
		DecalObject Add( float x, uint sortOrder, bool visible = true )
		{
			var decal = new DecalObject( new Transform( new Vector3( x, 0, 0 ), Rotation.Identity, 10 ) ) { SortOrder = sortOrder, Visible = visible };
			world.Add( decal );
			return decal;
		}

		var third = Add( 20, 3 );
		var first = Add( 40, 1 );
		var second = Add( 60, 2 );
		var fourth = Add( 80, 7 );
		Add( 100, 0, visible: false );
		Add( -100, 0 ); // behind the view

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var binner = system.GetFeature<LightBinnerFeature>();

		// By sort order, the hidden decal and the one behind the view left out. Ties keep the visible list's order, which is
		// the tree's; the Decal component makes every sort order unique
		Assert.AreEqual( 4, binner.DecalCount );
		CollectionAssert.AreEqual( new[] { first, second, third, fourth }, binner.Decals.ToArray() );

		// Decals aren't lights or probes
		Assert.AreEqual( 0, binner.Count );
	}

	[TestMethod]
	public void DecalLayoutMatchesNative()
	{
		// GPUDecal (lightbinner_standard.h), as Decals.hlsl reads it
		LightBinnerFeature.GpuDecal.ValidateLayout();
		Assert.AreEqual( 0, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.Position ) ) );
		Assert.AreEqual( 12, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.Rotation ) ) );
		Assert.AreEqual( 28, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.Scale ) ) );
		Assert.AreEqual( 40, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.PackedTextureIndex ) ) );
		Assert.AreEqual( 48, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.SortOrder ) ) );
		Assert.AreEqual( 52, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.ExclusionBitMask ) ) );
		Assert.AreEqual( 56, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.ColorTint ) ) );
		Assert.AreEqual( 60, (int)Marshal.OffsetOf<LightBinnerFeature.GpuDecal>( nameof( LightBinnerFeature.GpuDecal.ExtraDataOffset ) ) );
	}
}
