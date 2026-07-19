using Sandbox.Rendering;
using System;
using System.Collections.Generic;

namespace SceneTests;

// The sprite draw order is a lexicographic comparison of a packed layer/blend/order key and a
// distance, resolved on the GPU. These tests pin down the CPU half of that key: the packing has to
// be monotonic, because a non-monotonic key sorts wrongly with no visible error anywhere.
[TestClass]
public class SpriteSortKeyTest
{
	// Blend sits between layer and order in the key. Most of these cases only care about layer and
	// order, so they hold blend fixed at Transparent - what an ordinary 2D sprite is.
	static uint Pack( int layer, int order, SpriteBlendMode blend = SpriteBlendMode.Transparent )
		=> SpriteBatchSceneObject.SpriteData.PackSortKey( layer, blend, order );

	[TestMethod]
	public void SpritesWithNoSortingSetUpCompareEqual()
	{
		// Existing content has no layer or order, so every such sprite has to pack identically and
		// leave the distance term to decide - exactly the behaviour before sorting existed.
		Assert.AreEqual( Pack( 0, 0 ), Pack( 0, 0 ) );

		// And it must sit at the bottom of its blend state's range, so anything explicitly ordered
		// rises above it.
		Assert.AreEqual( Pack( 0, short.MinValue ), Pack( 0, 0 ) & 0xFFFF0000u );
	}

	[TestMethod]
	public void LayerOutranksOrder()
	{
		// A higher layer beats any order within a lower layer, however extreme.
		Assert.IsTrue( Pack( 1, short.MinValue ) > Pack( 0, short.MaxValue ) );
	}

	[TestMethod]
	public void OrderIsMonotonicWithinLayer()
	{
		int[] orders = [short.MinValue, -1000, -1, 0, 1, 1000, short.MaxValue];

		for ( int i = 1; i < orders.Length; i++ )
		{
			Assert.IsTrue( Pack( 5, orders[i - 1] ) < Pack( 5, orders[i] ),
				$"order {orders[i - 1]} should pack below {orders[i]}" );
		}
	}

	[TestMethod]
	public void NegativeOrderSortsBelowZero()
	{
		// The signed-to-unsigned bias exists for this case, so test it on its own.
		Assert.IsTrue( Pack( 3, -1 ) < Pack( 3, 0 ) );
	}

	[TestMethod]
	public void LayerIsMonotonic()
	{
		int[] layers = [0, 1, 2, 255, 256, 1000, ushort.MaxValue];

		for ( int i = 1; i < layers.Length; i++ )
		{
			Assert.IsTrue( Pack( layers[i - 1], 0 ) < Pack( layers[i], 0 ),
				$"layer {layers[i - 1]} should pack below {layers[i]}" );
		}
	}

	[TestMethod]
	public void OutOfRangeValuesClamp()
	{
		// Clamping rather than wrapping - a wrapped key would sort an extreme value to the
		// opposite end of the scene, which is the worst possible failure.
		Assert.AreEqual( Pack( 0, short.MinValue ), Pack( 0, int.MinValue ) );
		Assert.AreEqual( Pack( 0, short.MaxValue ), Pack( 0, int.MaxValue ) );
		Assert.AreEqual( Pack( 0, 0 ), Pack( int.MinValue, 0 ) );
	}

	[TestMethod]
	public void PackingIsUniqueAcrossTheGrid()
	{
		var seen = new HashSet<uint>();

		for ( int layer = 0; layer < 40; layer++ )
		{
			for ( int order = -20; order <= 20; order++ )
			{
				Assert.IsTrue( seen.Add( Pack( layer, order ) ),
					$"layer {layer} order {order} collided with another key" );
			}
		}
	}

	[TestMethod]
	public void LayerOutranksBlendMode()
	{
		// Blend state groups sprites into drawable runs, but it must never override a layer -
		// otherwise every additive sprite in the scene would jump in front of every ordinary one.
		Assert.IsTrue( Pack( 1, 0, SpriteBlendMode.Opaque ) > Pack( 0, 0, SpriteBlendMode.Additive ) );
	}

	[TestMethod]
	public void BlendModeOutranksOrderWithinALayer()
	{
		// The trade the renderer forces: sprites needing different blend states cannot share a
		// draw call, so within one layer they are grouped by blend before order is considered.
		Assert.IsTrue( Pack( 2, short.MinValue, SpriteBlendMode.Additive ) > Pack( 2, short.MaxValue, SpriteBlendMode.Transparent ) );
	}

