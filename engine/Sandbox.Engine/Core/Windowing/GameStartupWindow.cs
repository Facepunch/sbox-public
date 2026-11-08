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

	static readonly Color BarColor = new( 0.88f, 0.009f, 0.015f, 1 );
	static readonly Color BarSheenColor = new( 1f, 0.8f, 0.8f, 1 );

	/// <summary>How long the sheen takes to sweep along the bar.</summary>
	const float SheenSeconds = 1.6f;

	readonly bool standalone;
	Bitmap logo;
	Bitmap backdrop;
	Bitmap scaledBackdrop;
	Bitmap framebuffer;
	Bitmap background;
	Bitmap statusImage;
	string statusImageText;
	int statusImageMaxWidth;
	float statusImageScale;
	string status = "Starting";
	int framebufferWidth;
	int framebufferHeight;
	int backdropWidth;
	int backdropHeight;
	float progress = -1;
	bool isOpen = true;
	bool drawFailed;
	bool softwareSurfaceAvailable;
	bool pollingEvents;

	/// <summary>
	/// Held while drawing or using the window, which the animator does from its own thread.
	/// </summary>
	readonly object drawLock = new();
	readonly Stopwatch clock = Stopwatch.StartNew();
	double animatedAt;
	float shownProgress;

	/// <summary>
	/// Whether the splash is being redrawn often enough for the bar to ease along, rather than jump.
	/// </summary>
	bool redrawnContinuously;
	Texture texture;

	internal IntPtr Handle { get; private set; }
	internal float Progress => progress;

	/// <summary>Where the bar has got to on screen, as it eases towards <see cref="Progress"/>.</summary>
	internal float ShownProgress => shownProgress;
	internal string Status => status;
	internal bool IsOpen => Handle != IntPtr.Zero && isOpen;

	/// <summary>Whether <see cref="DrawToTexture"/> can draw it.</summary>
	internal bool IsDrawable => !drawFailed && background is not null;

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

			SdlWindow.WarnIfFailed( Sdl.SetWindowPosition( Handle, x, y ) );
			SdlWindow.Check( Sdl.SetWindowBordered( Handle, !borderless ) );
			SdlWindow.Check( Sdl.SetWindowResizable( Handle, true ) );
			SdlWindow.WarnIfFailed( Sdl.SyncWindow( Handle ) );
			if ( !Sdl.FillWindowColor( Handle, 27, 36, 51, 255 ) )
			{
				Log.Warning( $"Couldn't fill the startup window: {Sdl.GetError()}" );
				drawFailed = true;
				return;
			}

			softwareSurfaceAvailable = true;
			logo = LoadBitmap( LogoPath, "startup logo" );
			if ( !standalone ) backdrop = LoadBitmap( BackdropPath, "startup backdrop" );

			// Composed while hidden and drawn the moment it's shown, so Windows' open animation plays with the splash
			// in it rather than a blank white window
			UpdateProgress( 0, status );
			SdlWindow.Check( Sdl.ShowWindow( Handle ) );
			Draw();
			SdlWindow.WarnIfFailed( Sdl.SyncWindow( Handle ) );
			Bootstrap.MarkStartupWindowVisible();

			if ( !isOpen ) return;

			StartAnimating();
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

		lock ( drawLock )
		{
			if ( Handle == IntPtr.Zero ) return;

			PumpEvents();
			if ( !IsOpen || !SetProgress( value, newStatus ) ) return;

			Draw();
		}
	}

	/// <summary>Move progress on, never back. False if nothing changed.</summary>
	bool SetProgress( float value, string newStatus )
	{
		if ( float.IsNaN( value ) ) return false;

		value = Math.Clamp( value, 0, 1 );
		newStatus ??= status;
		if ( value < progress || (value == progress && newStatus == status) ) return false;

		progress = value;
		status = newStatus;
		return true;
	}

	/// <summary>
	/// Keep the bar moving while the main thread is busy starting the engine, so the window never looks frozen.
	/// </summary>
	void StartAnimating()
	{
		// Only we create, present or destroy the window's surface, always under drawLock, so presenting from another
		// thread is safe with the Windows backend. X11 and Wayland don't allow it.
		if ( !OperatingSystem.IsWindows() ) return;

		redrawnContinuously = true;
		new System.Threading.Thread( () =>
		{
			while ( true )
			{
				System.Threading.Thread.Sleep( 33 );

				lock ( drawLock )
				{
					if ( !IsOpen || drawFailed ) return;

					// Only ever present to the surface the main thread made, never create one from here
					if ( Sdl.WindowHasSurface( Handle ) )
						Draw( barOnly: true );
				}
			}
		} )
		{ IsBackground = true, Name = "Startup Splash" }.Start();
	}

	internal static Bitmap CreateStatusBitmap( string text, int maxWidth, float scale )
	{
		if ( string.IsNullOrWhiteSpace( text ) || maxWidth <= 0 ) return null;

		var paddingX = (int)(10 * scale);
		var paddingY = (int)(4 * scale);
		using var font = new SKFont( SKTypeface.Default, 14 * scale );
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

	/// <summary>
	/// Draw the splash at this progress into a texture, so it can carry on over the game after the game takes over
	/// its window. Call it every frame to keep the bar animating: after the first, only the bar is redrawn.
	/// </summary>
	internal Texture DrawToTexture( int width, int height, float value, string newStatus )
	{
		ThreadSafe.AssertIsMainThread();

		lock ( drawLock )
		{
			redrawnContinuously = true;
			SetProgress( value, newStatus );

			try
			{
				var resized = texture is null || framebufferWidth != width || framebufferHeight != height;
				var (top, bottom) = Compose( width, height, barOnly: !resized );
				if ( bottom <= top ) return texture;

				ReadOnlySpan<byte> pixels = framebuffer.GetBuffer();
				if ( resized )
				{
					texture?.Dispose();
					texture = Texture.Create( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithData( pixels ).WithDynamicUsage().Finish();
				}
				else
				{
					var stride = width * 4;
					texture.Update( pixels.Slice( top * stride, (bottom - top) * stride ), 0, top, width, bottom - top );
				}
			}
			catch ( Exception exception )
			{
				Log.Warning( exception, "Couldn't draw the startup image" );
			}

			return texture;
		}
	}

	internal IntPtr TransferToVulkan()
	{
		lock ( drawLock )
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
			ReleaseWindow();
			return handle;
		}
	}

	/// <summary>Stop using the SDL window without destroying it. Its new owner is responsible for it.</summary>
	internal void Detach()
	{
		lock ( drawLock )
		{
			ReleaseWindow();
			DisposeImages();
		}
	}

	/// <summary>Let go of the window but keep the images, so the splash can still be drawn with <see cref="DrawToTexture"/>.</summary>
	void ReleaseWindow()
	{
		Handle = IntPtr.Zero;
		isOpen = false;
		softwareSurfaceAvailable = false;
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

	void Draw( bool barOnly = false )
	{
		if ( drawFailed || Handle == IntPtr.Zero || !isOpen ) return;

		try
		{
			if ( !Sdl.GetWindowSizeInPixels( Handle, out var width, out var height ) )
				throw new InvalidOperationException( $"Couldn't get startup window pixel size: {Sdl.GetError()}" );

			var (top, bottom) = Compose( width, height, barOnly );
			if ( bottom <= top ) return;

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

	/// <summary>
	/// Draw the backdrop, logo and progress into the framebuffer at this size. With <paramref name="barOnly"/>, just
	/// the rows the bar and status are in are redrawn, if the size hasn't changed. Returns the rows that were drawn.
	/// </summary>
	(int Top, int Bottom) Compose( int width, int height, bool barOnly = false )
	{
		if ( width <= 0 || height <= 0 ) return default;

		if ( framebuffer is null || framebufferWidth != width || framebufferHeight != height )
		{
			framebuffer?.Dispose();
			framebuffer = new Bitmap( width, height );
			framebufferWidth = width;
			framebufferHeight = height;
			DrawBackground( width, height );
			barOnly = false;
		}

		var (top, bottom) = barOnly ? ProgressRows( width, height ) : (0, height);
		var stride = width * 4;
		background.GetBuffer().Slice( top * stride, (bottom - top) * stride ).CopyTo( framebuffer.GetBuffer().Slice( top * stride ) );

		Animate();
		DrawProgress( width, height );
		return (top, bottom);
	}

	/// <summary>
	/// Ease the bar towards the real progress rather than jumping, when it's redrawn often enough to. Steps are
	/// capped, so after a stall it eases on from where it was instead of snapping.
	/// </summary>
	void Animate()
	{
		var now = clock.Elapsed.TotalSeconds;
		var delta = (float)Math.Min( now - animatedAt, 0.1 );
		animatedAt = now;

		shownProgress = redrawnContinuously ? shownProgress.LerpTo( progress, 1 - MathF.Exp( -delta * 8 ) ) : progress;
	}

	/// <summary>Compose the backdrop and logo once per size, so progress updates only have to draw the bar.</summary>
	void DrawBackground( int width, int height )
	{
		background?.Dispose();
		background = new Bitmap( width, height );
		background.Clear( Color.Black );
		background.SetFill( Color.White );
		if ( backdrop is not null )
		{
			PrepareBackdrop( width, height );
			background.DrawBitmap( scaledBackdrop, new Rect( 0, 0, width, height ) );
			DiscardScaledBackdrop();
		}

		if ( logo is null ) return;

		var scale = Math.Min( width / 1920.0f, height / 1080.0f );
		scale = Math.Min( scale, Math.Min( width / (float)logo.Width, height / (float)logo.Height ) );
		var logoWidth = Math.Max( 1, (int)(logo.Width * scale + 0.5f) );
		var logoHeight = Math.Max( 1, (int)(logo.Height * scale + 0.5f) );
		using var scaledLogo = logo.Resize( logoWidth, logoHeight );
		background.DrawBitmap( scaledLogo, new Rect( (width - logoWidth) / 2, (height - logoHeight) / 2, logoWidth, logoHeight ) );
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

	/// <summary>Scales the progress UI with resolution. Below 1080p it stays at a fixed size that's legible on small high-density screens like the Steam Deck.</summary>
	static float UiScale( int height ) => height < 1080 ? 1.25f : height / 1080.0f;

	/// <summary>The rows the status and bar are drawn in, which are all that change once the background is down.</summary>
	static (int Top, int Bottom) ProgressRows( int width, int height )
	{
		var (_, barY, _, barHeight) = BarRect( width, height );
		var scale = UiScale( height );
		return (Math.Max( 0, barY - (int)(56 * scale) ), Math.Min( height, barY + barHeight ));
	}

	static (int X, int Y, int Width, int Height) BarRect( int width, int height )
	{
		var scale = UiScale( height );
		var barWidth = Math.Clamp( (int)(320 * scale), 1, width );
		var barHeight = Math.Clamp( (int)(4 * scale), 1, height );
		return ((width - barWidth) / 2, Math.Max( 0, height - (int)(48 * scale) - barHeight ), barWidth, barHeight);
	}

	void DrawProgress( int width, int height )
	{
		var scale = UiScale( height );
		var (barX, barY, barWidth, barHeight) = BarRect( width, height );
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
		var filledWidth = (int)(barWidth * shownProgress);
		if ( filledWidth <= 0 ) return;

		// A sheen sweeps along the bar, so it's clear things are still going even while it's waiting on something.
		// It's redrawn every frame, so it's written straight into the pixels rather than through a gradient.
		var sheenWidth = Math.Clamp( filledWidth * 0.35f, 2, barWidth * 0.2f );
		var sweep = (float)(clock.Elapsed.TotalSeconds % SheenSeconds) / SheenSeconds;
		var sheen = -sheenWidth + sweep * (filledWidth + sheenWidth * 2);
		var pixels = framebuffer.GetBuffer();
		var stride = width * 4;

		for ( var x = 0; x < filledWidth; x++ )
		{
			var color = Color.Lerp( BarColor, BarSheenColor, Math.Max( 0, 1 - Math.Abs( x - sheen ) / sheenWidth ) ).ToColor32();
			for ( var y = barY; y < barY + barHeight; y++ )
			{
				var pixel = pixels.Slice( y * stride + (barX + x) * 4, 4 );
				pixel[0] = color.r;
				pixel[1] = color.g;
				pixel[2] = color.b;
				pixel[3] = color.a;
			}
		}
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
		background?.Dispose();
		background = null;
		texture?.Dispose();
		texture = null;
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

		lock ( drawLock )
		{
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
}
