namespace Borealis.Gpu;

/// <summary>Stable scratch roles, independent of GPU debug labels.</summary>
internal enum TransientTextureRole
{
	Bloom,
	BloomDepth,
	ContactShadowMask,
	DepthChain,
	IdsNormals,
	IdsView,
	OcclusionChain,
	ReactiveDepth,
	RefractionStencil,
}

/// <summary>
/// Textures a render borrows and gives back once it's submitted, kept for later renders that want the same, and released
/// after <see cref="IdleRendersBeforeRelease"/> renders unused - as the engine's temporary render targets are. One per
/// render system, so a camera's renders reuse its own.
/// </summary>
internal sealed class TransientTextures : IDisposable
{
	const int IdleRendersBeforeRelease = 8;

	static readonly string[] labels = ["Bloom", "Bloom Depth", "Contact Shadow Mask", "Depth Chain", "Ids Normals", "Ids View", "Occlusion Chain", "Reactive Depth", "Refraction Stencil"];

	readonly record struct Shape( int Width, int Height, int Depth, TextureFormat Format, int Samples, int Mips,
		bool RenderTarget, bool Cube, bool Storage, bool GenerateMips, bool SampledMultisampled )
	{
		public Shape( in TextureDesc desc ) : this( desc.Width, desc.Height, desc.Depth, desc.Format, desc.Samples, desc.Mips,
			desc.RenderTarget, desc.Cube, desc.Storage, desc.GenerateMips, desc.SampledMultisampled )
		{ }
	}

	sealed class Entry
	{
		public Shape Shape;
		public TransientTextureRole Role;
		public GpuTexture Texture;
		public bool Loaned;
		public int Idle;
	}

	readonly List<Entry> entries = new();

	// This render's loan roles and shapes, and the render before it (GraphMeasure).
	int borrowed, lastBorrowed;

	/// <summary>
	/// Borrow uninitialized scratch of this role and shape. It is not lent out again in the same render.
	/// </summary>
	public GpuTexture Get( TransientTextureRole role, in TextureDesc desc )
	{
		if ( !desc.Data.IsEmpty ) throw new ArgumentException( "Transient textures cannot have initial pixels.", nameof( desc ) );
		var shape = new Shape( desc );
		if ( GraphMeasure.Enabled ) borrowed = HashCode.Combine( borrowed, role, shape );

		foreach ( var entry in entries )
		{
			if ( entry.Loaned || entry.Role != role || entry.Shape != shape ) continue;
			entry.Loaned = true;
			entry.Idle = 0;
			return entry.Texture;
		}

		var created = new Entry { Shape = shape, Role = role, Texture = RenderHost.Current.Device.CreateTexture( desc, $"Borealis {labels[(int)role]}" ), Loaned = true };
		entries.Add( created );
		return created.Texture;
	}

	/// <summary>
	/// The render is submitted: give everything back, and release what's gone unused. The device keeps textures alive for
	/// work in flight.
	/// </summary>
	public void EndRender()
	{
		if ( GraphMeasure.Enabled )
		{
			GraphMeasure.AddTargets( borrowed != lastBorrowed );
			lastBorrowed = borrowed;
			borrowed = 0;
		}

		for ( int i = entries.Count - 1; i >= 0; i-- )
		{
			var entry = entries[i];
			if ( entry.Loaned )
			{
				entry.Loaned = false;
				entry.Idle = 0;
				continue;
			}

			if ( ++entry.Idle < IdleRendersBeforeRelease ) continue;

			entry.Texture.Dispose();
			entries.RemoveAt( i );
		}
	}

	public void Dispose()
	{
		foreach ( var entry in entries )
			entry.Texture.Dispose();
		entries.Clear();
	}
}
