using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	/// <summary>
	/// Requested window geometry. Nonpositive dimensions select the desktop size; fullscreen dimensions request a display mode.
	/// </summary>
	internal readonly record struct WindowMode( bool Fullscreen, bool Borderless, int Width, int Height )
	{
		internal Vector2 GetSize( Vector2 displaySize )
		{
			if ( (Borderless && !Fullscreen) || Width <= 0 || Height <= 0 ) return displaySize;
			return Fullscreen ? new Vector2( Width, Height ) : new Vector2( Math.Min( Width, displaySize.x ), Math.Min( Height, displaySize.y ) );
		}
	}

	/// <summary>
	/// A window mode and its swapchain presentation and multisampling settings.
	/// </summary>
	internal readonly record struct VideoMode( WindowMode Window, bool VSync, RenderMultisampleType Multisample )
	{
		internal static VideoMode FromSettings( RenderSettings settings ) => new(
			new( settings.Fullscreen, settings.Borderless, settings.ResolutionWidth, settings.ResolutionHeight ),
			settings.VSync, settings.AntiAliasQuality.ToEngine() );

		internal static VideoMode FromCommandLine( RenderSettings settings )
		{
			if ( CommandLine.HasSwitch( "-nowindow" ) )
				return new( new( false, false, 4, 4 ), false, RenderMultisampleType.RENDER_MULTISAMPLE_NONE );

			var mode = FromSettings( settings );
			var window = mode.Window with
			{
				Width = CommandLine.GetSwitchInt( "-width", CommandLine.GetSwitchInt( "-w", mode.Window.Width ) ),
				Height = CommandLine.GetSwitchInt( "-height", CommandLine.GetSwitchInt( "-h", mode.Window.Height ) ),
				Fullscreen = mode.Window.Fullscreen || CommandLine.HasSwitch( "-fullscreen" ) || CommandLine.HasSwitch( "-fs" ),
				Borderless = mode.Window.Borderless || CommandLine.HasSwitch( "-noborder" )
			};
			// Explicit dimensions must not be replaced by the default borderless desktop size.
			if ( CommandLine.HasSwitch( "-w" ) || CommandLine.HasSwitch( "-width" ) ||
				CommandLine.HasSwitch( "-h" ) || CommandLine.HasSwitch( "-height" ) )
				window = window with { Borderless = false };
			if ( CommandLine.HasSwitch( "-border" ) ) window = window with { Borderless = false };
			if ( CommandLine.HasSwitch( "-sw" ) ) window = window with { Fullscreen = false, Borderless = false };

			return new( window,
				(mode.VSync || CommandLine.HasSwitch( "-vsync" )) && !CommandLine.HasSwitch( "-novsync" ) && !CommandLine.HasSwitch( "-forcenovsync" ),
				CommandLine.HasSwitch( "-msaa" ) ? CommandLine.GetSwitchInt( "-msaa", 0 ).ToEngineMultisampleType() : mode.Multisample );
		}
	}
}
