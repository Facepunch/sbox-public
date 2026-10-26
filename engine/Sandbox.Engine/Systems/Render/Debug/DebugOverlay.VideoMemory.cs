using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Engine.Settings;

namespace Sandbox;

internal static partial class DebugOverlay
{
	internal static class VideoMemoryWarning
	{
		static VideoMemoryPressure _pressure;
		static double _nextSample;
		static readonly Logger log = new( "VideoMemory" );
		const string Title = "Video memory budget exceeded";
		const string Message = "GPU memory may be paging to system RAM, causing stuttering. Lower Texture Detail or close other GPU-heavy applications.";

		internal static void Draw( Painter painter )
		{
			if ( Application.IsHeadless || !Graphics.IsAvailable )
			{
				_pressure = default;
				_nextSample = 0;
				return;
			}

			var now = RealTime.NowDouble;
			if ( now >= _nextSample )
			{
				_nextSample = now + 1.0;
				// Integrated GPUs normally use system RAM; that alone is not VRAM paging.
				if ( SystemInfo.GpuIntegrated )
				{
					_pressure = default;
					return;
				}

				// Use the driver's heap usage, not the engine's incomplete allocation tally.
				g_pRenderDevice.GetVideoMemoryInfo( out var budget, out var usage, out _ );
				var wasActive = _pressure.IsActive;
				_pressure.Update( now, budget, usage );
				if ( _pressure.IsActive && !wasActive )
					log.Warning( $"{Title}: {usage / (1024 * 1024)} MiB used / {budget / (1024 * 1024)} MiB budget. {Message}" );
			}

			if ( !_pressure.IsActive || Screen.Width <= 64 || Screen.Height <= 64 )
				return;

			var width = MathF.Min( 460, Screen.Width - 32 );
			var x = Screen.Width - width - 16;
			var warningColor = new Color( 1f, 0.7f, 0.2f );
			painter.TextStyle = new TextStyle( "Roboto", 16, warningColor ) { FontWeight = 700, Alignment = TextFlag.LeftTop | TextFlag.WordWrap };
			var titleSize = painter.MeasureText( Title, new Vector2( width - 32, 512 ) );
			var titleStyle = painter.TextStyle;
			painter.TextStyle = new TextStyle( "Roboto", 14, Color.White ) { Alignment = TextFlag.LeftTop | TextFlag.WordWrap };
			var messageSize = painter.MeasureText( Message, new Vector2( width - 32, 512 ) );
			var height = titleSize.y + messageSize.y + 40;
			painter.Fill = Color.Black.WithAlpha( 0.9f );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( x, 16, width, height ), 6 );
			painter.Fill = warningColor;
			painter.Rect( new Rect( x, 16, 4, height ) );
			painter.Text( Message, new Rect( x + 16, 40 + titleSize.y, width - 32, messageSize.y ) );
			painter.TextStyle = titleStyle;
			painter.Text( Title, new Rect( x + 16, 32, width - 32, titleSize.y ) );
		}
	}
}
