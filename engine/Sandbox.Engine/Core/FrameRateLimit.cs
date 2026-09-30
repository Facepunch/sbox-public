using Sandbox.Engine.Settings;

namespace Sandbox.Engine;

/// <summary>
/// A frame-rate cap and the setting responsible for it. Non-positive rates are uncapped.
/// </summary>
internal readonly record struct FrameRateLimit( double FramesPerSecond, string Source )
{
	internal static FrameRateLimit FromSettings( bool appActive, bool vsync, float refreshRate )
	{
		var settings = RenderSettings.Instance;
		var limit = new FrameRateLimit( settings.MaxFrameRate, "fps_max" );
		if ( Game.IsMainMenuVisible ) limit = limit.Tighten( settings.MaxFrameRateMenu, "fps_max_menu" );
		if ( !appActive ) limit = limit.Tighten( settings.MaxFrameRateInactive, "fps_max_inactive" );
		// Running ahead of the refresh rate just stalls the main thread on presentation.
		if ( vsync ) limit = limit.Tighten( refreshRate, "vsync" );
		return limit;
	}

	internal FrameRateLimit Tighten( double framesPerSecond, string source )
	{
		if ( framesPerSecond > 0 && (FramesPerSecond <= 0 || framesPerSecond < FramesPerSecond) )
			return new( framesPerSecond, source );

		return this;
	}
}
