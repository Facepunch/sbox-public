using Sandbox.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SceneTests;

// Sort layers are supposed to beat everything, and until now they did not: sprites needing
// different blend states ended up in different scene objects, and nothing in the engine orders one
// scene object against another. They now share a batch and are drawn as one run per
// (layer, blend) - so the layer wins, at the price of a few extra draw calls.
//
// This pins down where those runs land. The other half - that the GPU sort actually puts each
// sprite in the run these offsets claim - is what the key packing in SpriteSortKeyTest guarantees.
[TestClass]
public class SpriteDrawPlanTest
{
	record struct Sprite( int Layer, SpriteBlendMode Blend );

	// layerCount defaults to "every layer these sprites mention exists". Pass it explicitly to
	// model a project with fewer layers than the sprites are asking for.
	static List<SpriteDrawBucket> BuildPlan( Sprite[] sprites, int? layerCountOverride = null )
	{
		var layerCount = layerCountOverride
			?? (sprites.Length == 0 ? 1 : sprites.Max( s => s.Layer ) + 1);

		var buckets = new List<SpriteDrawBucket>();

		SpriteDrawPlan.BuildBuckets(
			sprites.Select( s => s.Layer ).ToArray(),
			sprites.Select( s => s.Blend ).ToArray(),
			layerCount,
			buckets,
			new int[layerCount * SpriteDrawPlan.BlendModeCount] );

		return buckets;
	}

	// The whole point of the change. Before it, these two lived in separate scene objects and the
	// additive one could be drawn either side of the other, whatever the layers said.
	[TestMethod]
	public void LayerBeatsBlendMode()
	{
		var plan = BuildPlan( [
			new Sprite( 1, SpriteBlendMode.Additive ),
			new Sprite( 0, SpriteBlendMode.Transparent )] );

		Assert.AreEqual( 0, plan[0].LayerIndex, "the lower layer has to be drawn first, at the back" );
		Assert.AreEqual( 1, plan[1].LayerIndex );
	}

	[TestMethod]
	public void EveryLowerLayerIsFullyDrawnBeforeAnyHigherOne()
	{
		var plan = BuildPlan( [
			new Sprite( 2, SpriteBlendMode.Transparent ),
			new Sprite( 0, SpriteBlendMode.Additive ),
			new Sprite( 1, SpriteBlendMode.Opaque ),
			new Sprite( 2, SpriteBlendMode.Additive ),
			new Sprite( 0, SpriteBlendMode.Transparent ),
			new Sprite( 1, SpriteBlendMode.Additive )] );

		var layers = plan.Select( b => b.LayerIndex ).ToArray();

		CollectionAssert.AreEqual( layers.OrderBy( l => l ).ToArray(), layers,
			"runs must come out in layer order, whatever blend states are mixed in" );
	}

	[TestMethod]
	public void OpaqueIsDrawnBeforeBlendedWorkInTheSameLayer()
	{
		// Opaque sprites occlude, so drawing them first lets the depth test reject what is behind
		// them instead of blending over it.
		var plan = BuildPlan( [
			new Sprite( 0, SpriteBlendMode.Additive ),
			new Sprite( 0, SpriteBlendMode.Transparent ),
			new Sprite( 0, SpriteBlendMode.Opaque )] );

		CollectionAssert.AreEqual(
			new[] { SpriteBlendMode.Opaque, SpriteBlendMode.Transparent, SpriteBlendMode.Additive },
			plan.Select( b => b.Blend ).ToArray() );
	}

	[TestMethod]
	public void EverySpriteIsAccountedForExactlyOnce()
	{
		// A miscount here would leave sprites outside every run, so they would simply never draw.
		var sprites = Enumerable.Range( 0, 50 )
			.Select( i => new Sprite( i % 4, (SpriteBlendMode)(i % 3) ) )
			.ToArray();

		Assert.AreEqual( sprites.Length, BuildPlan( sprites ).Sum( b => b.Count ) );
	}

