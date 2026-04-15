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
		get => VideoSettings.Get<bool>( "fullscreen", true );
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

		set => VideoSettings.Set( "aaquality", value.ToEngine() );
	}

	private void ApplyVideoMode()
	{
		// No changing this in the editor
		if ( Application.IsEditor )
			return;

		NativeEngine.RenderDeviceManager.ChangeVideoMode( Fullscreen, Borderless, VSync, ResolutionWidth, ResolutionHeight, AntiAliasQuality.ToEngine() );

		if ( Borderless )
		{
			int desktopWidth = 0;
			int desktopHeight = 0;
			uint desktopRefreshRate = 0;
			EngineGlobal.Plat_GetDesktopResolution( EngineGlobal.Plat_GetDefaultMonitorIndex(), ref desktopWidth, ref desktopHeight, ref desktopRefreshRate );
			ResolutionWidth = desktopWidth;
			ResolutionHeight = desktopHeight;
		}
	}

	public unsafe VideoDisplayMode[] DisplayModes( bool windowed )
	{
		var modes = new VideoDisplayMode[256];

		fixed ( VideoDisplayMode* ptr = modes )
		{
			var c = NativeEngine.RenderDeviceManager.GetDisplayModes( ptr, modes.Length, windowed );
			Array.Resize( ref modes, c );
		}

		return modes;
	}


	public struct VideoDisplayMode
	{
		public int Width { get; set; }
		public int Height { get; set; }
		public float RefreshRate { get; set; }
		public ImageFormat Format { get; set; }
	}
}
