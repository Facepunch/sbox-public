using NativeEngine;
using Sandbox.Diagnostics;
using Sandbox.Engine;

namespace Sandbox;

internal static partial class DebugOverlay
{
	[ConVar( "overlay_gpu_freeze", Help = "Freeze the GPU timeline overlay; profiling continues" )]
	internal static bool FreezeGpuTimeline { get; set; }

	public class GpuTimeline
	{
		private static readonly Color[] PassColors =
		[
			new( 0.4f, 0.7f, 1.0f ),
			new( 0.4f, 1.0f, 0.5f ),
			new( 1.0f, 0.7f, 0.3f ),
			new( 1.0f, 0.4f, 0.4f ),
			new( 0.8f, 0.5f, 1.0f ),
			new( 1.0f, 1.0f, 0.4f ),
			new( 0.5f, 1.0f, 1.0f ),
			new( 1.0f, 0.5f, 0.8f )
		];

		private const int MaxTimelineRows = 8;
		private const float TimelineRowHeight = 30;
		private const float TimelineLabelWidth = 132;
		private static GpuFrameTimeline _timeline;
		private static ulong _timelineFrame;
		private static double _nextTimelineUpdate;
		private static readonly List<TimelineBar> _timelineBars = new();
		private static readonly int[] _timelineRows = new int[2];
		private static double _timelineStart;
		private static double _timelineSpan;
		private static int _hiddenTimelinePasses;
		private static Rect _timelineHoverBounds;
		private static int _hoveredTimelineBar = -1;
		private static Rect _freezeButtonBounds;
		private static bool _freezeButtonPressed;
		private static bool _mouseWasDown;

		private readonly record struct TimelineBar( GpuFrameTimeline.Event Event, int Queue, int Row, Color Color, string Label, string Hierarchy );

		internal static void Clear()
		{
			_timeline = null;
			_timelineFrame = 0;
			_nextTimelineUpdate = 0;
			_timelineBars.Clear();
			_timelineHoverBounds = default;
			_hoveredTimelineBar = -1;
			_freezeButtonBounds = default;
			ResetFreezeButton();
		}

		private static void ResetFreezeButton()
		{
			_freezeButtonPressed = false;
			_mouseWasDown = InputRouter.IsButtonDown( ButtonCode.MouseLeft );
		}

		private static void UpdateFreezeButton()
		{
			// Observe the existing button state without consuming input or registering router hooks.
			if ( !InputRouter.MouseCursorVisible || !WindowInput.HasMouseFocus() )
			{
				ResetFreezeButton();
				return;
			}

			bool down = InputRouter.IsButtonDown( ButtonCode.MouseLeft );
			bool hovered = _freezeButtonBounds.IsInside( Mouse.Position );
			if ( down && !_mouseWasDown )
			{
				_freezeButtonPressed = hovered;
			}
			else if ( !down && _mouseWasDown )
			{
				if ( _freezeButtonPressed && hovered )
				{
					FreezeGpuTimeline = !FreezeGpuTimeline;
					_nextTimelineUpdate = 0;
				}
				_freezeButtonPressed = false;
			}
			_mouseWasDown = down;
		}

