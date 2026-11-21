namespace Borealis.Gpu;

/// <summary>
/// The layout a texture's image is in, as the device follows it from the barriers of the lists submitted so far (applied
/// at submission, in queue order): one for the whole image, or one per mip and layer once a barrier moves only some of
/// them, for up to <see cref="MaxSubresources"/>; past that, a partial barrier leaves it unknown until a whole one.
/// </summary>
internal struct SubresourceLayouts
{
	/// <summary>Subresources (mips times layers) followed one by one.</summary>
	public const int MaxSubresources = 64;

	/// <summary>Every subresource's layout, unless <see cref="Mixed"/>.</summary>
	public ImageLayout Whole;

	/// <summary>Subresources are in different layouts, held in the packed words (4 bits each, mip-major).</summary>
	public bool Mixed;

	/// <summary>A partial barrier on an image with more subresources than followed: not known until a whole barrier.</summary>
	public bool Unknown;

	ulong p0, p1, p2, p3;

	/// <summary>The layout of mip <paramref name="mip"/>, layer <paramref name="layer"/> of an image with <paramref name="layers"/> layers.</summary>
	public readonly ImageLayout Of( int mip, int layer, int layers )
	{
		if ( !Mixed ) return Whole;
		var at = mip * layers + layer;
		var word = (at >> 4) switch { 0 => p0, 1 => p1, 2 => p2, _ => p3 };
		return (ImageLayout)((word >> ((at & 15) * 4)) & 0xF);
	}

	void Set( int at, ImageLayout layout )
	{
		var shift = (at & 15) * 4;
		ref var word = ref p0;
		switch ( at >> 4 )
		{
			case 1: word = ref p1; break;
			case 2: word = ref p2; break;
			case 3: word = ref p3; break;
		}

		word = (word & ~(0xFul << shift)) | ((ulong)layout << shift);
	}

	/// <summary>A barrier moved mips <paramref name="baseMip"/> on (<paramref name="mipCount"/>, 0 for the rest) and layers likewise to <paramref name="layout"/>.</summary>
	public void Apply( int baseMip, int mipCount, int baseLayer, int layerCount, ImageLayout layout, int mips, int layers )
	{
		var mipEnd = mipCount > 0 ? Math.Min( baseMip + mipCount, mips ) : mips;
		var layerEnd = layerCount > 0 ? Math.Min( baseLayer + layerCount, layers ) : layers;
		if ( baseMip == 0 && mipEnd == mips && baseLayer == 0 && layerEnd == layers )
		{
			Whole = layout;
			Mixed = Unknown = false;
			return;
		}

		if ( mips * layers > MaxSubresources )
		{
			Unknown = true;
			return;
		}

		if ( !Mixed )
		{
			for ( int i = 0; i < mips * layers; i++ ) Set( i, Whole );
			Mixed = true;
		}

		for ( int mip = baseMip; mip < mipEnd; mip++ )
		{
			for ( int layer = baseLayer; layer < layerEnd; layer++ ) Set( mip * layers + layer, layout );
		}

		// All alike again: one layout
		var first = Of( 0, 0, layers );
		for ( int i = 1; i < mips * layers; i++ )
		{
			if ( Of( i / layers, i % layers, layers ) != first ) return;
		}

		Whole = first;
		Mixed = false;
	}
}
