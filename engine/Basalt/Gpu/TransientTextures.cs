namespace Basalt.Gpu;

/// <summary>
/// Textures a render borrows and gives back once it's submitted, kept for later renders that want the same, and released
/// after <see cref="IdleRendersBeforeRelease"/> renders unused - as the engine's temporary render targets are. One per
/// render system, so a camera's renders reuse its own.
/// </summary>
internal sealed class TransientTextures : IDisposable
{
	const int IdleRendersBeforeRelease = 8;

	sealed class Entry
	{
		public TextureDesc Desc;
		public string Name;
		public GpuTexture Texture;
		public bool Loaned;
		public int Idle;
	}

	readonly List<Entry> entries = new();

	/// <summary>
	/// A texture like <paramref name="desc"/> for this render: one no earlier part of it borrowed, or a new one.
	/// </summary>
	public GpuTexture Get( in TextureDesc desc, string name )
	{
		foreach ( var entry in entries )
		{
			if ( entry.Loaned || entry.Name != name || !Same( entry.Desc, desc ) ) continue;
			entry.Loaned = true;
			entry.Idle = 0;
			return entry.Texture;
		}

		var created = new Entry { Desc = desc, Name = name, Texture = RenderHost.Current.Device.CreateTexture( desc, $"Basalt {name}" ), Loaned = true };
		entries.Add( created );
		return created.Texture;
	}

	static bool Same( in TextureDesc a, in TextureDesc b ) => a.Width == b.Width && a.Height == b.Height && a.Depth == b.Depth && a.Format == b.Format
		&& a.Samples == b.Samples && a.Mips == b.Mips && a.RenderTarget == b.RenderTarget && a.Storage == b.Storage && a.GenerateMips == b.GenerateMips;

	/// <summary>
	/// The render is submitted: give everything back, and release what's gone unused. The device keeps textures alive for
	/// work in flight.
	/// </summary>
	public void EndRender()
	{
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
