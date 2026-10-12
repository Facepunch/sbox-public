using System.Diagnostics;
using SkiaSharp;
using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.Utility;

namespace Sandbox.Engine;

/// <summary>
/// CPU-only startup splash. It owns a plain SDL window and software bitmaps until normal rendering takes over.
/// </summary>
[SkipHotload]
internal sealed class GameStartupWindow : IDisposable
{
	const string LogoPath = "/core/materials/startup_background_color.png";
	const string BackdropPath = "/core/materials/startup_backdrop_color.png";

	readonly bool standalone;
	Bitmap logo;
	Bitmap backdrop;
	Bitmap scaledBackdrop;
	Bitmap framebuffer;
	Bitmap statusImage;
	string statusImageText;
	int statusImageMaxWidth;
	float statusImageScale;
	string status = "Initializing engine";
	int framebufferWidth;
	int framebufferHeight;
	int backdropWidth;
	int backdropHeight;
	float progress = -1;
	bool isOpen = true;
	bool drawFailed;
	bool softwareSurfaceAvailable;
	bool pollingEvents;

	internal IntPtr Handle { get; private set; }
	internal float Progress => progress;
	internal string Status => status;
	internal bool IsOpen => Handle != IntPtr.Zero && isOpen;

	internal GameStartupWindow( string title )
	{
		ThreadSafe.AssertIsMainThread();
		if ( OperatingSystem.IsLinux() ) Sdl.SetHint( "SDL_VIDEO_X11_NET_WM_PING", "0" );

		standalone = Application.IsStandalone;
		var mode = GameWindow.VideoMode.FromCommandLine( RenderSettings.Instance );
		var requested = mode.Window;
		var borderless = requested.Fullscreen || requested.Borderless;
		var bounds = SdlDisplay.GetBounds( SdlDisplay.Primary, usable: !borderless );
		if ( bounds.Width <= 0 || bounds.Height <= 0 )
			throw new InvalidOperationException( "Couldn't determine primary display bounds for the startup window" );

		var size = requested.Fullscreen ? bounds.Size : requested.GetSize( bounds.Size );
		var width = Math.Max( (int)size.x, 1 );
		var height = Math.Max( (int)size.y, 1 );
		var flags = Sdl.WindowFlags.Hidden | (borderless ? Sdl.WindowFlags.Borderless : Sdl.WindowFlags.Resizable);

		Handle = Sdl.CreateStartupWindow( title, width, height, flags );
		if ( Handle == IntPtr.Zero )
			throw new InvalidOperationException( $"Couldn't create the startup SDL window: {Sdl.GetError()}" );

		try
		{
			var x = (int)bounds.Left + ((int)bounds.Width - width) / 2;
			var y = (int)bounds.Top + ((int)bounds.Height - height) / 2;
			if ( CommandLine.HasSwitch( "-x" ) ) x = CommandLine.GetSwitchInt( "-x", x );
			if ( CommandLine.HasSwitch( "-y" ) ) y = CommandLine.GetSwitchInt( "-y", y );

			SdlWindow.Check( Sdl.SetWindowPosition( Handle, x, y ) );
			SdlWindow.Check( Sdl.SetWindowBordered( Handle, !borderless ) );
			SdlWindow.Check( Sdl.SetWindowResizable( Handle, true ) );
			SdlWindow.Check( Sdl.SyncWindow( Handle ) );
			if ( !Sdl.FillWindowColor( Handle, 27, 36, 51, 255 ) )
			{
				Log.Warning( $"Couldn't fill the startup window: {Sdl.GetError()}" );
				drawFailed = true;
				return;
			}

			softwareSurfaceAvailable = true;
			SdlWindow.Check( Sdl.ShowWindow( Handle ) );
			SdlWindow.Check( Sdl.SyncWindow( Handle ) );

			logo = LoadBitmap( LogoPath, "startup logo" );
			if ( !standalone ) backdrop = LoadBitmap( BackdropPath, "startup backdrop" );

			UpdateProgress( 0, status );
			if ( !isOpen ) return;
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	static Bitmap LoadBitmap( string path, string label )
	{
		try
		{
			if ( EngineFileSystem.Root is null || !EngineFileSystem.Root.FileExists( path ) )
			{
				Log.Warning( $"Startup {label} not found: {path}" );
				return null;
			}

			var bitmap = Bitmap.CreateFromBytes( EngineFileSystem.Root.ReadAllBytes( path ).ToArray() );
			if ( bitmap is null || !bitmap.IsValid )
			{
				bitmap?.Dispose();
				Log.Warning( $"Couldn't load startup {label}: {path}" );
				return null;
			}

			return bitmap;
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Couldn't load startup {label}: {path}" );
			return null;
		}
	}

	/// <summary>Refresh the splash when progress advances, while servicing early window and quit events.</summary>
	internal void UpdateProgress( float value, string newStatus )
	{
		ThreadSafe.AssertIsMainThread();
		if ( Handle == IntPtr.Zero ) return;

		PumpEvents();
		if ( !IsOpen || float.IsNaN( value ) ) return;

		value = Math.Clamp( value, 0, 1 );
		newStatus ??= string.Empty;
		if ( value < progress || (value == progress && newStatus == status) ) return;

		progress = value;
		status = newStatus;
		Draw();
	}

	internal static Bitmap CreateStatusBitmap( string text, int maxWidth, float scale )
	{
		if ( string.IsNullOrWhiteSpace( text ) || maxWidth <= 0 ) return null;

		var paddingX = Math.Clamp( (int)(10 * scale), 5, 12 );
		var paddingY = Math.Clamp( (int)(4 * scale), 2, 6 );
		using var font = new SKFont( SKTypeface.Default, Math.Clamp( 14 * scale, 10, 18 ) );
		using var paint = new SKPaint
		{
			Color = new SKColor( 244, 247, 252 ),
			IsAntialias = true
		};

		var availableWidth = Math.Max( 1, maxWidth - paddingX * 2 );
		var textWidth = font.MeasureText( text );
		if ( textWidth > availableWidth )
		{
			font.Size = Math.Max( 7, font.Size * availableWidth / textWidth );
			textWidth = font.MeasureText( text );
		}

		var metrics = font.Metrics;
		var textHeight = metrics.Descent - metrics.Ascent;
		var bitmapWidth = Math.Clamp( (int)Math.Ceiling( textWidth + paddingX * 2 ), 1, maxWidth );
		var bitmapHeight = Math.Max( 1, (int)Math.Ceiling( textHeight + paddingY * 2 ) );
		var bitmap = new SKBitmap( bitmapWidth, bitmapHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul );

		try
		{
			using var canvas = new SKCanvas( bitmap );
			canvas.Clear( SKColors.Transparent );
			using var background = new SKPaint { Color = new SKColor( 13, 18, 26 ), IsAntialias = true };
			canvas.DrawRoundRect( new SKRect( 0, 0, bitmapWidth, bitmapHeight ), bitmapHeight / 2f, bitmapHeight / 2f, background );
			canvas.DrawText( text, bitmapWidth / 2f, paddingY - metrics.Ascent, SKTextAlign.Center, font, paint );
			return new Bitmap( bitmap );
		}
		catch
		{
			bitmap.Dispose();
			throw;
		}
	}

	internal bool CanTransferToVulkan => Handle != IntPtr.Zero && isOpen && softwareSurfaceAvailable;

	internal IntPtr TransferToVulkan()
	{
		if ( !CanTransferToVulkan ) return IntPtr.Zero;
		// Destroy SDL's software framebuffer before SDL_Vulkan_CreateSurface promotes this same window.
		if ( Sdl.WindowHasSurface( Handle ) && !Sdl.DestroyWindowSurface( Handle ) )
		{
			Log.Warning( $"Couldn't release startup software surface: {Sdl.GetError()}" );
			softwareSurfaceAvailable = false;
			return IntPtr.Zero;
		}

		var handle = Handle;
		Handle = IntPtr.Zero;
		isOpen = false;
		softwareSurfaceAvailable = false;
		DisposeImages();
		return handle;
	}

	unsafe void PumpEvents()
	{
		if ( pollingEvents ) return;
		pollingEvents = true;
		try
		{
			var started = Stopwatch.GetTimestamp();
			while ( Sdl.PollEvent( out var e ) )
			{
				if ( e.Type == Sdl.EventType.Quit )
				{
					Application.Exit();
					break;
				}

				var eventWindow = Sdl.GetWindowFromEvent( (IntPtr)(&e) );
				if ( eventWindow == Handle )
				{
					if ( e.Type == Sdl.EventType.WindowCloseRequested )
					{
						isOpen = false;
						Application.Exit();
					}
					else if ( e.Type is Sdl.EventType.WindowExposed or Sdl.EventType.WindowResized or Sdl.EventType.WindowPixelSizeChanged or Sdl.EventType.WindowRestored )
					{
						Draw();
					}
				}
				else if ( GameWindow.Current is not null )
				{
					try { SdlEvents.Dispatch( e ); }
					catch ( Exception exception ) { Log.Warning( exception, $"SDL event {e.Type} failed" ); }
				}

				if ( Stopwatch.GetElapsedTime( started ).TotalMilliseconds > 5 ) break;
			}
		}
		finally
		{
			pollingEvents = false;
		}
	}

	void Draw()
	{
		if ( drawFailed || Handle == IntPtr.Zero || !isOpen ) return;

		try
		{
			if ( !Sdl.GetWindowSizeInPixels( Handle, out var width, out var height ) )
				throw new InvalidOperationException( $"Couldn't get startup window pixel size: {Sdl.GetError()}" );
			if ( width <= 0 || height <= 0 ) return;

			if ( framebuffer is null || framebufferWidth != width || framebufferHeight != height )
			{
				framebuffer?.Dispose();
				framebuffer = new Bitmap( width, height );
				framebufferWidth = width;
				framebufferHeight = height;
				DiscardScaledBackdrop();
			}

			framebuffer.Clear( Color.Black );
			framebuffer.SetFill( Color.White );
			if ( backdrop is not null )
			{
				PrepareBackdrop( width, height );
				framebuffer.DrawBitmap( scaledBackdrop, new Rect( 0, 0, width, height ) );
			}

			if ( logo is not null )
			{
				var scale = standalone ? height / 1080.0f : 1.0f;
				scale = Math.Min( scale, Math.Min( width / (float)logo.Width, height / (float)logo.Height ) );
				var logoWidth = Math.Max( 1, (int)(logo.Width * scale + 0.5f) );
				var logoHeight = Math.Max( 1, (int)(logo.Height * scale + 0.5f) );
				framebuffer.DrawBitmap( logo, new Rect( (width - logoWidth) / 2, (height - logoHeight) / 2, logoWidth, logoHeight ) );
			}

			DrawProgress( width, height );
			unsafe
			{
				fixed ( byte* pixels = framebuffer.GetBuffer() )
				{
					if ( !Sdl.PresentWindowPixels( Handle, width, height, (IntPtr)pixels, checked(width * 4) ) )
						throw new InvalidOperationException( $"Couldn't present startup window pixels: {Sdl.GetError()}" );
				}
			}
		}
		catch ( Exception exception )
		{
			drawFailed = true;
			Log.Warning( exception, "Couldn't draw the startup image" );
		}
	}

	void PrepareBackdrop( int width, int height )
	{
		if ( scaledBackdrop is not null && backdropWidth == width && backdropHeight == height ) return;

		DiscardScaledBackdrop();
		var sourceAspect = backdrop.Width / (float)backdrop.Height;
		var targetAspect = width / (float)height;
		float cropX = 0, cropY = 0, cropWidth = backdrop.Width, cropHeight = backdrop.Height;
		if ( sourceAspect > targetAspect )
		{
			cropWidth = backdrop.Height * targetAspect;
			cropX = (backdrop.Width - cropWidth) * 0.5f;
		}
		else
		{
			cropHeight = backdrop.Width / targetAspect;
			cropY = (backdrop.Height - cropHeight) * 0.5f;
		}

		using var cropped = backdrop.Crop( new Rect( cropX, cropY, cropWidth, cropHeight ) );
		scaledBackdrop = cropped.Resize( width, height );
		backdropWidth = width;
		backdropHeight = height;
	}

	void DrawProgress( int width, int height )
	{
		var scale = height / 1080.0f;
		var barWidth = Math.Clamp( (int)(320 * scale), 1, width );
		var barHeight = Math.Clamp( (int)(4 * scale), 1, height );
		var barX = (width - barWidth) / 2;
		var barY = Math.Max( 0, height - (int)(48 * scale) - barHeight );
		var statusMaxWidth = Math.Max( 1, width - (int)(32 * scale) );

		if ( statusImage is null || statusImageText != status || statusImageMaxWidth != statusMaxWidth || statusImageScale != scale )
		{
			statusImage?.Dispose();
			statusImage = CreateStatusBitmap( status, statusMaxWidth, scale );
			statusImageText = status;
			statusImageMaxWidth = statusMaxWidth;
			statusImageScale = scale;
		}

		if ( statusImage is not null )
		{
			framebuffer.SetFill( Color.White );
			var statusX = (width - statusImage.Width) / 2;
			var statusY = Math.Max( 0, barY - statusImage.Height - Math.Max( 4, (int)(10 * scale) ) );
			framebuffer.DrawBitmap( statusImage, new Rect( statusX, statusY, statusImage.Width, statusImage.Height ) );
		}

		framebuffer.SetAntialias( false );
		framebuffer.SetFill( new Color( 0.05f, 0.065f, 0.09f, 1 ) );
		framebuffer.DrawRect( new Rect( barX, barY, barWidth, barHeight ) );
		var filledWidth = (int)(barWidth * progress);
		if ( filledWidth <= 0 ) return;

		framebuffer.SetFill( new Color( 0.88f, 0.009f, 0.015f, 1 ) );
		framebuffer.DrawRect( new Rect( barX, barY, filledWidth, barHeight ) );
	}

	void DiscardScaledBackdrop()
	{
		scaledBackdrop?.Dispose();
		scaledBackdrop = null;
		backdropWidth = 0;
		backdropHeight = 0;
	}

	void DisposeImages()
	{
		logo?.Dispose();
		logo = null;
		backdrop?.Dispose();
		backdrop = null;
		DiscardScaledBackdrop();
		framebuffer?.Dispose();
		framebuffer = null;
		framebufferWidth = 0;
		framebufferHeight = 0;
		statusImage?.Dispose();
		statusImage = null;
		statusImageText = null;
		statusImageMaxWidth = 0;
		statusImageScale = 0;
	}

	public void Dispose()
	{
		ThreadSafe.AssertIsMainThread();
		if ( Handle != IntPtr.Zero )
		{
			Sdl.DestroyWindow( Handle );
			Handle = IntPtr.Zero;
		}

		isOpen = false;
		softwareSurfaceAvailable = false;
		DisposeImages();
	}
}
