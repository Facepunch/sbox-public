using Sandbox.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SceneTests;

/// <summary>
/// The one part of sprite sorting that no other test reaches: whether the index arithmetic on the
/// GPU actually lines up.
///
/// Three separate pieces have to agree - the bitonic sort in <c>sort_cs.shader</c>, the draw runs
/// worked out by <see cref="SpriteDrawPlan"/>, and the backwards walk of the sort table in
/// <c>sprite_ps.shader</c>. Each is defensible on its own and the combination is easy to get off
/// by one, which would draw sprites with the wrong blend state or read past the real entries into
/// the padding. Neither shows up as a failure anywhere - just as a wrong picture.
///
/// So the shader code is transcribed here and run over real keys. This proves the arithmetic, not
/// the HLSL: it cannot tell us the shaders compile to this or that the attributes arrive. But the
/// arithmetic is the part that was only ever argued for.
/// </summary>
[TestClass]
public class SpriteDrawIndexingTest
{
	record struct Sprite( int Layer, SpriteBlendMode Blend, int Order, float Depth );

	// ---- transcribed from the shaders -------------------------------------------------------

	/// <summary>SORTKEY_MAX in sort_cs.shader. Sorts after every real sprite.</summary>
	static readonly (uint X, uint Y) SortKeyMax = (0xFFFFFFFFu, 0xFFFFFFFFu);

	/// <summary>FloatToSortableUint in sprite_cs.shader.</summary>
	static uint FloatToSortableUint( float value )
	{
		var bits = BitConverter.SingleToUInt32Bits( value );
		return (bits & 0x80000000u) != 0 ? ~bits : bits | 0x80000000u;
	}

	/// <summary>CalculateSortKey in sprite_cs.shader, for a sprite with no sorting group.</summary>
	static (uint X, uint Y) CalculateSortKey( Sprite sprite )
	{
		var packed = SpriteBatchSceneObject.SpriteData.PackSortKey( sprite.Layer, sprite.Blend, sprite.Order );
		var quantized = FloatToSortableUint( sprite.Depth ) & ~0xFFu;

		return (~packed, quantized | 0xFFu);
	}

	/// <summary>The lexicographic comparison in sort_cs.shader.</summary>
	static bool Greater( (uint X, uint Y) a, (uint X, uint Y) b )
		=> a.X != b.X ? a.X > b.X : a.Y > b.Y;

	static bool Less( (uint X, uint Y) a, (uint X, uint Y) b )
		=> a.X != b.X ? a.X < b.X : a.Y < b.Y;

	/// <summary>
	/// MainCs in sort_cs.shader, both the D_CLEAR pass and the sort passes, driven by the dispatch
	/// loop in SpriteBatchSceneObject.UploadOnHost.
	///
	/// Running the threads of a stage in sequence matches the GPU here: a stage only ever swaps
	/// disjoint pairs, and the shader's own <c>compareIndex &lt; currentIndex</c> guard means just
	/// one thread of each pair does the work.
	/// </summary>
	static uint[] RunBitonicSort( IReadOnlyList<Sprite> sprites )
	{
		var bufferSize = (int)BitOperations.RoundUpToPowerOf2( (uint)Math.Max( sprites.Count, 1 ) );

		// D_CLEAR: every slot is seeded, so the padding carries SORTKEY_MAX and sorts to the end.
		var sortBuffer = new uint[bufferSize];
		var keys = new (uint X, uint Y)[bufferSize];

		for ( var i = 0; i < bufferSize; i++ )
		{
			sortBuffer[i] = (uint)i;
			keys[i] = SortKeyMax;
		}

		// sprite_cs writes a real key for each live sprite; the rest keep SORTKEY_MAX.
		for ( var i = 0; i < sprites.Count; i++ )
		{
			keys[i] = CalculateSortKey( sprites[i] );
		}

		for ( var dim = 2; dim <= bufferSize; dim <<= 1 )
		{
			for ( var block = dim >> 1; block > 0; block >>= 1 )
			{
				for ( var currentIndex = 0; currentIndex < bufferSize; currentIndex++ )
				{
					var compareIndex = currentIndex ^ block;

					if ( compareIndex >= bufferSize || compareIndex < currentIndex ) continue;

					var indexA = sortBuffer[currentIndex];
					var indexB = sortBuffer[compareIndex];

					var keyA = keys[indexA];
					var keyB = keys[indexB];

					var ascending = (currentIndex & dim) == 0;

					if ( ascending ? Greater( keyA, keyB ) : Less( keyA, keyB ) )
					{
						sortBuffer[currentIndex] = indexB;
						sortBuffer[compareIndex] = indexA;
					}
				}
			}
		}

		return sortBuffer;
	}