		private static void UpdateTimeline( bool hovering )
		{
			// Hold a real completed frame briefly so labels remain readable. Never average boundaries.
			if ( ((FreezeGpuTimeline || hovering) && _timeline is not null) || RealTime.Now < _nextTimelineUpdate )
			{
				return;
			}

			_nextTimelineUpdate = RealTime.Now + 0.25;
			if ( GpuProfilerStats.FrameId == _timelineFrame )
			{
				return;
			}

			_timelineFrame = GpuProfilerStats.FrameId;
			_timeline = GpuFrameTimeline.Capture();
			_timelineBars.Clear();
			_hoveredTimelineBar = -1;
			Array.Clear( _timelineRows );
			_hiddenTimelinePasses = 0;
			if ( _timeline is null )
			{
				return;
			}

			var events = _timeline.Events;
			var indices = Enumerable.Range( 0, events.Length ).Where( i => events[i].Kind == "pass" )
				.OrderBy( i => events[i].StartMs ).ThenByDescending( i => events[i].EndMs )
				.ThenBy( i => events[i].Path.Count( c => c == '/' ) ).ToArray();
			if ( indices.Length == 0 )
			{
				return;
			}

			_timelineStart = events[indices[0]].StartMs;
			_timelineSpan = Math.Max( 0.001, indices.Max( i => events[i].EndMs ) - _timelineStart );
			var rows = new int[events.Length];
			Array.Fill( rows, -1 );
			var rowEnds = new double[2, MaxTimelineRows];

			foreach ( var i in indices )
			{
				var e = events[i];
				int queue = e.Queue == "async-compute" ? 1 : 0;
				int row = e.Parent < 0 ? 0 : rows[e.Parent] + 1;
				if ( e.Parent >= 0 && rows[e.Parent] < 0 )
				{
					_hiddenTimelinePasses++;
					continue;
				}

				// Nested and overlapping scopes share a queue band, but never cover each other's bars.
				while ( row < MaxTimelineRows && rowEnds[queue, row] > e.StartMs )
				{
					row++;
				}

				if ( row == MaxTimelineRows )
				{
					_hiddenTimelinePasses++;
					continue;
				}

				rows[i] = row;
				rowEnds[queue, row] = e.EndMs;
				_timelineRows[queue] = Math.Max( _timelineRows[queue], row + 1 );
				uint hash = 2166136261;
				foreach ( char c in e.Name )
				{
					hash = unchecked((hash ^ c) * 16777619);
				}

				var names = new Stack<string>();
				for ( int parent = i; parent >= 0; parent = events[parent].Parent )
				{
					names.Push( events[parent].Name );
				}
				_timelineBars.Add( new( e, queue, row, PassColors[hash % (uint)PassColors.Length],
					$"{e.Name}  {e.EndMs - e.StartMs:F3}ms", string.Join( " > ", names ) ) );
			}
		}

