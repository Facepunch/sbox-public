using System;
using NativeEngine;

namespace Sandbox.Engine.Settings;

public partial class RenderSettings
{

	public int DisplayId
	{
		get => VideoSettings.Get<int>( "displayid", 0 );
		set => VideoSettings.Set<int>( "displayid", value );
	}

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

	/// <summary>
	/// Hardware MSAA sample count. Applies on its own and underneath <see cref="AntiAliasMode.Cmaa2"/>.
	/// </summary>
	public MultisampleAmount AntiAliasQuality
	{
		get
		{
			var defaultValue = RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
			var value = VideoSettings.Get( "aaquality", defaultValue );

			if ( !Enum.IsDefined( typeof( RenderMultisampleType ), value ) )
				value = defaultValue;

			return value.FromEngine();
		}

		set
		{
			var supported = GetSupportedAntiAliasQuality( value );
			VideoSettings.Set( "aaquality", supported.ToEngine() );
		}
	}

	/// <summary>
	/// Anti-aliasing method. CMAA2 filters the finished image and combines with <see cref="AntiAliasQuality"/>.
	/// </summary>
	public AntiAliasMode AntiAliasMode
	{
		get
		{
			var defaultMode = UpscalerMode == UpscalerMode.Off ? AntiAliasMode.Cmaa2 : AntiAliasMode.None;
			var value = VideoSettings.Get<int>( "aa.mode", (int)defaultMode );
			return Enum.IsDefined( typeof( AntiAliasMode ), value ) ? (AntiAliasMode)value : defaultMode;
		}

		set
		{
			if ( !Enum.IsDefined( typeof( AntiAliasMode ), value ) )
				throw new ArgumentOutOfRangeException( nameof( value ), value, "Undefined anti-aliasing mode." );

			VideoSettings.Set( "aa.mode", (int)value );
		}
	}

	/// <summary>Returns the requested MSAA amount when supported, or the best available amount.</summary>
	public static MultisampleAmount GetSupportedAntiAliasQuality( MultisampleAmount amount ) =>
		NativeEngine.RenderDeviceManager.GetBestMultisampleType( amount.ToEngine() ).FromEngine();

	internal struct VideoModeSnapshot
	{
		public int Width, Height, DisplayId;
		public bool Fullscreen, Borderless, VSync;
		public MultisampleAmount AntiAlias;
		public AntiAliasMode AntiAliasMode;
		public int MaxFps, MaxFpsInactive;
		public float Fov;
	}

	internal VideoModeSnapshot CaptureSnapshot() => new VideoModeSnapshot
	{
		Width = ResolutionWidth,
		Height = ResolutionHeight,
		DisplayId = DisplayId,
		Fullscreen = Fullscreen,
		Borderless = Borderless,
		VSync = VSync,
		AntiAlias = AntiAliasQuality,
		AntiAliasMode = AntiAliasMode,
		MaxFps = MaxFrameRate,
		MaxFpsInactive = MaxFrameRateInactive,
		Fov = DefaultFOV,
	};

	internal void RestoreSnapshot( VideoModeSnapshot snap )
	{
		ResolutionWidth = snap.Width;
		ResolutionHeight = snap.Height;
		DisplayId = snap.DisplayId;
		Fullscreen = snap.Fullscreen;
		Borderless = snap.Borderless;
		VSync = snap.VSync;

		AntiAliasQuality = snap.AntiAlias;
		AntiAliasMode = snap.AntiAliasMode;
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
			var display = DisplayId > 0 ? (uint)DisplayId : SdlDisplay.Current;
			var size = SdlDisplay.GetDesktopSize( display );
			ResolutionWidth = (int)size.x;
			ResolutionHeight = (int)size.y;
			DisplayId = (int)display;
		}

		window.QueueVideoMode( GameWindow.VideoMode.FromSettings( this ) );
	}

	/// <summary>
	/// Lists SDL fullscreen modes for a display. Both windowed and fullscreen settings use
	/// this list; <paramref name="windowed"/> is retained for compatibility and does not filter it.
	/// All returned formats are RGBA8888, matching the game swapchain.
	/// </summary>
	/// <param name="windowed">Unused; kept for compatibility.</param>
	/// <param name="displayId">SDL display id, or 0 for the preferred/current display.</param>
	public VideoDisplayMode[] DisplayModes( bool windowed, int displayId = 0 )
	{
		var display = displayId > 0 ? (uint)displayId
			: DisplayId > 0 ? (uint)DisplayId
			: SdlDisplay.Current;
		return SdlDisplay.GetModes( display );
	}

	public struct VideoDisplayMode
	{
		public int DisplayId { get; set; }
		public string Name { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public float RefreshRate { get; set; }
		/// <summary>The game swapchain format (RGBA8888), rather than the display's native pixel format.</summary>
		public ImageFormat Format { get; set; }
	}

	public List<VideoDisplayMode> GetDisplays()
	{
		return SdlDisplay.GetDisplays().Select( x => new VideoDisplayMode
		{
			DisplayId = (int)x.Id,
			Name = x.Name,
			Width = (int)x.Width,
			Height = (int)x.Height,
			RefreshRate = (float)x.RefreshRate,
			Format = ImageFormat.RGBA8888,
		} ).ToList();
	}
}
