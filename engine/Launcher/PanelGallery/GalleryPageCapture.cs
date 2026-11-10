using static Sandbox.Internal.GlobalGameNamespace;
using System.Runtime.InteropServices;

namespace Sandbox.PanelGallery;

/// <summary>
/// <c>-capture-pages folder [-pages a;b] [-page-frames N]</c>: opens each gallery page in turn (or the ones named, by
/// title or <c>Folder/Title</c>), lets it run <c>-page-frames</c> frames (40), then draws the window's UI into a texture
/// on the process's device as a frame of the window draws it, reads it back and saves <c>folder/Page.png</c>. Then it
/// exits. With <c>-capture-basalt</c> (on Source 2) the same frame is also drawn on a Basalt.Gpu device of its own, the
/// panels recorded again for it, through <c>-basaltgpu</c>'s window path (<c>Page.basalt.png</c>), and <c>results.txt</c>
/// lists each page's frame time (the loop's pace over the second half of its frames), maximum channel difference and
/// pixels over 2. With <c>-basaltgpu</c> the pages are Basalt.Gpu's.
/// Source 2's device can't draw in a <c>-basaltgpu</c> process: nothing presents on it, so its resources never finish
/// loading.
internal sealed class GalleryPageCapture
{
	readonly string folder;
	readonly GalleryPageInfo[] pages;
	readonly int frames;
	readonly BasaltGpuRenderBackend basalt;
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

		if ( Environment.GetCommandLineArgs().Any( x => x.Equals( "-capture-basalt", StringComparison.OrdinalIgnoreCase ) ) && RenderBackend.Default is Source2RenderBackend )
		{
			basalt = BasaltGpuRenderBackend.CreateBeside( Environment.GetCommandLineArgs().Any( x => x.Equals( "-basaltgpu-validation", StringComparison.OrdinalIgnoreCase ) ) );
		}
	}

	/// <summary>After each frame. False once every page is saved.</summary>
	public bool Frame( PanelWindow window )
	{
		if ( index >= pages.Length ) return Finish();
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
		if ( basalt is not null )
		{
			var other = core.RenderToPixels( basalt );
			basalt.WaitIdle();
			basalt.NextFrame();
			Save( other, size, $"{name}.basalt.png" );

			int max = 0, over = 0;
			for ( int i = 0; i < pixels.Length; i++ )
			{
				var a = pixels[i];
				var b = other[i];
				var d = Math.Max( Math.Max( Math.Abs( a.r - b.r ), Math.Abs( a.g - b.g ) ), Math.Max( Math.Abs( a.b - b.b ), Math.Abs( a.a - b.a ) ) );
				max = Math.Max( max, d );
				if ( d > 2 ) over++;
			}

			line += $"\t{max}\t{over}";
		}

		System.IO.File.AppendAllText( System.IO.Path.Combine( folder, "results.txt" ), line + "\n" );
		Log.Info( $"Captured page {index + 1} of {pages.Length}: {line}" );

		index++;
		frame = 0;
		return index < pages.Length || Finish();
	}

	bool Finish()
	{
		if ( basalt is not null )
		{
			System.IO.File.AppendAllText( System.IO.Path.Combine( folder, "results.txt" ), $"Basalt.Gpu validation errors {basalt.ValidationErrors}, textures copied from Source 2 {basalt.CopiedFromSource2}\n" );
			basalt.Dispose();
		}

		return false;
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