		internal static void Draw( Painter painter, ref Vector2 pos )
		{
			_freezeButtonBounds = default;
			float width = Screen.Width - pos.x - 32;
			if ( width < 320 )
			{
				_timelineHoverBounds = default;
				_hoveredTimelineBar = -1;
				ResetFreezeButton();
				return;
			}

			// Use the routed cursor, including the console/menu cursor, rather than the game's mouse mode.
			bool hovering = InputRouter.MouseCursorVisible && _timelineHoverBounds.IsInside( Mouse.Position );
			UpdateTimeline( hovering || _freezeButtonPressed );
			using var state = painter.Scope();
			if ( _timelineBars.Count == 0 )
			{
				ResetFreezeButton();
				TimelineText( painter, "GPU timeline: waiting for supported timestamp data...", Color.White,
					new Rect( pos.x, pos.y, width, 24 ) );
				pos.y += 32;
				return;
			}

			float chartX = pos.x + TimelineLabelWidth;
			float chartWidth = width - TimelineLabelWidth;
			float chartY = pos.y + 64;
			float graphicsHeight = _timelineRows[0] > 0 ? Math.Max( 2, _timelineRows[0] ) * TimelineRowHeight : 0;
			float computeHeight = _timelineRows[1] > 0 ? Math.Max( 2, _timelineRows[1] ) * TimelineRowHeight : 0;
			float computeY = chartY + graphicsHeight + (graphicsHeight > 0 && computeHeight > 0 ? 16 : 0);
			float chartBottom = computeY + computeHeight;
			float bottom = chartBottom + 40;
			_timelineHoverBounds = new Rect( chartX, chartY, chartWidth, chartBottom - chartY );

			painter.Stroke = Stroke.None;
			painter.Fill = new Color( 0.04f, 0.05f, 0.07f, 0.95f );
			painter.Rect( new Rect( pos.x - 12, pos.y - 12, width + 24, bottom - pos.y + 24 ) );
			painter.Clip( new Rect( pos.x, pos.y, width, bottom - pos.y ) );
			_freezeButtonBounds = new Rect( pos.x + width - 112, pos.y, 112, 28 );
			UpdateFreezeButton();
			var status = FreezeGpuTimeline ? "FROZEN" : hovering ? "HELD FOR INSPECTION" : "LIVE (4 Hz)";
			TimelineText( painter, $"GPU frame #{_timeline.FrameId}  |  {_timeline.Renderer}  |  {_timelineSpan:F3}ms captured span  |  {status}",
				Color.White, new Rect( pos.x, pos.y, width - 128, 28 ), 15, 700 );

			bool buttonHovered = InputRouter.MouseCursorVisible && _freezeButtonBounds.IsInside( Mouse.Position );
			painter.Fill = _freezeButtonPressed && buttonHovered ? new Color( 0.16f, 0.32f, 0.48f )
				: buttonHovered ? new Color( 0.26f, 0.44f, 0.62f ) : new Color( 0.16f, 0.24f, 0.32f );
			painter.Stroke = Stroke.Solid( Color.White.WithAlpha( buttonHovered ? 0.8f : 0.3f ), 1 );
			painter.Rect( _freezeButtonBounds );
			painter.Stroke = Stroke.None;
			TimelineText( painter, FreezeGpuTimeline ? "Resume" : "Freeze", Color.White, _freezeButtonBounds,
				alignment: TextFlag.Center );

			int ticks = Math.Clamp( (int)(chartWidth / 140), 2, 10 );
			for ( int i = 0; i <= ticks; i++ )
			{
				float x = chartX + chartWidth * i / ticks;
				painter.Fill = Color.White.WithAlpha( 0.10f );
				painter.Rect( new Rect( x - 1, chartY, 1, chartBottom - chartY ) );
				TimelineText( painter, $"{_timelineSpan * i / ticks:F3}ms", Color.White.WithAlpha( 0.65f ),
					new Rect( i == ticks ? x - 90 : x, pos.y + 34, 90, 24 ), 12,
					alignment: i == ticks ? TextFlag.RightCenter : TextFlag.LeftCenter );
			}

			if ( graphicsHeight > 0 )
			{
				TimelineText( painter, "Graphics", Color.White, new Rect( pos.x, chartY, TimelineLabelWidth, TimelineRowHeight ) );
			}
			if ( computeHeight > 0 )
			{
				TimelineText( painter, "Async compute", Color.White, new Rect( pos.x, computeY, TimelineLabelWidth, TimelineRowHeight ) );
			}

			Rect BarRect( TimelineBar bar )
			{
				float left = chartX + (float)((bar.Event.StartMs - _timelineStart) / _timelineSpan) * chartWidth;
				float right = chartX + (float)((bar.Event.EndMs - _timelineStart) / _timelineSpan) * chartWidth;
				return new Rect( left, (bar.Queue == 0 ? chartY : computeY) + bar.Row * TimelineRowHeight,
					MathF.Max( 1, right - left ), TimelineRowHeight - 3 );
			}

			// Give subpixel scopes a small hit target without changing their displayed duration.
			float nearest = 3;
			_hoveredTimelineBar = -1;
			for ( int i = 0; i < _timelineBars.Count && InputRouter.MouseCursorVisible; i++ )
			{
				var rect = BarRect( _timelineBars[i] );
				var mouse = Mouse.Position;
				if ( mouse.y < rect.Top || mouse.y >= rect.Bottom ) continue;
				float distance = MathF.Max( 0, MathF.Max( rect.Left - mouse.x, mouse.x - rect.Right ) );
				if ( distance < nearest )
				{
					nearest = distance;
					_hoveredTimelineBar = i;
				}
			}

			for ( int i = 0; i < _timelineBars.Count; i++ )
			{
				var bar = _timelineBars[i];
				var rect = BarRect( bar );
				bool selected = i == _hoveredTimelineBar;
				painter.Fill = selected ? bar.Color.Lighten( 0.25f ) : bar.Color.WithAlpha( 0.85f );
				painter.Stroke = selected ? Stroke.Solid( Color.White, 1 ) : Stroke.None;
				painter.Rect( rect );
				if ( rect.Width >= 48 )
				{
					TimelineText( painter, bar.Label, Color.Black, new Rect( rect.Left + 5, rect.Top, rect.Width - 10, rect.Height ) );
				}
			}
			painter.Stroke = Stroke.None;

			int omitted = _hiddenTimelinePasses + _timeline.DroppedEvents;
			var footer = "Elapsed GPU scopes, not additive  |  Offsets from first pass  |  overlay_gpu_freeze 1 to hold";
			if ( omitted > 0 ) footer += $"  |  {omitted} events omitted";
			TimelineText( painter, footer, Color.White.WithAlpha( 0.65f ), new Rect( pos.x, chartBottom + 10, width, 24 ), 12 );

			pos.y = bottom + 32;
		}

