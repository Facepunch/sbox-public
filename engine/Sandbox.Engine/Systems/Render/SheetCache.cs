using Borealis.Source2.Materials;

namespace Sandbox;

/// <summary>
/// Where sprite sheets go in a device's sheet cache texture, laid out as native's (<c>CSheetDataTextureCache</c>): each
/// sheet's rows, placed the first time its sheet data is asked for, and the rows still to be written into the texture.
/// Sprite shaders read a sheet texture's frames from it by <see cref="Texture.SequenceData"/>. Any thread.
/// </summary>
internal sealed class SheetCache
{
	// Native's cache is 512 wide; 256 sequences of three rows here (native has room for 1024)
	public const int Width = 512, Sequences = 256, Height = Sequences * TextureSheet.RowsPerSequence;

	readonly System.Threading.Lock gate = new();
	readonly Dictionary<object, Vector4> placed = new();
	readonly List<(ushort[] Rows, int Row, int Height)> writes = new();
	int row;

	/// <summary>
	/// <paramref name="texture"/>'s sheet data as <c>GetSheetInfo</c> gives it - the cache's v of its first sequence's rows,
	/// the cache's width, one over its height, and its sequence count - with its rows placed in the cache the first time.
	/// Zero for a texture with no sheet.
	/// </summary>
	public Vector4 SheetData( Texture texture )
	{
		var sheet = texture?.Sheet;
		if ( sheet is null || sheet.Sequences.Length == 0 ) return default;

		// A loaded texture's sheet is its file's, shared by every texture of that file
		object key = string.IsNullOrEmpty( texture.ResourcePath ) || texture.MadeAtRunTime ? sheet : texture.ResourcePath.ToLowerInvariant();
		lock ( gate )
		{
			if ( placed.TryGetValue( key, out var known ) ) return known;

			var rows = sheet.CacheRows( Width, out var sequences );
			var height = sequences * TextureSheet.RowsPerSequence;
			if ( height > Height ) return placed[key] = default;

			// Full: start over, as native does, and what was in it is placed again when next asked for
			if ( row + height > Height )
			{
				Log.Warning( "The sheet cache is full, starting over" );
				placed.Clear();
				row = 0;
			}

			writes.Add( (rows, row, height) );

			var data = new Vector4( (row + 0.5f) / Height, Width, 1.0f / Height, sequences );
			row += height;
			return placed[key] = data;
		}
	}

	/// <summary>Hand the rows placed since the last call to <paramref name="write"/> (row, height, texels), in order.</summary>
	public void TakeWrites( Action<ushort[], int, int> write )
	{
		lock ( gate )
		{
			foreach ( var (rows, at, height) in writes ) write( rows, at, height );
			writes.Clear();
		}
	}

	/// <summary>Whether rows are waiting to be written.</summary>
	public bool HasWrites
	{
		get
		{
			lock ( gate ) return writes.Count > 0;
		}
	}

	/// <summary>Forget every placement: the texture they were written into has gone.</summary>
	public void Clear()
	{
		lock ( gate )
		{
			placed.Clear();
			writes.Clear();
			row = 0;
		}
	}
}
