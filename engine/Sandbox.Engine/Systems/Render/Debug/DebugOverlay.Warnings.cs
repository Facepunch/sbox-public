using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Network;

namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// Small labelled icons in the top right for problems the player can feel but not diagnose: packet loss, video
	/// memory over budget and a silent host. Orange is a warning, red is critical. The numbers behind them are in the dev tools.
	/// </summary>
	internal static class Warnings
	{
		// Shared with the dev tools' issue list
		internal const float QualityWarn = 0.95f, QualityBad = 0.8f; // Fraction of packets delivered

		enum Kind { VideoMemory, Connection, ConnectionProblem, Count }

		static readonly string[] Icons = ["memory", "sync_problem", "wifi_off"];
		static readonly string[] Labels = ["Low VRAM", "Packet Loss", "Connection Problem"];
		static readonly WarningState[] States = new WarningState[(int)Kind.Count];

		// Not debounced: it has its own threshold, and clears as soon as the host is heard from
		static bool _connectionProblem;
		static int _tenthsUntilTimeout = -1;
		static string _connectionProblemDetail;

		// Sized for the widest countdown, so the cell doesn't change width as it ticks
		const string DetailWidthSample = "Disconnecting in 00.0s";
		static readonly Logger log = new( "VideoMemory" );
		static double _nextSample;
		static ulong _vramBudget, _vramUsage;

		const double SampleInterval = 0.5;
		const float CellHeight = 26, CellGap = 4, PadX = 8, IconSize = 16, IconGap = 5, LabelSize = 12, DetailSize = 11, DetailHeight = 14, LineGap = 1, Margin = 16;
		static readonly Color WarningColor = new( 1f, 0.62f, 0.15f );
		static readonly Color CriticalColor = new( 1f, 0.27f, 0.23f );

		internal static void Draw( Painter painter )
		{
			if ( Application.IsHeadless || !Graphics.IsAvailable )
			{
				Array.Clear( States );
				_nextSample = 0;
				_connectionProblem = false;
				return;
			}

			var now = RealTime.NowDouble;
			if ( now >= _nextSample )
			{
				_nextSample = now + SampleInterval;
				Sample( now );
			}

			// Every frame, so the countdown ticks smoothly
			SampleConnectionProblem();

			if ( Screen.Width <= 64 || Screen.Height <= 64 )
				return;

			// Sized like the UI, relative to 1080p, but never so small it can't be read
			var scale = MathF.Max( Screen.Height / 1080f, 0.85f );
			float cellHeight = CellHeight * scale, cellGap = CellGap * scale, padX = PadX * scale, iconSize = IconSize * scale, iconGap = IconGap * scale, margin = Margin * scale;
			var labelStyle = new TextStyle( "Inter", LabelSize * scale, Color.White.WithAlpha( 0.85f ) ) { FontWeight = 600, Alignment = TextFlag.LeftCenter };
			var detailStyle = new TextStyle( "Inter", DetailSize * scale, Color.White.WithAlpha( 0.6f ) ) { FontWeight = 500, Alignment = TextFlag.LeftCenter };

			// One row, laid out from the right edge
			var x = Screen.Width - margin;
			for ( int i = States.Length - 1; i >= 0; i-- )
			{
				var isConnectionProblem = i == (int)Kind.ConnectionProblem;
				var level = isConnectionProblem ? (_connectionProblem ? WarningLevel.Critical : WarningLevel.None) : States[i].Level;
				if ( level == WarningLevel.None ) continue;

				var color = level == WarningLevel.Critical ? CriticalColor : WarningColor;
				var label = Labels[i];
				var detail = isConnectionProblem ? _connectionProblemDetail : null;

				painter.TextStyle = labelStyle;
				var labelSize = painter.MeasureText( label );
				var textWidth = labelSize.x;
				var detailSize = Vector2.Zero;

				if ( detail is not null )
				{
					painter.TextStyle = detailStyle;
					detailSize = painter.MeasureText( detail );
					textWidth = MathF.Max( textWidth, painter.MeasureText( DetailWidthSample ).x );
				}

				var height = detail is null ? cellHeight : cellHeight + DetailHeight * scale;
				var cellWidth = MathF.Ceiling( padX + iconSize + iconGap + textWidth + padX );
				x -= cellWidth;
				var rect = new Rect( x, margin, cellWidth, height );
				x -= cellGap;

				painter.Fill = Color.Black.WithAlpha( 0.45f );
				painter.Stroke = Stroke.None;
				painter.Rect( rect, 4 * scale );

				var iconRect = new Rect( rect.Left + padX, rect.Top, iconSize, height );
				DrawText( painter, new TextRendering.Scope( Icons[i], color, iconSize, "Material Icons", 400 ), iconRect, TextFlag.Center );

				var textLeft = iconRect.Right + iconGap;

				if ( detail is null )
				{
					painter.TextStyle = labelStyle;
					var labelRect = new Rect( textLeft, rect.Top, labelSize.x, height );
					painter.Text( label, labelRect.Align( labelSize, TextFlag.LeftCenter ).SnapToGrid() );
					continue;
				}

				// Both lines centred together in the cell
				var lineGap = LineGap * scale;
				var top = rect.Top + (height - labelSize.y - lineGap - detailSize.y) / 2;

				painter.TextStyle = labelStyle;
				painter.Text( label, new Rect( textLeft, top, labelSize.x, labelSize.y ).SnapToGrid() );

				painter.TextStyle = detailStyle;
				painter.Text( detail, new Rect( textLeft, top + labelSize.y + lineGap, detailSize.x, detailSize.y ).SnapToGrid() );
			}
		}

		static void Sample( double now )
		{
			var stats = Networking.IsActive && !Networking.IsHost && Connection.Host is { } host ? host.Stats : (ConnectionStats?)null;

			Update( Kind.VideoMemory, now, SampleVideoMemory() );
			Update( Kind.Connection, now, stats is { } c ? Quality( c.ConnectionQuality ) : null );
		}

		static void SampleConnectionProblem()
		{
			_connectionProblem = Networking.System?.LastRecievedFromHost is { } since && since >= Networking.ConnectionProblemTime;
			if ( !_connectionProblem || Networking.System.TimeoutCountdown is not { } left ) return;

			// Rebuilt only when the shown value changes
			var tenths = (int)Math.Ceiling( left * 10 );
			if ( tenths == _tenthsUntilTimeout ) return;

			_tenthsUntilTimeout = tenths;
			_connectionProblemDetail = $"Disconnecting in {tenths / 10f:0.0}s";
		}

		/// <summary>
		/// A null sample means it can't be measured right now, which clears the icon straight away.
		/// </summary>
		static void Update( Kind kind, double now, WarningLevel? sample )
		{
			ref var state = ref States[(int)kind];

			if ( sample is not { } level )
			{
				state = default;
				return;
			}

			var was = state.Level;
			state.Update( now, level );

			if ( kind == Kind.VideoMemory && was == WarningLevel.None && state.Level != WarningLevel.None )
				LogVideoMemory();
		}

		/// <summary>
		/// Zero or less is no measurement.
		/// </summary>
		internal static WarningLevel Quality( float quality ) => quality <= 0 ? WarningLevel.None
			: quality < QualityBad ? WarningLevel.Critical
			: quality < QualityWarn ? WarningLevel.Warning
			: WarningLevel.None;

		static WarningLevel? SampleVideoMemory()
		{
			// Integrated GPUs normally use system RAM; that alone is not VRAM paging.
			if ( SystemInfo.GpuIntegrated )
				return null;

			// Use the driver's heap usage, not the engine's incomplete allocation tally: the drawing device's
			ulong budget = Graphics.VideoMemoryBudget, usage = Graphics.VideoMemoryUsed;
			if ( budget == 0 )
				return null;

			_vramBudget = budget;
			_vramUsage = usage;

			var active = States[(int)Kind.VideoMemory].Level != WarningLevel.None;
			return usage > budget + budget / 10 ? WarningLevel.Critical
				: usage > budget ? WarningLevel.Warning
				// Require 5% headroom before clearing, so small budget fluctuations don't flicker
				: active && usage > budget - budget / 20 ? WarningLevel.Warning
				: WarningLevel.None;
		}

		static void LogVideoMemory()
		{
			log.Warning( $"Video memory budget exceeded: {_vramUsage / (1024 * 1024)} MiB used / {_vramBudget / (1024 * 1024)} MiB budget. GPU memory may be paging to system RAM, causing stuttering. Lower Texture Detail or close other GPU-heavy applications." );
		}
	}
}