	/// <summary>
	/// GetSprite in sprite_ps.shader. <paramref name="instanceId"/> is relative to the draw, which
	/// is why the run's offset comes into it.
	/// </summary>
	static int GetSpriteIndex( uint[] sortLut, int instanceCount, int sortLutOffset, int instanceId )
		=> (int)sortLut[instanceCount - 1 - instanceId - sortLutOffset];

	// ---- the actual pipeline ----------------------------------------------------------------

	/// <summary>
	/// Everything the renderer would do, end to end: build the runs, sort, then walk each run the
	/// way the shader does. Returns each drawn sprite paired with the blend state its draw call
	/// was configured for.
	/// </summary>
	static List<(Sprite Sprite, SpriteBlendMode DrawnAs)> Draw( IReadOnlyList<Sprite> sprites, int layerCount )
	{
		var buckets = new List<SpriteDrawBucket>();

		SpriteDrawPlan.BuildBuckets(
			sprites.Select( s => s.Layer ).ToArray(),
			sprites.Select( s => s.Blend ).ToArray(),
			layerCount,
			buckets,
			new int[layerCount * SpriteDrawPlan.BlendModeCount] );

		var sortLut = RunBitonicSort( sprites );
		var drawn = new List<(Sprite, SpriteBlendMode)>();

		foreach ( var bucket in buckets )
		{
			for ( var instanceId = 0; instanceId < bucket.Count; instanceId++ )
			{
				var index = GetSpriteIndex( sortLut, sprites.Count, bucket.Offset, instanceId );

				Assert.IsTrue( index >= 0 && index < sprites.Count,
					$"read index {index} outside the {sprites.Count} live sprites - the walk ran into the padding" );

				drawn.Add( (sprites[index], bucket.Blend) );
			}
		}

		return drawn;
	}

	// ---- tests ------------------------------------------------------------------------------

	[TestMethod]
	public void EverySpriteIsDrawnUnderItsOwnBlendState()
	{
		// The failure this exists for. If a run's offset is off by even one, sprites are handed to
		// a draw call set up for someone else's blend state - additive artwork rendered opaque, or
		// the reverse. Nothing errors; the picture is just wrong.
		var sprites = new List<Sprite>();
		var random = new Random( 4242 );

		for ( var i = 0; i < 120; i++ )
		{
			sprites.Add( new Sprite(
				random.Next( 0, 5 ),
				(SpriteBlendMode)random.Next( 0, SpriteDrawPlan.BlendModeCount ),
				random.Next( -50, 50 ),
				random.NextSingle() * 2000f - 1000f ) );
		}

		foreach ( var (sprite, drawnAs) in Draw( sprites, layerCount: 5 ) )
		{
			Assert.AreEqual( sprite.Blend, drawnAs );
		}
	}

	[TestMethod]
	public void EverySpriteIsDrawnExactlyOnce()
	{
		// Catches the walk overlapping itself or skipping a slot between runs.
		var sprites = Enumerable.Range( 0, 64 )
			.Select( i => new Sprite( i % 3, (SpriteBlendMode)(i % 3), i, i * 10f ) )
			.ToList();

		var drawn = Draw( sprites, layerCount: 3 );

		Assert.AreEqual( sprites.Count, drawn.Count );
		CollectionAssert.AreEquivalent( sprites, drawn.Select( d => d.Sprite ).ToList() );
	}

