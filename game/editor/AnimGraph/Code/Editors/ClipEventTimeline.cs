using Editor.NodeEditor;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// The event tracks laid over an animation clip - what fires when, and which track the clip is
/// divided into phases by. Lanes are measured in the clip's own frames, so an event sits where it
/// was seen to happen.
/// </summary>
internal sealed class ClipEventTimeline : Widget
{
	private readonly ClipEventLanes _lanes;
	private readonly ClipEventRuler _ruler;
	private readonly ClipEventProperties _properties;
	private readonly ScrollArea _scroll;
	private Label _status;
	private IconButton _addTrack;
	private IconButton _addEvent;
	private IconButton _trackOptions;

	private ClipDocument _document;
	private int _frameCount;

	private ClipDocument.Track _track;
	private ClipDocument.Event _event;

	/// <summary>
	/// Called once an edit is finished, never part way through one, naming the change so it can
	/// be undone. The document isn't written here - whoever owns it decides when to save.
	/// </summary>
	public Action<string> Edited { get; set; }

	/// <summary>
	/// Dragging the ruler scrubs the preview, so events can be placed against what's on screen.
	/// </summary>
	public Action<float> Scrubbed { get; set; }

	public ClipEventTimeline( Widget parent ) : base( parent )
	{
		Name = "ClipEvents";
		WindowTitle = "Events";
		SetWindowIcon( "bolt" );

		Layout = Layout.Column();

		_lanes = new ClipEventLanes( this )
		{
			Selected = Select,
			AddEvent = AddEvent,
			DeleteEvent = DeleteEvent,
			ContextMenu = OpenContextMenu,
			Copy = CopySelection,
			Paste = () => PasteEvents( Playhead * LastFrame ),
			Scrubbed = Seek,
			Edited = () =>
			{
				Commit( "Move Event" );
				ShowProperties();
			}
		};

		Layout.Add( BuildToolbar() );

		_ruler = new ClipEventRuler( this, _lanes ) { Scrubbed = Seek };
		Layout.Add( _ruler );

		_scroll = Layout.Add( new ScrollArea( this ) );
		_scroll.VerticalScrollbarMode = ScrollbarMode.Auto;
		_scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		_scroll.Canvas = _lanes;
		VerticalSizeMode = SizeMode.CanGrow;

		_properties = Layout.Add( new ClipEventProperties( this )
		{
			Edited = () =>
			{
				Commit( "Edit Event" );
				_lanes.Update();
				if ( _event is not null )
					Seek( _event.StartFrame / LastFrame );
			},
			Deleted = DeleteEvent
		} );

		Rebuild();
	}

	/// <summary>
	/// What the tracks as a whole are up to, and the one thing you can do to them - the dock's own
	/// title says what this is, so the bar doesn't repeat it.
	/// </summary>
	private Widget BuildToolbar()
	{
		var bar = new Widget( this ) { VerticalSizeMode = SizeMode.CanGrow };

		bar.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( bar.LocalRect );
			return true;
		};

		var row = bar.Layout = Layout.Row();
		row.Margin = new Sandbox.UI.Margin( 8, 0, 4, 2 );
		row.Spacing = 4;
		row.Alignment = TextFlag.LeftCenter;

		_status = row.Add( new Label( this ) { Color = Theme.TextControl.WithAlpha( 0.6f ), WordWrap = true, HorizontalSizeMode = SizeMode.Flexible | SizeMode.Ignore }, 1 );
		_status.ToolTip = TracksHint;

		_addTrack = row.Add( SectionHeader.CreateButton( this, "add", Theme.Green, "Add an event track", OpenAddTrackMenu ) );
		_addEvent = row.Add( SectionHeader.CreateButton( this, "add_circle_outline", Theme.Green, "Add an event at the playhead", () => AddEvent( _track, Playhead * LastFrame ) ) );
		_trackOptions = row.Add( SectionHeader.CreateButton( this, "more_horiz", Theme.TextControl, "Event options", () => OpenContextMenu( _track, Playhead * LastFrame ) ) );

