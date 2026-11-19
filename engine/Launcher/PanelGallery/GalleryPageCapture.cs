using static Sandbox.Internal.GlobalGameNamespace;
using System.Runtime.InteropServices;

namespace Sandbox.PanelGallery;

/// <summary>
/// <c>-capture-pages folder [-pages a;b] [-page-frames N]</c>: opens each gallery page in turn (or the ones named, by
/// title or <c>Folder/Title</c>), lets it run <c>-page-frames</c> frames (40), then draws the window's UI into a texture
/// on the process's device as a frame of the window draws it, reads it back and saves <c>folder/Page.png</c>. Then it
/// exits. <c>results.txt</c> lists each page's frame time (the loop's pace over the second half of its frames).
/// </summary>
internal sealed class GalleryPageCapture
{
	readonly string folder;
	readonly GalleryPageInfo[] pages;
	readonly int frames;
	int index, frame;

	// The frame loop's own pace over the second half of each page's frames: what a frame of the window costs
	long halfway;

	public static GalleryPageCapture FromCommandLine()
	{
		var folder = Arg( "-capture-pages" );
		if ( folder is null ) return null;

		var names = Arg( "-pages" )?.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var pages = names is null ? GalleryPageInfo.All : GalleryPageInfo.All.Where( p => names.Any( n => n.Equals( p.Title, StringComparison.OrdinalIgnoreCase ) || n.Equals( $"{p.Folder}/{p.Title}", StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
		return new GalleryPageCapture( System.IO.Path.GetFullPath( folder ), pages, int.TryParse( Arg( "-page-frames" ), out var n ) ? n : 40 );
	}

	GalleryPageCapture( string folder, GalleryPageInfo[] pages, int frames )
	{
		this.folder = folder;
		this.pages = pages;
		this.frames = Math.Max( 2, frames );
		System.IO.Directory.CreateDirectory( folder );
		GalleryWindow.Capturing = true;
	}

	/// <summary>After each frame. False once every page is saved.</summary>
	public bool Frame( PanelWindow window )
	{
		if ( index >= pages.Length ) return false;
		if ( window?.Core is not { } core || core.Surface is null ) return true;

		var gallery = window.Root.Children.OfType<GalleryWindow>().FirstOrDefault();
		if ( gallery is null ) return true;

		// Not paced down to the idle rate while nobody's using it
		core.AlwaysFullFrameRate = true;

		var page = pages[index];
		if ( frame++ == 0 ) gallery.Open( page );
		if ( frame == frames / 2 ) halfway = System.Diagnostics.Stopwatch.GetTimestamp();
		if ( frame < frames ) return true;

		var frameMs = System.Diagnostics.Stopwatch.GetElapsedTime( halfway ).TotalMilliseconds / (frames - frames / 2);

		var size = core.Surface.Size;
		var name = string.Concat( $"{page.Folder}_{page.Title}".Select( c => char.IsLetterOrDigit( c ) ? c : '_' ) );
		var pixels = core.RenderToPixels( RenderBackend.Default );
		Save( pixels, size, $"{name}.png" );

		var line = $"{page.Folder}/{page.Title}	{frameMs:0.000} ms";
		System.IO.File.AppendAllText( System.IO.Path.Combine( folder, "results.txt" ), line + "\n" );
		Log.Info( $"Captured page {index + 1} of {pages.Length}: {line}" );

		index++;
		frame = 0;
		return index < pages.Length;
	}

	void Save( Color32[] pixels, Vector2 size, string file )
	{
		using var bitmap = new Bitmap( (int)size.x, (int)size.y );
		bitmap.SetPixelData( MemoryMarshal.AsBytes( pixels.AsSpan() ), premultiplied: false );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( folder, file ), bitmap.ToPng() );
	}

	static string Arg( string name )
	{
		var args = Environment.GetCommandLineArgs();
		var i = Array.FindIndex( args, x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}
}
