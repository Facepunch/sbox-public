using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

/// <summary>
/// Owns the process's SDL system and custom cursors on the main thread.
/// </summary>
[SkipHotload]
internal static class SdlCursors
{
	static readonly Dictionary<Sdl.SystemCursor, IntPtr> system = new();
	static readonly Dictionary<string, IntPtr> custom = new( StringComparer.OrdinalIgnoreCase );
	static readonly Dictionary<IntPtr, (byte[] Pixels, int Width, int Height, int HotX, int HotY)> editorCursors = new();
	static IntPtr selected;
	static bool initialized, temporaryCursor;

	static readonly CaseInsensitiveDictionary<Sdl.SystemCursor> CursorLookup = new()
	{
		{ "arrow", Sdl.SystemCursor.Default },
		{ "ibeam", Sdl.SystemCursor.Text },
		{ "crosshair", Sdl.SystemCursor.Crosshair },
		{ "hand", Sdl.SystemCursor.Pointer },
		{ "progress", Sdl.SystemCursor.Progress },
		{ "wait", Sdl.SystemCursor.Wait },
		{ "move", Sdl.SystemCursor.Move },
		{ "sizenesw", Sdl.SystemCursor.NeswResize },
		{ "sizenwse", Sdl.SystemCursor.NwseResize },
		{ "sizewe", Sdl.SystemCursor.EwResize },
		{ "sizens", Sdl.SystemCursor.NsResize },
		{ "not-allowed", Sdl.SystemCursor.NotAllowed },
	};

	static readonly CaseInsensitiveDictionary<string> CursorAliases = new()
	{
		{ "text", "ibeam" },
		{ "pointer", "hand" },
		{ "hourglass", "wait" },
		{ "nesw-resize", "sizenesw" },
		{ "nwse-resize", "sizenwse" },
		{ "ew-resize", "sizewe" },
		{ "col-resize", "sizewe" },
		{ "ns-resize", "sizens" },
		{ "row-resize", "sizens" },
	};

	/// <summary>
	/// Select a CSS cursor name, resolving aliases before custom cursors. Unknown names use the arrow.
	/// </summary>
	internal static void SetCursor( string name, bool allowCustom = true )
	{
		if ( !initialized ) return;
		name = string.IsNullOrWhiteSpace( name ) ? "arrow" : name;
		if ( name.Equals( "none", StringComparison.OrdinalIgnoreCase ) )
		{
			Select( IntPtr.Zero );
			return;
		}
		if ( CursorAliases.TryGetValue( name, out var canonical ) ) name = canonical;
		if ( !allowCustom || !custom.TryGetValue( name, out var cursor ) )
			cursor = GetSystemCursor( CursorLookup.TryGetValue( name, out var type ) ? type : Sdl.SystemCursor.Default );
		if ( cursor != IntPtr.Zero ) Select( cursor );
	}

	static void Select( IntPtr cursor )
	{
		if ( selected == cursor && !temporaryCursor ) return;
		selected = cursor;
		Restore();
	}

	internal static void Initialize()
	{
		initialized = true;
		SetCursor( "arrow", allowCustom: false );
	}

	static IntPtr GetSystemCursor( Sdl.SystemCursor type )
	{
		if ( system.TryGetValue( type, out var cursor ) ) return cursor;
		cursor = Sdl.CreateSystemCursor( type );
		if ( cursor != IntPtr.Zero ) system.Add( type, cursor );
		return cursor;
	}

	internal static bool HasUserCursor( string name ) => custom.ContainsKey( name );

	internal static void Restore()
	{
		if ( !initialized ) return;
		temporaryCursor = false;
		if ( editorCursors.TryGetValue( selected, out var image ) )
			IToolsDll.Current?.SetGameCursor( selected, image.Pixels, image.Width, image.Height, image.HotX, image.HotY );
		else
			IToolsDll.Current?.SetGameCursor( IntPtr.Zero, default, 0, 0, 0, 0 );
		Sdl.SetCursor( selected );
		if ( selected == IntPtr.Zero ) Sdl.HideCursor();
		else Sdl.ShowCursor();
		// SDL skips unchanged cursors; null forces a redraw after Qt replaces the native cursor.
		Sdl.SetCursor( IntPtr.Zero );
	}

	internal static void ShowArrow()
	{
		if ( !initialized ) return;
		temporaryCursor = true;
		IToolsDll.Current?.SetGameCursor( IntPtr.Zero, default, 0, 0, 0, 0 );
		Sdl.SetCursor( GetSystemCursor( Sdl.SystemCursor.Default ) );
		Sdl.ShowCursor();
	}

	internal static bool LoadCursorFromFile( string path, string name, int hotX, int hotY )
	{
		if ( !initialized ) return false;
		if ( custom.ContainsKey( name ) ) return true;
		var native = FloatBitMap_t.Create();
		using var bitmap = new FloatBitmap( native );
		if ( !native.LoadFromFile( path, FBMGammaType_t.FBM_GAMMA_LINEAR ) ) return false;
		var pixels = bitmap.EncodeTo( ImageFormat.RGBA8888 );
		return pixels is not null && CreateCursor( name, pixels, bitmap.Width, bitmap.Height, hotX, hotY );
	}

	internal static unsafe bool CreateCursor( string name, ReadOnlySpan<byte> pixels, int width, int height, int hotX, int hotY )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !initialized || width <= 0 || height <= 0 || pixels.Length != (long)width * height * 4 ) return false;
		if ( custom.ContainsKey( name ) ) return true;
		fixed ( byte* data = pixels )
		{
			// SDL_PIXELFORMAT_RGBA32 describes byte order, independent of host endianness.
			var format = Sdl.PixelFormatRgba32;
			var surface = Sdl.CreateSurfaceFrom( width, height, format, (IntPtr)data, width * 4 );
			if ( surface == IntPtr.Zero ) return false;
			try
			{
				hotX = Math.Clamp( hotX, 0, width - 1 );
				hotY = Math.Clamp( hotY, 0, height - 1 );
				var cursor = Sdl.CreateColorCursor( surface, hotX, hotY );
				if ( cursor == IntPtr.Zero ) return false;
				custom.Add( name, cursor );
				if ( IToolsDll.Current is not null ) editorCursors.Add( cursor, (pixels.ToArray(), width, height, hotX, hotY) );
				return true;
			}
			finally { Sdl.DestroySurface( surface ); }
		}
	}

	internal static void ShutdownUserCursors()
	{
		if ( custom.Values.Contains( selected ) ) SetCursor( "arrow", allowCustom: false );
		foreach ( var cursor in custom.Values ) Sdl.DestroyCursor( cursor );
		custom.Clear();
		editorCursors.Clear();
	}

	internal static void Shutdown()
	{
		if ( !initialized ) return;
		IToolsDll.Current?.SetGameCursor( IntPtr.Zero, default, 0, 0, 0, 0 );
		Sdl.SetCursor( IntPtr.Zero );
		selected = IntPtr.Zero;
		ShutdownUserCursors();
		foreach ( var cursor in system.Values ) Sdl.DestroyCursor( cursor );
		system.Clear();
		initialized = false;
	}
}
