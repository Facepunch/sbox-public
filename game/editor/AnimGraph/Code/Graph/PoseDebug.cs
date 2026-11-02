using Sandbox.Animation;

namespace Editor.AnimGraph;

public static class PoseDebug
{
	public const float Height = 88.0f;
	public const float Margin = 8.0f;

	/// <summary>Number of text readout lines (Time, Percent, Event, Event ID).</summary>
	public const int LineCount = 4;

	private static readonly Color ActiveColor = new( 50.0f / 255.0f, 205.0f / 255.0f, 50.0f / 255.0f );
	private static readonly Color AlternateEventColor = Color.Parse( "#404040" ) ?? default;
	private static readonly Color OverflowEventColor = Color.Parse( "#ff69b4" ) ?? default;

	/// <summary>
	/// Retain a consumer's own copy so refilling a shared sample cannot hide changes from it.
	/// </summary>
	internal static bool UpdateEvents( ref GraphDebug.SyncEvent[] destination, GraphDebug.SyncEvent[] source )
	{
		if ( destination.AsSpan().SequenceEqual( source ) )
			return false;

		if ( destination is null || destination.Length != source.Length )
			destination = source.Length == 0 ? [] : new GraphDebug.SyncEvent[source.Length];

		source.CopyTo( destination, 0 );
		return true;
	}

	/// <summary>
	/// Paints the sync-track bar with per-event blocks and the live progress marker. The
	/// graphical part of the pose debug, shared by the graph canvas and the properties dock.
	/// </summary>
	public static void PaintBar(
		Rect rect,
		bool hasInfo,
		GraphDebug.NodeInfo info,
		IReadOnlyList<GraphDebug.SyncEvent> events,
		float scale = 1.0f )
	{
		var barHeight = 12.0f * scale;
		var markerSize = 4.0f * scale;
		var bar = new Rect( rect.Left, rect.Top, rect.Width, barHeight );

		Paint.ClearPen();

		if ( !hasInfo )
		{
			Paint.SetBrush( AlternateEventColor );
			Paint.DrawRect( bar );
			return;
		}

		for ( var i = 0; i < events.Count; i++ )
		{
			var syncEvent = events[i];
			var eventRect = new Rect(
				bar.Left + MathF.Round( bar.Width * syncEvent.StartTime ),
				bar.Top,
				MathF.Round( bar.Width * syncEvent.Duration ),
				bar.Height );

			eventRect.Right = MathF.Min( eventRect.Right, bar.Right );

			if ( i == 0 && syncEvent.StartTime != 0.0f )
			{
				Paint.SetBrush( events.Count % 2 == 0 ? AlternateEventColor : Color.White );
				Paint.DrawRect( new Rect( bar.Left, bar.Top, eventRect.Left - bar.Left, bar.Height ) );

				Paint.SetPen( OverflowEventColor, 2.0f * scale );
				Paint.DrawLine( eventRect.TopLeft, eventRect.BottomLeft );
				Paint.ClearPen();
			}

			Paint.SetBrush( i % 2 == 0 ? Color.White : AlternateEventColor );
			Paint.DrawRect( eventRect );
		}

		var progress = info.NormalizedTime.Clamp( 0.0f, 1.0f );
		var progressX = bar.Left + MathF.Floor( bar.Width * progress );

		Paint.SetBrush( ActiveColor.WithAlpha( 0.65f ) );
		Paint.DrawRect( new Rect( bar.Left, bar.Top, progressX - bar.Left, bar.Height ) );

		Paint.SetPen( ActiveColor, 1.0f * scale );
		Paint.DrawLine( new Vector2( progressX, bar.Top ), new Vector2( progressX, bar.Bottom ) );

		Paint.ClearPen();
		Paint.SetBrush( ActiveColor );
		Paint.DrawPolygon(
			new Vector2( progressX, bar.Bottom ),
			new Vector2( progressX - markerSize, bar.Bottom + markerSize ),
			new Vector2( progressX + markerSize, bar.Bottom + markerSize ) );
	}

	/// <summary>
	/// One line of the pose text readout, or an "N/A" placeholder when not live.
	/// </summary>
	public static string InfoLine( int index, bool hasInfo, GraphDebug.NodeInfo info, string currentEventID )
	{
		if ( !hasInfo )
		{
			return index switch
			{
				0 => "Time: N/A",
				1 => "Percent: N/A",
				2 => "Event: N/A",
				_ => "Event ID: N/A",
			};
		}

		return index switch
		{
			0 => $"Time: {info.NormalizedTime * info.Duration:0.00}/{info.Duration:0.00}s",
			1 => $"Percent: {info.NormalizedTime * 100.0f:0.0}%",
			2 => $"Event: {info.SyncEventIndex}, {info.SyncEventProgress * 100.0f:0.0}%",
			_ => $"Event ID: {currentEventID}",
		};
	}

	/// <summary>
	/// Paints the bar and its text readout. Shared by the zoomable graph canvas and the
	/// properties dock's Debug section (see <see cref="BuildDebugWidgets" />).
	/// </summary>
	public static void PaintDebugInfo(
		Rect rect,
		bool hasInfo,
		GraphDebug.NodeInfo info,
		IReadOnlyList<GraphDebug.SyncEvent> events,
		string currentEventID,
		float scale = 1.0f )
	{
		PaintBar( rect, hasInfo, info, events, scale );

		var textTop = rect.Top + (12.0f + 4.0f + 2.0f) * scale;
		var lineHeight = 16.0f * scale;

		Paint.ClearBrush();
		Paint.SetPen( Theme.Text );
		Paint.SetDefaultFont( 8.0f * scale );

		for ( var i = 0; i < LineCount; i++ )
		{
			Paint.DrawText(
				new Rect( rect.Left, textTop + lineHeight * i, rect.Width, lineHeight ),
				InfoLine( i, hasInfo, info, currentEventID ),
				TextFlag.LeftCenter );
		}
	}

	/// <summary>Adds the live sync-track bar and text readout to a node's Debug section.</summary>
	public static void BuildDebugWidgets( Widget parent, Node node )
		=> parent.Layout.Add( new InfoCanvas( parent, node ) );

	private sealed class InfoCanvas( Widget parent, Node node ) : LiveCanvas( parent, PoseDebug.Height )
	{
		protected override void PaintContent( Rect rect )
			=> PaintDebugInfo( rect, node.IsPoseLive, node.LiveDebugInfo, node.LiveSyncTrackEvents, node.LiveSyncEventID );
	}
}
