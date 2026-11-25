using NativeEngine;

namespace Sandbox.Engine;

/// <summary>
/// Owns an SDL window. The caller supplies window policy, rendering and disposal timing; a device draws into it through a
/// swap chain of its own (<see cref="BorealisGpuWindow"/>).
/// </summary>
[SkipHotload]
internal sealed class SdlWindow : IDisposable
{
	internal IntPtr Handle { get; private set; }
	internal Sdl.WindowFlags Flags => Sdl.GetWindowFlags( Handle );
	internal bool IsFocused => (Flags & Sdl.WindowFlags.InputFocus) != 0;


	/// <summary>
	/// Take ownership of an SDL window on the main thread.
	/// </summary>
	internal SdlWindow( IntPtr handle )
	{
		ThreadSafe.AssertIsMainThread();
		if ( handle == IntPtr.Zero )
			throw new InvalidOperationException( $"Couldn't create the SDL window: {Sdl.GetError()}" );
		Handle = handle;
	}

	internal string Title
	{
		set => WarnIfFailed( Sdl.SetWindowTitle( Handle, value ?? "" ) );
	}

	byte[] titleBytes;

	/// <summary>
	/// Set the title from <paramref name="text"/> without making a string of it: it's encoded into a kept buffer, for a title
	/// that changes several times a second (<see cref="BorealisGpuTitle"/>).
	/// </summary>
	internal unsafe void SetTitle( System.Text.StringBuilder text )
	{
		var length = 1;
		foreach ( var chunk in text.GetChunks() ) length += System.Text.Encoding.UTF8.GetByteCount( chunk.Span );
		if ( titleBytes is null || titleBytes.Length < length ) titleBytes = new byte[Math.Max( length, 512 )];

		var written = 0;
		foreach ( var chunk in text.GetChunks() ) written += System.Text.Encoding.UTF8.GetBytes( chunk.Span, titleBytes.AsSpan( written ) );
		titleBytes[written] = 0;

		fixed ( byte* title = titleBytes )
			WarnIfFailed( Sdl.__N.globalSdl_SetWindowTitle( Handle, (IntPtr)title ) != 0 );
	}

	internal Vector2 PixelSize
	{
		get
		{
			Check( Sdl.GetWindowSizeInPixels( Handle, out var width, out var height ) );
			return new Vector2( width, height );
		}
	}

	internal bool Show() => WarnIfFailed( Sdl.ShowWindow( Handle ) );
	internal bool Sync() => WarnIfFailed( Sdl.SyncWindow( Handle ) );

	/// <summary>
	/// Apply fullscreen at a frame boundary. Zero dimensions select desktop fullscreen. A timeout
	/// leaves the request pending; callers must retry before sizing against the restored desktop.
	/// </summary>
	internal unsafe bool SetFullscreen( bool fullscreen, int width = 0, int height = 0, float refreshRate = 0 )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !Sync() ) return false;

		if ( fullscreen )
		{
			Sdl.DisplayMode mode = default;
			var exclusive = width > 0 && height > 0 && Sdl.GetClosestFullscreenDisplayMode( SdlDisplay.ForWindow( Handle ), width, height, refreshRate, false, out mode );
			// A saved resolution can become unavailable after changing displays. Null selects desktop fullscreen.
			Check( Sdl.SetWindowFullscreenMode( Handle, exclusive ? (IntPtr)(&mode) : IntPtr.Zero ) );
			// Exclusive modes can change in place, without restoring the desktop first.
			Check( Sdl.SetWindowFullscreen( Handle, true ) );
		}
		else
		{
			if ( (Flags & Sdl.WindowFlags.Fullscreen) != 0 )
			{
				Check( Sdl.SetWindowFullscreen( Handle, false ) );
				if ( !Sync() ) return false;
			}
			Check( Sdl.SetWindowFullscreenMode( Handle, IntPtr.Zero ) );
		}

		return Sync();
	}

	/// <summary>
	/// Request relative mouse mode on the main thread and return SDL's actual state, including on failure.
	/// </summary>
	internal static bool SetRelativeMouseMode( IntPtr window, bool relative )
	{
		if ( window == IntPtr.Zero ) return false;
		if ( Sdl.GetWindowRelativeMouseMode( window ) != relative )
			WarnIfFailed( Sdl.SetWindowRelativeMouseMode( window, relative ) );
		return Sdl.GetWindowRelativeMouseMode( window );
	}

	internal static void Check( bool success )
	{
		if ( !success ) throw new InvalidOperationException( $"SDL window: {Sdl.GetError()}" );
	}

	internal static bool WarnIfFailed( bool success )
	{
		if ( !success ) Log.Warning( $"SDL window: {Sdl.GetError()}" );
		return success;
	}

	/// <summary>
	/// Destroy the SDL window, on the main thread, after the swap chain drawing into it.
	/// </summary>
	public void Dispose()
	{
		ThreadSafe.AssertIsMainThread();
		if ( Handle == IntPtr.Zero ) return;

		Sdl.DestroyWindow( Handle );
		Handle = IntPtr.Zero;
	}
}