		internal static void DrawTooltip( Painter painter )
		{
			if ( overlay_gpu_timeline != 1 || !InputRouter.MouseCursorVisible || _hoveredTimelineBar < 0
				|| _hoveredTimelineBar >= _timelineBars.Count || Screen.Width < 160 || Screen.Height < 160 )
			{
				return;
			}

			var bar = _timelineBars[_hoveredTimelineBar];
			var e = bar.Event;
			float width = MathF.Min( 620, Screen.Width - 24 );
			float height = MathF.Min( 196, Screen.Height - 24 );
			var mouse = Mouse.Position;
			float x = mouse.x + 16;
			float y = mouse.y + 20;
			if ( x + width > Screen.Width - 12 ) x = mouse.x - width - 16;
			if ( y + height > Screen.Height - 12 ) y = mouse.y - height - 16;
			x = Math.Clamp( x, 12, Screen.Width - width - 12 );
			y = Math.Clamp( y, 12, Screen.Height - height - 12 );
			var bounds = new Rect( x, y, width, height );

			using var state = painter.Scope();
			painter.Fill = new Color( 0.055f, 0.065f, 0.09f, 0.98f );
			painter.Stroke = Stroke.Solid( bar.Color.WithAlpha( 0.8f ), 1 );
			painter.Rect( bounds );
			painter.Stroke = Stroke.None;
			painter.Clip( bounds );
			var line = new Rect( x + 12, y + 8, width - 24, 28 );
			TimelineText( painter, e.Name, bar.Color, line, 15, 700 );
			line.Position += new Vector2( 0, 28 );
			TimelineText( painter, $"Duration {e.EndMs - e.StartMs:F4}ms  |  {(bar.Queue == 0 ? "Graphics" : "Async compute")}", Color.White, line );
			line.Position += new Vector2( 0, 26 );
			TimelineText( painter, $"Start {e.StartMs - _timelineStart:F4}ms  |  End {e.EndMs - _timelineStart:F4}ms (from first pass)", Color.White, line, 12 );
			line.Position += new Vector2( 0, 26 );
			var submission = e.SubmissionId is null ? "Submission unavailable"
				: e.EndSubmissionId is not null && e.EndSubmissionId != e.SubmissionId ? $"Submissions #{e.SubmissionId} -> #{e.EndSubmissionId}" : $"Submission #{e.SubmissionId}";
			TimelineText( painter, $"Frame #{_timeline.FrameId}  |  {submission}", Color.White.WithAlpha( 0.65f ), line, 12 );
			line.Position += new Vector2( 0, 30 );
			line.Height = 64;
			painter.Clip( line );
			painter.TextStyle = new TextStyle( "Roboto Mono", 12, Color.White.WithAlpha( 0.8f ) )
			{
				Alignment = TextFlag.LeftTop | TextFlag.WordWrap
			};
			painter.Text( bar.Hierarchy, line );
		}

		private static void TimelineText( Painter painter, string text, Color color, Rect rect, float size = 13, int weight = 600, TextFlag alignment = TextFlag.LeftCenter )
		{
			using var clip = painter.Scope();
			painter.Clip( rect );
			var scope = new TextRendering.Scope( text, color, size, "Roboto Mono", weight );
			DebugOverlay.DrawText( painter, scope, rect, alignment );
		}
	}
}