	[TestMethod]
	public void DrawOrderIsBackToFront()
	{
		// The order the whole feature is for: layer, then blend, then order in layer, then depth
		// with the furthest away drawn first.
		var sprites = new List<Sprite>
		{
			new( 1, SpriteBlendMode.Transparent, 0, 100f ),
			new( 0, SpriteBlendMode.Additive, 5, 50f ),
			new( 0, SpriteBlendMode.Transparent, 5, 10f ),
			new( 0, SpriteBlendMode.Transparent, 5, 900f ),
			new( 0, SpriteBlendMode.Transparent, -3, 20f ),
			new( 2, SpriteBlendMode.Opaque, 0, 30f ),
		};

		var expected = sprites
			.OrderBy( s => s.Layer )
			.ThenBy( s => (int)s.Blend )
			.ThenBy( s => s.Order )
			.ThenByDescending( s => s.Depth )
			.ToList();

		CollectionAssert.AreEqual( expected, Draw( sprites, layerCount: 3 ).Select( d => d.Sprite ).ToList() );
	}

	[TestMethod]
	public void PaddingIsNeverDrawn()
	{
		// A sprite count just past a power of two leaves the buffer mostly padding. If the walk
		// started from the padded size instead of the live count, this is where it would show.
		foreach ( var count in new[] { 1, 2, 3, 5, 9, 17, 33, 65 } )
		{
			var sprites = Enumerable.Range( 0, count )
				.Select( i => new Sprite( i % 2, SpriteBlendMode.Transparent, i, i ) )
				.ToList();

			var drawn = Draw( sprites, layerCount: 2 );

			Assert.AreEqual( count, drawn.Count, $"{count} sprites" );
			CollectionAssert.AreEquivalent( sprites, drawn.Select( d => d.Sprite ).ToList(), $"{count} sprites" );
		}
	}

	[TestMethod]
	public void SingleDrawPathMatchesTheBucketedPath()
	{
		// A batch with one blend state takes the old single-draw path with offset 0. It has to
		// produce the same order as the bucketed path, or enabling the merge would change how
		// existing scenes look.
		var sprites = Enumerable.Range( 0, 40 )
			.Select( i => new Sprite( i % 3, SpriteBlendMode.Transparent, i % 7, (i * 37) % 500 ) )
			.ToList();

		var sortLut = RunBitonicSort( sprites );

		var singleDraw = Enumerable.Range( 0, sprites.Count )
			.Select( i => sprites[GetSpriteIndex( sortLut, sprites.Count, 0, i )] )
			.ToList();

		CollectionAssert.AreEqual( singleDraw, Draw( sprites, layerCount: 3 ).Select( d => d.Sprite ).ToList() );
	}

	[TestMethod]
	public void ArbitraryScenesKeepEveryGuarantee()
	{
		var random = new Random( 20260719 );

		for ( var iteration = 0; iteration < 300; iteration++ )
		{
			var layerCount = random.Next( 1, 6 );
			var count = random.Next( 1, 130 );

			var sprites = Enumerable.Range( 0, count )
				.Select( _ => new Sprite(
					random.Next( 0, layerCount ),
					(SpriteBlendMode)random.Next( 0, SpriteDrawPlan.BlendModeCount ),
					random.Next( -20, 20 ),
					random.NextSingle() * 400f - 200f ) )
				.ToList();

			var drawn = Draw( sprites, layerCount );
			var context = $"iteration {iteration}, {count} sprites over {layerCount} layers";

			Assert.AreEqual( count, drawn.Count, context );

			foreach ( var (sprite, drawnAs) in drawn )
			{
				Assert.AreEqual( sprite.Blend, drawnAs, context );
			}

			// Layers must come out non-decreasing however the blend states are mixed in.
			var layers = drawn.Select( d => d.Sprite.Layer ).ToArray();
			CollectionAssert.AreEqual( layers.OrderBy( l => l ).ToArray(), layers, context );
		}
	}
}
