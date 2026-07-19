namespace Sandbox.Rendering;

/// <summary>
/// The blend state a sprite has to be drawn with. Sprites needing different states cannot share a
/// draw call, so this is the second axis - after the sort layer - that the draw order is cut along.
/// </summary>
internal enum SpriteBlendMode
{
	/// <summary>Writes depth and occludes. Drawn before the see-through work in the same layer.</summary>
	Opaque = 0,

	/// <summary>Ordinary alpha blending.</summary>
	Transparent = 1,

	/// <summary>Additive blending, which has no meaningful order against itself.</summary>
	Additive = 2,
}

/// <summary>
/// One run of sprites sharing a sort layer and a blend state, drawn as a single instanced call.
/// </summary>
internal readonly record struct SpriteDrawBucket( int LayerIndex, SpriteBlendMode Blend, int Offset, int Count );

/// <summary>
/// Works out the order sprites have to be drawn in, and where each run of them lives in the buffer.
///
/// The problem this solves: a sort layer is meant to beat everything, but sprites needing different
/// blend states cannot be drawn together, and the engine gives no way to order one scene object
/// against another. So the ordering has to happen *inside* a single scene object, by laying the
/// sprites out grouped by layer and blend and then issuing one draw per group, layer by layer.
///
/// Grouping on the CPU is what keeps this verifiable: the run boundaries are known before anything
/// reaches the GPU, so no read-back or indirect draw is needed, and the draw order is a pure
/// function of the sprites - which is what <c>SpriteDrawPlanTest</c> pins down.
/// </summary>
internal static class SpriteDrawPlan
{
	/// <summary>Number of distinct blend states, and so the number of buckets a layer can hold.</summary>
	internal const int BlendModeCount = 3;

	/// <summary>
	/// The blend state a sprite's flags call for. Opaque wins over additive, because an opaque
	/// sprite is never blended whatever else it asks for.
	/// </summary>
	internal static SpriteBlendMode GetBlendMode( bool opaque, bool additive )
	{
		if ( opaque ) return SpriteBlendMode.Opaque;

		return additive ? SpriteBlendMode.Additive : SpriteBlendMode.Transparent;
	}

	/// <summary>
	/// The position of a sprite's bucket in the draw order. Layer is the more significant term, so
	/// every bucket of a lower layer is drawn before any bucket of a higher one - which is exactly
	/// the guarantee sort layers are supposed to make.
	/// </summary>
	internal static int GetBucketIndex( int layerIndex, SpriteBlendMode blend )
		=> layerIndex * BlendModeCount + (int)blend;

	/// <summary>
	/// Works out where each run of sprites will land in the sorted draw order.
	///
	/// Nothing is rearranged here. The GPU sort already puts sprites in key order, and the key
	/// leads with layer then blend - so a run's *position* is simply how many sprites sort ahead
	/// of it, which is a matter of counting. That is what removes any need to read the sort result
	/// back or to permute the buffer on the CPU.
	///
	/// Linear time, because this is on the per-frame upload path.
	/// </summary>
	/// <param name="layerIndices">Each sprite's resolved sort layer index.</param>
	/// <param name="blendModes">Each sprite's blend state, parallel to <paramref name="layerIndices"/>.</param>
	/// <param name="layerCount">Number of layers in the project, sizing the counting pass.</param>
	/// <param name="buckets">Receives the non-empty runs in draw order, cleared first.</param>
	/// <param name="counts">Scratch of at least <paramref name="layerCount"/> * <see cref="BlendModeCount"/> entries.</param>
	internal static void BuildBuckets(
		ReadOnlySpan<int> layerIndices,
		ReadOnlySpan<SpriteBlendMode> blendModes,
		int layerCount,
		List<SpriteDrawBucket> buckets,
		Span<int> counts )
	{
		buckets.Clear();

		var spriteCount = layerIndices.Length;
		var bucketCount = Math.Max( layerCount, 1 ) * BlendModeCount;

		counts = counts[..bucketCount];
		counts.Clear();

		for ( var i = 0; i < spriteCount; i++ )
		{
			counts[BucketOf( layerIndices[i], blendModes[i], layerCount )]++;
		}

		// Turn the counts into start offsets. Empty buckets are skipped rather than emitted as
		// zero-length draws - layers are project-wide, so most scenes leave most of them unused.
		var running = 0;

		for ( var bucket = 0; bucket < counts.Length; bucket++ )
		{
			if ( counts[bucket] > 0 )
			{
				buckets.Add( new SpriteDrawBucket(
					LayerIndex: bucket / BlendModeCount,
					Blend: (SpriteBlendMode)(bucket % BlendModeCount),
					Offset: running,
					Count: counts[bucket] ) );
			}

			running += counts[bucket];
		}
	}

	/// <summary>
	/// A sprite's bucket, with an out-of-range layer sent back to the default one.
	///
	/// A layer index can outlive the layer it referred to, if one is deleted while sprites still
	/// point at it. Falling back to layer 0 matches how <see cref="SortingSettings.GetLayerIndex"/>
	/// already resolves an unknown id - and it fails in the quieter direction, leaving the orphan
	/// at the back rather than promoting it in front of everything.
	/// </summary>
	private static int BucketOf( int layerIndex, SpriteBlendMode blend, int layerCount )
	{
		var resolved = layerIndex >= 0 && layerIndex < layerCount ? layerIndex : 0;

		return GetBucketIndex( resolved, blend );
	}
}