	[TestMethod]
	public void BlendModesGroupIntoContiguousRuns()
	{
		// Each blend state has to occupy one unbroken run of the key space, or a run could not be
		// drawn as a single call over a contiguous range.
		//
		// int, not var: var would infer short here, and short arithmetic wraps silently at 32767
		// rather than throwing - which turns this into an infinite loop.
		for ( int order = short.MinValue; order < short.MaxValue; order += 4096 )
		{
			Assert.IsTrue( Pack( 7, order, SpriteBlendMode.Opaque ) < Pack( 7, short.MinValue, SpriteBlendMode.Transparent ) );
			Assert.IsTrue( Pack( 7, order, SpriteBlendMode.Transparent ) < Pack( 7, short.MinValue, SpriteBlendMode.Additive ) );
		}
	}

	// The compute shader inverts the packed key, because the pixel shader walks the sort LUT
	// backwards. Getting this backwards would put the front layer at the back.
	static uint Coarse( int layer, int order, SpriteBlendMode blend = SpriteBlendMode.Transparent ) => ~Pack( layer, order, blend );

	[TestMethod]
	public void InvertedKeyPutsHigherLayersInFront()
	{
		// Drawn back to front, so "in front" means a smaller sorted key.
		Assert.IsTrue( Coarse( 2, 0 ) < Coarse( 1, 0 ) );
		Assert.IsTrue( Coarse( 1, 10 ) < Coarse( 1, 9 ) );
	}

	// Mirror of FloatToSortableUint in sprite_cs.shader. The GPU compares distances as unsigned
	// integers so they can share a comparison with the packed key, and that mapping has to
	// preserve float ordering across the sign boundary.
	static uint FloatToSortableUint( float value )
	{
		var bits = BitConverter.SingleToUInt32Bits( value );
		return (bits & 0x80000000u) != 0 ? ~bits : bits | 0x80000000u;
	}

	[TestMethod]
	public void FloatMappingPreservesOrder()
	{
		float[] values = [-1e30f, -1000f, -1f, -0.001f, 0f, 0.001f, 1f, 1000f, 1e30f];

		for ( int i = 1; i < values.Length; i++ )
		{
			Assert.IsTrue( FloatToSortableUint( values[i - 1] ) < FloatToSortableUint( values[i] ),
				$"{values[i - 1]} should map below {values[i]}" );
		}
	}

	// Only CustomAxis sorts along an axis. Every other mode has to resolve to zero, because the
	// shader reads a zero axis as "use camera depth" - that fallback is what keeps existing
	// cameras rendering unchanged.
	[TestMethod]
	public void OnlyCustomAxisModeResolvesAnAxis()
	{
		var axis = new Vector3( 0, 1, 0 );

		Assert.AreEqual( Vector3.Zero, CameraComponent.ResolveSortAxis( TransparencySortMode.Default, axis ) );
		Assert.AreEqual( Vector3.Zero, CameraComponent.ResolveSortAxis( TransparencySortMode.Perspective, axis ) );
		Assert.AreEqual( Vector3.Zero, CameraComponent.ResolveSortAxis( TransparencySortMode.Orthographic, axis ) );
		Assert.AreEqual( axis, CameraComponent.ResolveSortAxis( TransparencySortMode.CustomAxis, axis ) );
	}

	[TestMethod]
	public void SortAxisIsNormalized()
	{
		// Length would otherwise scale every distance, which changes nothing about the ordering
		// but makes the numbers meaningless to anyone reading them back.
		var resolved = CameraComponent.ResolveSortAxis( TransparencySortMode.CustomAxis, new Vector3( 0, 12, 0 ) );

		Assert.AreEqual( 1f, resolved.Length, 0.0001f );
	}

	[TestMethod]
	public void ZeroSortAxisStaysZeroRatherThanBecomingNaN()
	{
		// A camera switched to CustomAxis before an axis is typed in. Degrading to camera depth is
		// survivable; a NaN axis would poison every key in the batch.
		var resolved = CameraComponent.ResolveSortAxis( TransparencySortMode.CustomAxis, Vector3.Zero );

		Assert.AreEqual( Vector3.Zero, resolved );
		Assert.IsFalse( float.IsNaN( resolved.x ) );
	}

	// Mirror of the fine key in sprite_cs.shader: the depth with its low bits traded away for a
	// sorting group member's rank.
	const uint RankMask = 0xFFu;

	static uint Fine( float depth, int rank )
	{
		var quantized = FloatToSortableUint( depth ) & ~RankMask;
		var inverted = RankMask - Math.Min( (uint)rank, RankMask );

		return quantized | inverted;
	}

	// The full key as the GPU compares it: coarse first, axis distance only as a tiebreak.
	static (uint Coarse, uint Fine) Key( int layer, int order, float axisDistance, int rank = 0 )
		=> (Coarse( layer, order ), Fine( axisDistance, rank ));

