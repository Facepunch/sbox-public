using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Network;

namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// Small labelled icons in the top right for problems the player can feel but not diagnose: packet loss and video
	/// memory over budget. Orange is a warning, red is critical. The numbers behind them are in the dev tools.
	/// </summary>
	internal static class Warnings
	{
		// Shared with the dev tools' issue list
		internal const float QualityWarn = 0.95f, QualityBad = 0.8f; // Fraction of packets delivered

		enum Kind { VideoMemory, Connection, Count }

		static readonly string[] Icons = ["memory", "sync_problem"];
		static readonly string[] Labels = ["Low VRAM", "Packet Loss"];
		static readonly WarningState[] States = new WarningState[(int)Kind.Count];
		static readonly Logger log = new( "VideoMemory" );
		static double _nextSample;
		static ulong _vramBudget, _vramUsage;

		const double SampleInterval = 0.5;
		const float CellHeight = 26, CellGap = 4, PadX = 8, IconSize = 16, IconGap = 5, LabelSize = 12, Margin = 16;
		static readonly Color WarningColor = new( 1f, 0.62f, 0.15f );
		static readonly Color CriticalColor = new( 1f, 0.27f, 0.23f );

		internal static void Draw( Painter painter )
		{
			if ( Application.IsHeadless || !Graphics.IsAvailable )
			{
				Array.Clear( States );
				_nextSample = 0;
				return;
			}

			var now = RealTime.NowDouble;
			if ( now >= _nextSample )
			{
				_nextSample = now + SampleInterval;
				Sample( now );
			}

			if ( Screen.Width <= 64 || Screen.Height <= 64 )
				return;

			// Sized like the UI, relative to 1080p, but never so small it can't be read
			var scale = MathF.Max( Screen.Height / 1080f, 0.85f );
			float cellHeight = CellHeight * scale, cellGap = CellGap * scale, padX = PadX * scale, iconSize = IconSize * scale, iconGap = IconGap * scale, margin = Margin * scale;
			var labelStyle = new TextStyle( "Inter", LabelSize * scale, Color.White.WithAlpha( 0.85f ) ) { FontWeight = 600, Alignment = TextFlag.LeftCenter };

			// One row, laid out from the right edge
			var x = Screen.Width - margin;
			for ( int i = States.Length - 1; i >= 0; i-- )
			{
				var state = States[i];
				if ( state.Level == WarningLevel.None ) continue;

				var color = state.Level == WarningLevel.Critical ? CriticalColor : WarningColor;

				painter.TextStyle = labelStyle;
				var labelSize = painter.MeasureText( Labels[i] );
				var cellWidth = MathF.Ceiling( padX + iconSize + iconGap + labelSize.x + padX );
				x -= cellWidth;
				var rect = new Rect( x, margin, cellWidth, cellHeight );
				x -= cellGap;

				painter.Fill = Color.Black.WithAlpha( 0.45f );
				painter.Stroke = Stroke.None;
				painter.Rect( rect, 4 * scale );

				var iconRect = new Rect( rect.Left + padX, rect.Top, iconSize, cellHeight );
				DrawText( painter, new TextRendering.Scope( Icons[i], color, iconSize, "Material Icons", 400 ), iconRect, TextFlag.Center );

				painter.TextStyle = labelStyle;
				var labelRect = new Rect( iconRect.Right + iconGap, rect.Top, labelSize.x, cellHeight );
				painter.Text( Labels[i], labelRect.Align( labelSize, TextFlag.LeftCenter ).SnapToGrid() );
			}
		}

		static void Sample( double now )
		{
			var stats = Networking.IsActive && !Networking.IsHost && Connection.Host is { } host ? host.Stats : (ConnectionStats?)null;

			Update( Kind.VideoMemory, now, SampleVideoMemory() );
			Update( Kind.Connection, now, stats is { } c ? Quality( c.ConnectionQuality ) : null );
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

			// Use the driver's heap usage, not the engine's incomplete allocation tally: Source 2's, or without its device the
			// drawing device's (the empty one reports none)
			ulong budget = Graphics.VideoMemoryBudget, usage = Graphics.VideoMemoryUsed;
			if ( Graphics.HasSource2Device ) g_pRenderDevice.GetVideoMemoryInfo( out budget, out usage, out _ );
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
