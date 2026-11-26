using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// Giving a texture another image under the same handle (<see cref="Replace"/>): a streamed texture's image of other mips,
/// a copy in other memory. The handle, its bindless slots, the persistent sets that bind it and the views made of it stay,
/// so nothing that holds the slot or the handle is made again; only the Vulkan image, memory and default views change. The
/// bindless table and persistent sets can't be written while work that may read them is pending, so replacements wait,
/// batched, until a frame that begins with the GPU idle (as <see cref="RelocateDraining"/>).
/// </summary>
public sealed unsafe partial class Device
{
	/// <summary>Frames a replacement waits for others to join it, so that one wait for the GPU swaps many images.</summary>
	public int ReplaceAfterFrames { get; set; } = 16;

	/// <summary>Bytes of new images waiting that make them due at once, as video memory holds the old and the new until then.</summary>
	public long ReplaceAfterBytes { get; set; } = 128L << 20;

	/// <summary>Textures given new images so far, and waits for the GPU they took.</summary>
	public long TexturesReplaced { get; private set; }

	/// <inheritdoc cref="TexturesReplaced"/>
	public long ReplacementWaits { get; private set; }

	// New images by the texture they're for, until BeginFrame swaps them in
	readonly Dictionary<TextureHandle, TextureHandle> replacements = new();
	readonly List<(TextureHandle Live, TextureHandle Image)> replacing = new();
	ulong replaceSince;
	long replaceBytes;
	bool replaceNow;

	/// <summary>Whether <paramref name="texture"/> is waiting for a new image (<see cref="Replace"/>).</summary>
	public bool IsReplacing( TextureHandle texture )
	{
		lock ( replacements ) return replacements.ContainsKey( texture );
	}

	/// <summary>
	/// Give <paramref name="texture"/> the image <paramref name="image"/> holds, in a coming <see cref="BeginFrame"/> that finds
	/// no work pending: until then <paramref name="texture"/> keeps its image, so everything that samples it samples that, and
	/// the old one is destroyed once the swap is done. <paramref name="image"/> is the caller's no more: its handle is
	/// destroyed with the old image, and must be completely written (submitted) by then. Same format, dimension, layers,
	/// samples and usage, but any size and mips: views of <paramref name="texture"/> made with <see cref="CreateView"/> are
	/// made again over the new image, so they must fit it. A second image for the same texture before the swap replaces the
	/// first.
	/// </summary>
	public void Replace( TextureHandle texture, TextureHandle image )
	{
		ref var live = ref Get( texture );
		ref var next = ref Get( image );
		if ( texture == image || live.External || next.External || live.Pinned || next.Pinned ) throw new ArgumentException( "Neither texture can be a swap chain image or the device's own" );
		if ( live.Info.Format != next.Info.Format || live.Info.Dimension != next.Info.Dimension || live.Info.Layers != next.Info.Layers || live.Info.Samples != next.Info.Samples || live.Info.Usage != next.Info.Usage )
		{
			throw new ArgumentException( $"{next.Info.Name} can't take {live.Info.Name}'s place: its format, dimension, layers, samples or usage differ" );
		}

		var bytes = (long)next.Memory.Size;
		TextureHandle superseded;
		var had = false;
		lock ( replacements )
		{
			had = replacements.Remove( texture, out superseded );
			if ( had ) replaceBytes -= (long)Get( superseded ).Memory.Size;
			else if ( replacements.Count == 0 ) replaceSince = FrameNumber;
			replacements[texture] = image;
			replaceBytes += bytes;
		}

		if ( had ) Destroy( superseded );
	}

	/// <summary>Have every waiting replacement swapped in by the next <see cref="BeginFrame"/>: video memory ran short.</summary>
	public void ReplaceSoon()
	{
		lock ( replacements ) replaceNow = replacements.Count > 0;
	}

	/// <summary>
	/// In <see cref="BeginFrame"/>: once the replacements are due, wait for the GPU, then give each texture its new image and
	/// write what names it again. Put off to the next frame while anything records.
	/// </summary>
	void ApplyReplacements()
	{
		lock ( replacements )
		{
			if ( replacements.Count == 0 ) return;
			if ( !replaceNow && FrameNumber - replaceSince < (ulong)Math.Max( ReplaceAfterFrames, 0 ) && replaceBytes < ReplaceAfterBytes ) return;
		}

		if ( IsLost || !TryBeginMove() ) return;
		try
		{
			// Everything recorded has been sent; once the GPU has run it, nothing pending reads the old images or the slots
			Flush();
			graphicsQueue.Wait( graphicsQueue.Submitted );
			computeQueue.Wait( computeQueue.Submitted );
			ReplacementWaits++;

			lock ( replacements )
			{
				foreach ( var (live, image) in replacements ) replacing.Add( (live, image) );
				replacements.Clear();
				replaceBytes = 0;
				replaceNow = false;
			}

			foreach ( var (live, image) in replacing )
			{
				// Destroyed meanwhile: its new image goes with it
				if ( !Textures.IsAlive( live.Index, live.Generation ) ) continue;
				SwapImages( live, image );
				movedTextures.Add( live );
				TexturesReplaced++;
			}

			// Views made of them over the new images, and every set that names them, in their slots
			if ( movedTextures.Count > 0 )
			{
				RemakeViews();
				RelocatedBindings += RewriteSets( new TexturesIn( movedTextures, movedViews ) );
			}

			// What the handles hold now is the old images: they go once the frame is done, as anything destroyed does
			foreach ( var (_, image) in replacing ) Destroy( image );
			foreach ( var view in oldViews ) DestroyViewLater( view );
		}
		finally
		{
			replacing.Clear();
			movedTextures.Clear();
			movedViews.Clear();
			oldViews.Clear();
			EndMove();
		}
	}

	/// <summary>
	/// Exchange the images, memory, views and layouts of two textures, and point <paramref name="live"/>'s bindless slots at
	/// the new views. The other keeps its own slots (they go with it), which hold the views it had before.
	/// </summary>
	void SwapImages( TextureHandle live, TextureHandle image )
	{
		ref var kept = ref Get( live );
		ref var other = ref Get( image );
		(kept.Image, other.Image) = (other.Image, kept.Image);
		(kept.Memory, other.Memory) = (other.Memory, kept.Memory);
		(kept.Info, other.Info) = (other.Info, kept.Info);
		(kept.View, other.View) = (other.View, kept.View);
		(kept.SrgbView, other.SrgbView) = (other.SrgbView, kept.SrgbView);
		(kept.StorageView, other.StorageView) = (other.StorageView, kept.StorageView);
		(kept.Attachments, other.Attachments) = (other.Attachments, kept.Attachments);
		(kept.Layouts, other.Layouts) = (other.Layouts, kept.Layouts);
		Allocator.SetOwner( kept.Memory, OwnerOf( live ) );
		Allocator.SetOwner( other.Memory, OwnerOf( image ) );

		if ( kept.BindlessIndex >= 0 ) Bindless.SetTexture( kept.BindlessIndex, kept.View, kept.SrgbView );
		if ( kept.StorageIndex >= 0 ) Bindless.SetStorage( kept.StorageIndex, kept.StorageView );
	}
}