		return bar;
	}

	private const string TracksHint = "<b>Events</b><p>Markup laid over the clip - one track per event type, in the clip's own frames.</p><p>Drag an event to move it, drag its right edge to give it a length.</p>";

	/// <summary>
	/// Point the timeline at a clip. The hint is shown in place of the tracks when there's nothing
	/// to author, so it can say why.
	/// </summary>
	public void SetClip( ClipDocument document, int frameCount, string emptyHint )
	{
		_document = document;
		_frameCount = frameCount;

		_lanes.Document = document;
		_lanes.FrameCount = frameCount;
		_lanes.EmptyHint = emptyHint;
		_ruler.FrameCount = frameCount;
		_properties.FrameCount = frameCount;

		Select( null, null );
		Playhead = 0f;

		Rebuild();
	}

	/// <summary>
	/// Where the preview is - a handle on the ruler and a line down the lanes, so events line up
	/// with the pose on screen.
	/// </summary>
	public float Playhead
	{
		get => _lanes.Playhead;
		set
		{
			if ( _lanes.Playhead == value )
				return;

			_lanes.Playhead = value;
			_ruler.Playhead = value;

			_lanes.Update();
			_ruler.Update();
		}
	}

	public bool HasDocument => _document is not null;

	private float LastFrame => MathF.Max( _frameCount - 1, 1f );

	private void Rebuild()
	{
		_addTrack.Enabled = HasDocument && !ReadOnly;
		_ruler.Visible = HasDocument;
		_lanes.MinimumHeight = ClipEventLayout.HeightFor( _document?.Tracks.Count ?? 0 );
		_scroll.FixedHeight = Math.Clamp( _lanes.MinimumHeight, 72, 168 );

		_lanes.Update();
		_ruler.Update();

		UpdateStatus();
		ShowProperties();
	}

	private void UpdateStatus()
	{
		var tracks = _document?.Tracks.Count ?? 0;
		var problem = ClipEventTypes.Problem( _document, LastFrame );

		_status.Color = (problem is null ? Theme.TextControl : Theme.Yellow).WithAlpha( problem is null ? 0.6f : 0.9f );
		_status.Text = problem
			?? (tracks > 0 ? $"{tracks} track{(tracks == 1 ? "" : "s")}, {_frameCount} frames" : "Add a track to start placing events.");
	}

	private void ShowProperties()
	{
		_addEvent.Enabled = !ReadOnly && IsLive( _track );
		_trackOptions.Enabled = HasDocument;
		_properties.Enabled = !ReadOnly;
		_properties.Show( _event, _track?.EventClass, HasDocument ? "Select an event to edit it" : "" );
	}

	private void Select( ClipDocument.Track track, ClipDocument.Event item )
	{
		_track = track;
		_event = item;

		_lanes.SelectedTrack = track;
		_lanes.SelectedEvent = item;

		ShowProperties();
		_lanes.Update();
		if ( item is not null )
			Seek( item.StartFrame / LastFrame );
	}

	private void Seek( float position )
	{
		Playhead = position.Clamp( 0f, 1f );
		Scrubbed?.Invoke( Playhead );
	}

	/// <summary>
	/// Show the document again after something else changed it, like an undo. Everything holding
	/// a track or an event is rebuilt from the document, since restoring one replaces them all.
	/// </summary>
	public void Refresh()
	{
		Select( null, null );
		Rebuild();
	}

	private void Commit( string name )
	{
		UpdateStatus();
		Edited?.Invoke( name );
	}

	/// <summary>
	/// Whether a track is still part of the clip we're showing. A menu or a popup can come back
	/// after the editor has moved on to another clip, or to none.
	/// </summary>
	private bool IsLive( ClipDocument.Track track ) => track is not null && _document is not null && _document.Tracks.Contains( track );

	private void OpenAddTrackMenu()
	{
		if ( ReadOnly )
			return;

		var menu = new Menu( this );

		menu.AddOption( "ID Events", "label_important", () => AddTrack( ClipEventTypes.ID ) );
		menu.AddOption( "Generic Events", "bolt", () => AddTrack( ClipEventTypes.Generic ) );
		menu.AddOption( "Foot Events", "directions_walk", () => AddTrack( ClipEventTypes.Foot ) );

		menu.OpenAtCursor();
	}

	private void AddTrack( string eventClass )
	{
		// Menus and popups outlive the clip they were opened on
		if ( _document is null || ReadOnly )
			return;

		var track = _document.AddTrack( eventClass );
		Select( track, null );

		Commit( "Add Track" );
		Rebuild();
	}

	private void RemoveTrack( ClipDocument.Track track )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;

		_document.RemoveTrack( track );

		if ( _track == track )
			Select( null, null );

		Commit( "Delete Track" );
		Rebuild();
	}

	/// <summary>
	/// Nothing here goes through the graph's undo, so a track with work in it asks first.
	/// </summary>
	private void DeleteTrack( ClipDocument.Track track )
	{
		if ( track.Events.Count == 0 )
		{
			RemoveTrack( track );
			return;
		}

		var confirm = new PopupWindow( "Delete Track",
			$"Delete this {ClipEventTypes.Title( track.EventClass )} track?\n\nThis also deletes {track.Events.Count} event{(track.Events.Count == 1 ? "" : "s")}.",
			"Cancel", new Dictionary<string, Action> { { "Delete", () => RemoveTrack( track ) } } );

		confirm.Show();
		confirm.Window.Position = ScreenRect.Center - confirm.Window.Size * 0.5f;
	}

	private void AddEvent( ClipDocument.Track track, float frame )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;

		Select( track, track.AddEvent( MathF.Round( frame ) ) );

		Commit( "Add Event" );
		Rebuild();
	}

	private void DeleteEvent()
	{
		if ( _event is null || !IsLive( _track ) || ReadOnly )
			return;

		_track.RemoveEvent( _event );
		Select( _track, null );

		Commit( "Delete Event" );
		Rebuild();
	}

	/// <summary>
	/// Only one track can be the sync track - the compiler takes the first and warns about the rest.
	/// </summary>
	private void ToggleSyncTrack( ClipDocument.Track track )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;

		_document.SetSyncTrack( track, !track.IsSyncTrack );

		Commit( "Set Sync Track" );
		_lanes.Update();
	}

	private void ToggleDisabled( ClipDocument.Track track )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;

		track.IsDisabled = !track.IsDisabled;

		Commit( "Toggle Track" );
		_lanes.Update();
	}

	private void OpenContextMenu( ClipDocument.Track track, float frame )
	{
		if ( !HasDocument )
			return;

		var menu = new Menu( this );

		if ( IsLive( track ) )
		{
			if ( _event is not null && track.Events.Contains( _event ) )
				menu.AddOption( "Copy Event", "content_copy", () => CopyEvents( track, _event ), "Ctrl+C" );
			menu.AddOption( "Copy Track", "content_copy", () => CopyEvents( track ) );
		}
		menu.AddOption( "Copy All Events", "content_copy", () => CopyEvents() ).Enabled = _document.Tracks.Count > 0;
		menu.AddOption( "Paste Events", "content_paste", () => PasteEvents( frame ), "Ctrl+V" ).Enabled = !ReadOnly
			&& (GraphClipboard.CanPaste( TracksClipboardIdent ) || GraphClipboard.CanPaste( EventClipboardIdent ));
		if ( ReadOnly )
		{
			menu.OpenAtCursor();
			return;
		}
		menu.AddSeparator();

		if ( track is null )
		{
			menu.AddOption( "Add Track", "add", OpenAddTrackMenu );
			menu.OpenAtCursor();
			return;
		}

		if ( _event is not null )
		{
			menu.AddOption( "Delete Event", "delete", DeleteEvent, "Del" );
		}
		else
		{
			menu.AddOption( "Add Event", "add", () => AddEvent( track, frame ) );
		}

		menu.AddSeparator();

		var sync = menu.AddOption( "Sync Track", "sync", () => ToggleSyncTrack( track ) );
		sync.Checkable = true;
		sync.Checked = track.IsSyncTrack;
		sync.ToolTip = "Divide the clip into phases at this track's events, so clips of different lengths can be blended by phase";

		var disabled = menu.AddOption( "Disabled", "block", () => ToggleDisabled( track ) );
		disabled.Checkable = true;
		disabled.Checked = track.IsDisabled;
		disabled.ToolTip = "Keep the track's events but compile none of them";

		menu.AddSeparator();
		menu.AddOption( "Delete Track", "delete_sweep", () => DeleteTrack( track ) );

		menu.OpenAtCursor();
	}

	private const string TracksClipboardIdent = "animgraph-clip-tracks";
	private const string EventClipboardIdent = "animgraph-clip-event";

	private void CopySelection() => CopyEvents( _track, _event );

	private void CopyEvents( ClipDocument.Track track = null, ClipDocument.Event item = null )
	{
		if ( !HasDocument || (track is not null && !IsLive( track )) || (item is not null && (track is null || !track.Events.Contains( item ))) )
			return;

		var copy = ClipDocument.Create();
		if ( item is not null )
			copy.AddTrack( track.EventClass ).AddEvent( item );
		else
			foreach ( var source in track is null ? _document.Tracks : [track] )
				copy.AddTrack( source );

		GraphClipboard.Copy( item is null ? TracksClipboardIdent : EventClipboardIdent, copy.SerializeEvents() );
	}

	private void PasteEvents( float frame )
	{
		if ( !HasDocument || ReadOnly )
			return;

		var singleEvent = GraphClipboard.CanPaste( EventClipboardIdent );
		if ( !GraphClipboard.TryPaste( singleEvent ? EventClipboardIdent : TracksClipboardIdent, out var json ) )
			return;

		try
		{
			if ( JsonNode.Parse( json ) is not JsonArray tracks )
				throw new JsonException( "Expected event tracks." );

			var copy = ClipDocument.Create();
			copy.Restore( json );
			if ( copy.Tracks.Count == 0 || copy.Tracks.Count != tracks.Count
				|| (singleEvent && (copy.Tracks.Count != 1 || copy.Tracks[0].Events.Count != 1)) )
				throw new JsonException( "Clipboard contains no valid events." );

			foreach ( var track in copy.Tracks )
			{
				foreach ( var item in track.Events )
				{
					item.StartFrame = (singleEvent ? MathF.Round( frame ) : item.StartFrame).Clamp( 0, LastFrame );
					item.DurationFrames = item.DurationFrames.Clamp( 0, LastFrame - item.StartFrame );
				}
			}

			if ( singleEvent )
			{
				var source = copy.Tracks[0];
				var target = IsLive( _track ) && _track.EventClass == source.EventClass ? _track
					: _document.Tracks.FirstOrDefault( track => track.EventClass == source.EventClass ) ?? _document.AddTrack( source.EventClass );
				var item = target.AddEvent( source.Events[0] );
				Select( target, item );
			}
			else
			{
				ClipDocument.Track first = null;
				foreach ( var source in copy.Tracks )
				{
					var added = _document.AddTrack( source );
					if ( added.IsSyncTrack )
						_document.SetSyncTrack( added, true );
					first ??= added;
				}
				Select( first, null );
			}
			Commit( "Paste Events" );
			Rebuild();
		}
		catch ( JsonException exception )
		{
			Log.Warning( $"Couldn't paste animation events: {exception.Message}" );
			_status.Text = "Couldn't paste animation events: invalid clipboard data.";
			_status.Color = Theme.Red;
		}
	}
}