	static bool DrawsInFrontOf( (uint Coarse, uint Fine) a, (uint Coarse, uint Fine) b )
		=> a.Coarse != b.Coarse ? a.Coarse < b.Coarse : a.Fine < b.Fine;

	[TestMethod]
	public void HigherAlongSortAxisDrawsBehind()
	{
		// The whole point of Y-sorting: a character lower on the screen walks in front of one
		// higher up. Same layer, same order, so only the axis distance decides.
		var nearer = Key( 0, 0, axisDistance: 10f );
		var further = Key( 0, 0, axisDistance: 90f );

		Assert.IsTrue( DrawsInFrontOf( nearer, further ) );
	}

	[TestMethod]
	public void SortAxisWorksAcrossTheOrigin()
	{
		// Unlike camera distance, an axis dot product is signed - sprites on the far side of the
		// world origin produce negative distances and still have to order correctly.
		var below = Key( 0, 0, axisDistance: -500f );
		var above = Key( 0, 0, axisDistance: 500f );

		Assert.IsTrue( DrawsInFrontOf( below, above ) );
	}

	[TestMethod]
	public void SortLayerBeatsSortAxis()
	{
		// A sprite explicitly placed in a higher layer must draw in front no matter how far along
		// the sort axis it sits. If the axis could ever win, layers would be advisory.
		var highLayerFarBack = Key( 3, 0, axisDistance: 1e6f );
		var lowLayerFarFront = Key( 0, 0, axisDistance: -1e6f );

		Assert.IsTrue( DrawsInFrontOf( highLayerFarBack, lowLayerFarFront ) );
	}

	[TestMethod]
	public void HigherRankDrawsInFrontWithinAGroup()
	{
		// Every member of a group resolves to the group's own depth, so rank is the only thing
		// separating them.
		var back = Key( 0, 0, axisDistance: 100f, rank: 0 );
		var front = Key( 0, 0, axisDistance: 100f, rank: 5 );

		Assert.IsTrue( DrawsInFrontOf( front, back ) );
	}

	// The property sorting groups exist for: nothing from outside a group may be drawn in between
	// its members. This is the test that would catch the rank bits being too few, or the depth
	// quantization being finer than the rank field.
	[TestMethod]
	public void OutsideSpriteCannotLandBetweenGroupMembers()
	{
		const float groupDepth = 500f;

		var first = Key( 0, 0, groupDepth, rank: 0 );
		var last = Key( 0, 0, groupDepth, rank: 20 );

		// Depths within the group's own quantization bucket are the dangerous ones - anything
		// further away is separated by the depth term and was never a risk.
		float[] nearbyDepths = [groupDepth, groupDepth + 0.001f, groupDepth - 0.001f, groupDepth + 0.01f];

		foreach ( var depth in nearbyDepths )
		{
			var outsider = Key( 0, 0, depth, rank: 0 );

			var isBetween = DrawsInFrontOf( outsider, first ) && DrawsInFrontOf( last, outsider );

			Assert.IsFalse( isBetween, $"a sprite at depth {depth} sliced the group apart" );
		}
	}

	[TestMethod]
	public void QuantizationNeverReordersSeparatedDepths()
	{
		// Trading the low bits away may make two near-equal depths tie, but it must never swap
		// them. Anything coarser than the quantization step has to survive intact.
		float[] depths = [-1e5f, -1f, 0f, 1f, 100f, 1e5f];

		for ( var i = 1; i < depths.Length; i++ )
		{
			Assert.IsTrue( Fine( depths[i - 1], 0 ) <= Fine( depths[i], 0 ),
				$"depth {depths[i - 1]} must not quantize above {depths[i]}" );
		}
	}

	[TestMethod]
	public void RanksBeyondTheLastRungShareIt()
	{
		// Groups larger than the rank field degrade to a tie rather than wrapping around, which
		// would otherwise send the 256th member to the very back of the group.
		var last = Key( 0, 0, 10f, rank: SortingGroup.MaxRank );
		var overflow = Key( 0, 0, 10f, rank: SortingGroup.MaxRank + 50 );

		Assert.AreEqual( last.Fine, overflow.Fine );
		Assert.IsFalse( DrawsInFrontOf( overflow, last ) );
	}

	[TestMethod]
	public void UngroupedSpritesAllShareTheBottomRung()
	{
		// Rank 0 is what every ungrouped sprite gets, so the rank bits must be a constant for
		// them and leave the depth entirely in charge.
		Assert.AreEqual( Fine( 42f, 0 ) & RankMask, Fine( 99f, 0 ) & RankMask );
	}
}
