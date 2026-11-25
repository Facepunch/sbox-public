using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Sprite sheets on this device: the sheet cache (<see cref="SheetCache"/>) as a texture, which sprite shaders read a sheet
/// texture's frames from by the sheet data <see cref="Texture.SequenceData"/> gives.
/// </summary>
internal sealed partial class BorealisGpuRenderBackend
{
	Texture sheetCache;

	/// <summary>The sheet cache, once a sheet has been written into it.</summary>
	internal Texture SheetCacheTexture => sheetCache;

	/// <summary>The sheet cache and what's in it go (no more frames): its texture is this backend's own.</summary>
	void ReleaseSheets()
	{
		services.SheetCache = null;
		sheetCache?.Dispose();
		sheetCache = null;
		Sheets.Clear();
	}

	/// <summary>
	/// Write the sheets placed since the last block into the sheet cache, and make it what this device's views read sheets
	/// from once it exists. On the main thread, before a block starts recording: sprite systems ask for sheet data from
	/// workers and from the main thread mid-frame, so their rows are written here, never into a list being recorded.
	/// </summary>
	void WriteSheets()
	{
		if ( Sheets.HasWrites )
		{
			sheetCache ??= Texture.Create( SheetCache.Width, SheetCache.Height, ImageFormat.RGBA16161616 ).WithName( "Borealis sheet cache" ).Finish();
			Sheets.TakeWrites( writeSheetRows ??= ( rows, row, height ) => sheetCache.Update( MemoryMarshal.AsBytes( rows.AsSpan() ), 0, row, SheetCache.Width, height ) );
		}

		// Every time: a write this device couldn't take releases its copy, which is made again with a new handle, and the
		// views would go on reading the old one
		if ( sheetCache is null ) return;
		services.SheetCache = BorealisViewOf( sheetCache );
	}

	Action<ushort[], int, int> writeSheetRows;
}