	[TestMethod]
	public void RunsAreContiguousAndInDrawOrder()
	{
		// Each run becomes one instanced draw over [Offset, Offset+Count), so a gap or an overlap
		// between them would silently skip or repeat sprites.
		var sprites = Enumerable.Range( 0, 30 )
			.Select( i => new Sprite( i % 3, (SpriteBlendMode)(i % 3) ) )
			.ToArray();

		var expectedOffset = 0;

		foreach ( var bucket in BuildPlan( sprites ) )
		{
			Assert.AreEqual( expectedOffset, bucket.Offset, "runs must tile the buffer with no holes" );
			expectedOffset += bucket.Count;
		}

		Assert.AreEqual( sprites.Length, expectedOffset );
	}

	[TestMethod]
	public void EmptyRunsAreNotDrawn()
	{
		// Layers are project-wide, so most scenes leave most of them unused. A draw call per empty
		// layer would cost more than the ordering is worth.
		var plan = BuildPlan( [new Sprite( 3, SpriteBlendMode.Transparent )] );

		Assert.AreEqual( 1, plan.Count );
		Assert.AreEqual( 3, plan[0].LayerIndex );
		Assert.AreEqual( 1, plan[0].Count );
	}

	[TestMethod]
	public void LayersPastTheEndFallBackToTheFirst()
	{
		// A sprite can outlive the layer it pointed at - delete a layer and its sprites keep the
		// old index. It must still be drawn; dropping it would make deleting a layer look like
		// deleting the artwork.
		var plan = BuildPlan( [new Sprite( 99, SpriteBlendMode.Transparent )], layerCountOverride: 2 );

		Assert.AreEqual( 1, plan.Sum( b => b.Count ) );
		Assert.AreEqual( 0, plan[0].LayerIndex );
	}

	[TestMethod]
	public void OpaqueWinsOverAdditive()
	{
		// An opaque sprite is never blended, whatever else it asks for.
		Assert.AreEqual( SpriteBlendMode.Opaque, SpriteDrawPlan.GetBlendMode( opaque: true, additive: true ) );
		Assert.AreEqual( SpriteBlendMode.Additive, SpriteDrawPlan.GetBlendMode( opaque: false, additive: true ) );
		Assert.AreEqual( SpriteBlendMode.Transparent, SpriteDrawPlan.GetBlendMode( opaque: false, additive: false ) );
	}

	[TestMethod]
	public void NoSpritesProducesNoDraws()
	{
		Assert.AreEqual( 0, BuildPlan( [] ).Count );
	}

	// The cases above are the ones worth reading. This is the one worth trusting: arbitrary mixes,
	// re-checking the invariants that must hold for every scene rather than the shapes someone
	// thought to write down. Seeded, so a failure reproduces.
	[TestMethod]
	public void InvariantsHoldAcrossArbitraryScenes()
	{
		var random = new Random( 20260719 );

		for ( var iteration = 0; iteration < 500; iteration++ )
		{
			var layerCount = random.Next( 1, 9 );
			var spriteCount = random.Next( 0, 200 );

			var sprites = Enumerable.Range( 0, spriteCount )
				.Select( _ => new Sprite(
					random.Next( 0, layerCount ),
					(SpriteBlendMode)random.Next( 0, SpriteDrawPlan.BlendModeCount ) ) )
				.ToArray();

			var plan = BuildPlan( sprites, layerCount );
			var context = $"iteration {iteration}, {spriteCount} sprites over {layerCount} layers";

			// Nothing left outside a run.
			Assert.AreEqual( spriteCount, plan.Sum( b => b.Count ), context );

			// The runs tile the buffer exactly, since each becomes a draw over its own range.
			var expectedOffset = 0;
			foreach ( var bucket in plan )
			{
				Assert.AreEqual( expectedOffset, bucket.Offset, context );
				Assert.IsTrue( bucket.Count > 0, context );
				expectedOffset += bucket.Count;
			}

			// The guarantee the whole feature rests on: a lower layer is never drawn after a
			// higher one, whatever blend states are mixed into either.
			var keys = plan.Select( b => SpriteDrawPlan.GetBucketIndex( b.LayerIndex, b.Blend ) ).ToArray();
			CollectionAssert.AreEqual( keys.OrderBy( k => k ).ToArray(), keys, context );
		}
	}
}
