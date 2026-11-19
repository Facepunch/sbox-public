using Borealis.Source2.Materials;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Sprite sheets on this device: a sheet cache texture laid out as native's (<c>CSheetDataTextureCache</c>), which sprite
/// shaders read a sheet texture's frames from by the sheet data <see cref="Texture.SequenceData"/> gives.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	// Native's cache is 512 wide; 256 sequences of three rows here (native has room for 1024)
	const int SheetCacheWidth = 512, SheetCacheSequences = 256, SheetCacheHeight = SheetCacheSequences * TextureSheet.RowsPerSequence;

	readonly object sheetLock = new();
	readonly Dictionary<string, Vector4> sheetData = new( StringComparer.OrdinalIgnoreCase );
	readonly List<(ushort[] Rows, int Row, int Height)> sheetWrites = new();
	Texture sheetCache;
	int sheetRow;

	/// <summary>The sheet cache, once a sheet has been written into it.</summary>
	internal Texture SheetCacheTexture => sheetCache;

	/// <summary>
	/// <paramref name="texture"/>'s sheet data as <c>GetSheetInfo</c> gives it - the sheet cache's v of its first sequence's
	/// rows, the cache's width, one over its height, and its sequence count - with its sequences written into this device's
	/// sheet cache the first time it's asked for. Zero for a texture with no sheet. Any thread: sprite systems ask from
	/// workers and from the main thread mid-frame, so the rows are kept and written before the next block starts
	/// (<see cref="WriteSheets"/>), never into a list being recorded.
	/// </summary>
	internal Vector4 SheetData( Texture texture )
	{
		if ( texture is null || texture.SequenceCount <= 0 || string.IsNullOrEmpty( texture.ResourcePath ) ) return default;

		var path = texture.ResourcePath;
		lock ( sheetLock )
		{
			if ( sheetData.TryGetValue( path, out var known ) ) return known;

			var sheet = TextureSheet.Read( services.Content.TryRead( path ) );
			if ( sheet is null || sheet.Sequences.Length == 0 ) return sheetData[path] = default;

			var rows = sheet.CacheRows( SheetCacheWidth, out var sequences );
			var height = sequences * TextureSheet.RowsPerSequence;
			if ( height > SheetCacheHeight ) return sheetData[path] = default;

			// Full: start over, as native does, and what was in it is written again when next asked for
			if ( sheetRow + height > SheetCacheHeight )
			{
				Log.Warning( "Borealis.Gpu: the sheet cache is full, starting over" );
				sheetData.Clear();
				sheetRow = 0;
			}

			sheetWrites.Add( (rows, sheetRow, height) );

			var data = new Vector4( (sheetRow + 0.5f) / SheetCacheHeight, SheetCacheWidth, 1.0f / SheetCacheHeight, sequences );
			sheetRow += height;
			return sheetData[path] = data;
		}
	}

	/// <summary>
	/// Write the sheets asked for since the last block into the sheet cache, and make it what this device's views read
	/// sheets from once it exists. On the main thread, before a block starts recording.
	/// </summary>
	void WriteSheets()
	{
		lock ( sheetLock )
		{
			if ( sheetWrites.Count > 0 )
			{
				sheetCache ??= Texture.Create( SheetCacheWidth, SheetCacheHeight, ImageFormat.RGBA16161616 ).WithName( "Borealis sheet cache" ).Finish();
				foreach ( var (rows, row, height) in sheetWrites ) sheetCache.Update( MemoryMarshal.AsBytes( rows.AsSpan() ), 0, row, SheetCacheWidth, height );
				sheetWrites.Clear();
			}
		}

		// Every time: a write this device couldn't take releases its copy, which is made again with a new handle, and the
		// views would go on reading the old one
		if ( sheetCache is null ) return;
		services.SheetCache = BorealisViewOf( sheetCache );
	}
}
