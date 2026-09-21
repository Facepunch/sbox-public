using NativeEngine;

namespace Sandbox.Engine.Settings;

public partial class RenderSettings
{
	public int ResolutionWidth
	{
		get => VideoSettings.Get<int>( "defaultreswidth", 1920 );
		set => VideoSettings.Set<int>( "defaultreswidth", value );
	}

	public int ResolutionHeight
	{
		get => VideoSettings.Get<int>( "defaultresheight", 1080 );
		set => VideoSettings.Set<int>( "defaultresheight", value );
	}

	public bool Fullscreen
	{
		get => VideoSettings.Get<bool>( "fullscreen", false );
		set => VideoSettings.Set<bool>( "fullscreen", value );
	}

	public bool Borderless
	{
		get => VideoSettings.Get<bool>( "borderless", true );
		set => VideoSettings.Set<bool>( "borderless", value );
	}

	public bool VSync
	{
		get => VideoSettings.Get<bool>( "vsync", true );
		set => VideoSettings.Set<bool>( "vsync", value );
	}

	public MultisampleAmount AntiAliasQuality
	{
		get
		{
			var defaultValue = RenderMultisampleType.RENDER_MULTISAMPLE_4X;
			var value = VideoSettings.Get( "aaquality", defaultValue );

			if ( !Enum.IsDefined( typeof( RenderMultisampleType ), value ) )
				value = defaultValue;

			return value.FromEngine();
		}

		set => VideoSettings.Set( "aaquality", GetSupportedAntiAliasQuality( value ).ToEngine() );
	}

	/// <summary>Returns the requested MSAA amount when supported, or the best available amount.</summary>
	public static MultisampleAmount GetSupportedAntiAliasQuality( MultisampleAmount amount ) =>
		NativeEngine.RenderDeviceManager.GetBestMultisampleType( amount.ToEngine() ).FromEngine();

	internal struct VideoModeSnapshot
	{
		public int Width, Height;
		public bool Fullscreen, Borderless, VSync;
		public MultisampleAmount AntiAlias;
		public int MaxFps, MaxFpsInactive;
		public float Fov;
	}

	internal VideoModeSnapshot CaptureSnapshot() => new VideoModeSnapshot
	{
		Width = ResolutionWidth,
		Height = ResolutionHeight,
		Fullscreen = Fullscreen,
		Borderless = Borderless,
		VSync = VSync,
		AntiAlias = AntiAliasQuality,
		MaxFps = MaxFrameRate,
		MaxFpsInactive = MaxFrameRateInactive,
		Fov = DefaultFOV,
	};

	internal void RestoreSnapshot( VideoModeSnapshot snap )
	{
		ResolutionWidth = snap.Width;
		ResolutionHeight = snap.Height;
		Fullscreen = snap.Fullscreen;
		Borderless = snap.Borderless;
		VSync = snap.VSync;
		AntiAliasQuality = snap.AntiAlias;
		MaxFrameRate = snap.MaxFps;
		MaxFrameRateInactive = snap.MaxFpsInactive;
		DefaultFOV = snap.Fov;

		ApplyVideoMode();
		VideoSettings.Save();
	}

	private void ApplyVideoMode()
	{
		if ( GameWindow.Current is not { } window ) return;

		if ( Borderless && !Fullscreen )
		{
			var size = SdlDisplay.GetDesktopSize( SdlDisplay.Current );
			ResolutionWidth = (int)size.x;
			ResolutionHeight = (int)size.y;
		}

		window.QueueVideoMode( GameWindow.VideoMode.FromSettings( this ) );
	}

	/// <summary>
	/// Lists SDL fullscreen modes for the current display. Both windowed and fullscreen settings use
	/// this list; <paramref name="windowed"/> is retained for compatibility and does not filter it.
	/// All returned formats are RGBA8888, matching the game swapchain.
	/// </summary>
	public VideoDisplayMode[] DisplayModes( bool windowed ) => SdlDisplay.GetModes( SdlDisplay.Current );

	public struct VideoDisplayMode
	{
		public int Width { get; set; }
		public int Height { get; set; }
		public float RefreshRate { get; set; }
		/// <summary>The game swapchain format (RGBA8888), rather than the display's native pixel format.</summary>
		public ImageFormat Format { get; set; }
	}
}
