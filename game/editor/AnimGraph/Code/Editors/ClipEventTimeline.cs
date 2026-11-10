using Editor.NodeEditor;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

internal sealed class ClipEventTimeline : Widget
{
	public Action<string> Edited { get; set; }
	public Action<float> Scrubbed { get; set; }
	public Action<ClipDocument.Track, ClipDocument.Event> Selected { get; set; }
	public Action EditRequested { get; set; }
	public Action FinishEdit { get; set; }

	private readonly ClipEventLanes _lanes;
	private readonly Widget _sidebar;
	private readonly Widget _transportGroup;
	private readonly ClipEventRuler _ruler;
	private readonly ClipEventRuler _frameRuler;
	private readonly Label _status;
	private readonly Label _frameRate;
	private readonly IconButton _addTrack;
	private readonly IconButton _addEvent;
	public float Duration { get; private set; }
	private ClipDocument _document;
	private ClipDocument.Track _track;
	private ClipDocument.Event _event;
	private int _frameCount;
	private string _emptyHint;
	private float LastFrame => MathF.Max( _frameCount - 1, 1 );
	private int _trackIndex = -1;
	private int _eventIndex = -1;

	public ClipEventTimeline( Widget parent ) : base( parent )
	{
		Name = "ClipEvents";
		Layout = Layout.Column();
		Layout.Spacing = 0;
		Layout.Margin = 0;
		VerticalSizeMode = SizeMode.Flexible;
		MinimumHeight = 220;

		var header = Layout.Add( new Widget( this ) { Layout = Layout.Row() } );
		header.Layout.Spacing = 2;
		header.Layout.Margin = new Sandbox.UI.Margin( 0, 2 );
		header.OnPaintOverride = () =>
		{
			Paint.SetBrushAndPen( Theme.TabBackground );
			Paint.DrawRect( header.LocalRect );
			return true;
		};
		_sidebar = header.Layout.Add( new Widget( header ) { Layout = Layout.Row() } );
		_sidebar.Layout.Spacing = 2;
		_sidebar.FixedWidth = ClipEventLayout.HeaderWidth - 2;
		var trackGroup = AddGroup( _sidebar );
		trackGroup.HorizontalSizeMode = SizeMode.CanShrink;
		_addTrack = AddButton( trackGroup, "playlist_add", "Add a track", OpenAddTrackMenu );
		var titleGroup = AddGroup( _sidebar );
		titleGroup.HorizontalSizeMode = SizeMode.Flexible;
		titleGroup.Layout.Add( new Label( "Animation Events" ) { Margin = 2, Color = Theme.TextControl, Alignment = TextFlag.Center }, 1 );
		_addEvent = AddButton( titleGroup, "add", "Add an event at the playhead", AddEventAtPlayhead );
		_transportGroup = AddGroup( header );
		_transportGroup.HorizontalSizeMode = SizeMode.CanShrink;
		_transportGroup.Visible = false;
		header.Layout.AddStretchCell();
		_lanes = new ClipEventLanes( this )
		{
			Selected = Select,
			AddEvent = ( track, frame ) => AddEvent( track, frame ),
			EditEvent = () => EditRequested?.Invoke(),
			DeleteEvent = DeleteEvent,
			ContextMenu = OpenContextMenu,
			Copy = () => CopyEvents( _track, _event ),
			Paste = () => PasteEvents( Playhead * LastFrame ),
			Scrubbed = Seek,
			Edited = () => Commit( "Move Event" ),
			HeightChanged = UpdateRulers
		};
		var options = AddGroup( header );
		var snap = AddButton( options, "straighten", "Snap to frames. Hold Ctrl while dragging to bypass.", null );
		snap.IsToggle = true;
		snap.IsActive = true;
		snap.OnToggled = value => _lanes.SnapToFrames = value;
		_frameRate = options.Layout.Add( new Label { Margin = 2, Color = Theme.TextControl.WithAlpha( 0.5f ) } );
		AddButton( AddGroup( header ), "fit_screen", "Fit the clip in the timeline (F)", _lanes.Fit );
		_ruler = Layout.Add( new ClipEventRuler( this, _lanes, true ) );
		var scroll = Layout.Add( new ScrollArea( this )
		{
			VerticalScrollbarMode = ScrollbarMode.Auto,
			HorizontalScrollbarMode = ScrollbarMode.Off,
			NoSystemBackground = true,
			TranslucentBackground = true,
			MinimumHeight = 80,
			Canvas = _lanes
		}, 1 );
		scroll.SetStyles( "QScrollArea { border: 0px; }" );
		_lanes.Scrolled = delta => scroll.VerticalScrollbar.Value += (int)delta;
		_frameRuler = Layout.Add( new ClipEventRuler( this, _lanes, false ) );
		_status = Layout.Add( new Label( this ) { Color = Theme.TextControl, WordWrap = true, Margin = 2 } );
		UpdateView();
	}

	private static Widget AddGroup( Widget toolbar )
	{
		var group = toolbar.Layout.Add( new Widget( toolbar ) { Layout = Layout.Row(), HorizontalSizeMode = SizeMode.CanGrow } );
		group.Layout.Spacing = 2;
		group.Layout.Margin = 2;
		group.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SidebarBackground );
			Paint.DrawRect( group.LocalRect, 3 );
			return true;
		};
		return group;
	}

	private static IconButton AddButton( Widget group, string icon, string tooltip, Action action ) =>
		group.Layout.Add( new IconButton( icon, action )
		{
			ToolTip = tooltip,
			IconSize = 16,
			Background = Color.Transparent,
			BackgroundActive = Color.Transparent,
			ForegroundActive = Theme.Primary
		} );

	private void UpdateRulers()
	{
		_sidebar.FixedWidth = MathF.Max( _lanes.HeaderWidth - 2, 120 );
		_ruler?.Update();
		_frameRuler?.Update();
	}

	public float Playhead
	{
		get => _lanes.Playhead;
		set
		{
			_lanes.Playhead = value.Clamp( 0, 1 );
			_lanes.UpdateTracks( false );
			UpdateRulers();
		}
	}

	public void SetClip( ClipDocument document, int frameCount, float duration, string emptyHint )
	{
		FinishEditing();
		if ( _document != document )
			_trackIndex = _eventIndex = -1;
		_document = document;
		_frameCount = frameCount;
		_emptyHint = emptyHint;
		Duration = duration;
		_lanes.FrameRate = duration > 0 ? LastFrame / duration : 30;
		_frameRate.Text = $"{_lanes.FrameRate:0.##} fps";
		Refresh();
	}

	public void Refresh()
	{
		_lanes.SetClip( _document, _frameCount, _emptyHint );
		var track = _document?.Tracks.ElementAtOrDefault( _trackIndex );
		SetSelection( track, track?.Events.ElementAtOrDefault( _eventIndex ), false );
		UpdateView();
	}

	public void RefreshEvent() => UpdateView();

	public void SetTransport( Widget transport )
	{
		transport.FixedHeight = Theme.RowHeight;
		transport.Layout.Margin = 0;
		transport.Layout.Spacing = 2;
		transport.HorizontalSizeMode = SizeMode.CanShrink;
		_transportGroup.Layout.Add( transport );
		_transportGroup.Visible = true;
	}

	public void FinishEditing()
	{
		FinishEdit?.Invoke();
		_lanes.FinishDrag();
	}

	private bool IsLive( ClipDocument.Track track ) => track is not null && _document?.Tracks.Contains( track ) == true;

	internal void Select( ClipDocument.Track track, ClipDocument.Event item ) => SetSelection( track, item, true );

	private void SetSelection( ClipDocument.Track track, ClipDocument.Event item, bool reveal )
	{
		FinishEdit?.Invoke();
		_track = track;
		_event = item;
		_lanes.SelectedTrack = track;
		_lanes.SelectedEvent = item;
		_trackIndex = _document?.Tracks.IndexOf( track ) ?? -1;
		_eventIndex = track?.Events.IndexOf( item ) ?? -1;
		Selected?.Invoke( track, item );
		_addEvent.Enabled = _document is not null && !ReadOnly;
		_lanes.UpdateTracks();
		if ( reveal && item is not null )
		{
			if ( item.DurationFrames <= 0 )
				Seek( item.StartFrame / LastFrame );
			EditRequested?.Invoke();
		}
	}

	private void Seek( float position )
	{
		Playhead = position;
		Scrubbed?.Invoke( Playhead );
	}

	private void UpdateView()
	{
		_addTrack.Enabled = _document is not null && !ReadOnly;
		_addEvent.Enabled = _document is not null && !ReadOnly;
		_ruler.Visible = _document is not null;
		_frameRuler.Visible = _document is not null;
		var problem = ClipEventTypes.Problem( _document, LastFrame );
		_status.Text = problem ?? string.Empty;
		_status.Color = Theme.Yellow;
		_status.Visible = problem is not null;
		_lanes.UpdateTracks();
		UpdateRulers();
	}

	private void Commit( string name )
	{
		Edited?.Invoke( name );
		if ( IsValid )
			UpdateView();
	}

	private void OpenAddTrackMenu()
	{
		if ( _document is null || ReadOnly )
			return;
		var menu = new Menu( this );
		menu.AddOption( "Animation Events", "bolt", () => AddTrack( ClipEventTypes.Generic ) );
		menu.AddOption( "ID Events", "label_important", () => AddTrack( ClipEventTypes.ID ) );
		menu.AddOption( "Foot Events", "directions_walk", () => AddTrack( ClipEventTypes.Foot ) );
		menu.OpenAtCursor( false );
	}

	private void AddEventAtPlayhead()
	{
		if ( _document is null || ReadOnly )
			return;

		var track = IsLive( _track ) ? _track : _document.Tracks.FirstOrDefault( track => track.EventClass == ClipEventTypes.Generic );
		if ( track is null )
		{
			track = _document.AddTrack( ClipEventTypes.Generic );
			_lanes.SetClip( _document, _frameCount, _emptyHint );
		}
		AddEvent( track, Playhead * LastFrame );
	}

	private void AddTrack( string eventClass )
	{
		if ( _document is null || ReadOnly )
			return;
		var track = _document.AddTrack( eventClass );
		_lanes.SetClip( _document, _frameCount, _emptyHint );
		Select( track, null );
		Commit( "Add Track" );
	}

	private void RemoveTrack( ClipDocument.Track track )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;
		FinishEditing();
		_document.RemoveTrack( track );
		Refresh();
		Commit( "Delete Track" );
	}

	private void DeleteTrack( ClipDocument.Track track )
	{
		if ( track.Events.Count == 0 )
		{
			RemoveTrack( track );
			return;
		}
		var confirm = new PopupWindow( "Delete Track",
			$"Delete this {ClipEventTypes.Title( track.EventClass )} track and its {track.Events.Count} event{(track.Events.Count == 1 ? "" : "s")}?",
			"Cancel", new Dictionary<string, Action> { { "Delete", () => RemoveTrack( track ) } } );
		confirm.Show();
	}

	private void AddEvent( ClipDocument.Track track, float frame, string name = ClipEventTypes.ManagedName )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;
		FinishEdit?.Invoke();
		var item = track.AddEvent( (_lanes.SnapToFrames ? MathF.Round( frame ) : frame).Clamp( 0, LastFrame ) );
		if ( track.EventClass == ClipEventTypes.Generic )
			item.Name = name;
		Select( track, item );
		Commit( "Add Event" );
	}

	private void DeleteEvent()
	{
		if ( !IsLive( _track ) || _event is null || ReadOnly )
			return;
		FinishEdit?.Invoke();
		_track.RemoveEvent( _event );
		Select( _track, null );
		Commit( "Delete Event" );
	}

	private void ToggleSyncTrack( ClipDocument.Track track )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;
		_document.SetSyncTrack( track, !track.IsSyncTrack );
		Commit( "Set Sync Track" );
	}

	private void ToggleDisabled( ClipDocument.Track track )
	{
		if ( !IsLive( track ) || ReadOnly )
			return;
		track.IsDisabled = !track.IsDisabled;
		Commit( "Toggle Track" );
	}

	private void OpenContextMenu( ClipDocument.Track track, float frame )
	{
		if ( _document is null )
			return;
		var menu = new Menu( this );
		if ( IsLive( track ) )
		{
			if ( _event is not null )
			{
				menu.AddOption( "Edit Event", "edit", () => EditRequested?.Invoke(), "Enter" );
				menu.AddOption( "Copy Event", "content_copy", () => CopyEvents( track, _event ), "Ctrl+C" );
				menu.AddOption( "Delete Event", "delete", DeleteEvent, "Del" ).Enabled = !ReadOnly;
				menu.AddSeparator();
			}
			menu.AddOption( "Add Event Here", "add", () => AddEvent( track, frame ) ).Enabled = !ReadOnly;
			if ( track.EventClass == ClipEventTypes.Generic )
				menu.AddOption( "Add Native Event Here", "settings", () => AddEvent( track, frame, "" ) ).Enabled = !ReadOnly;
			menu.AddOption( "Copy Track", "content_copy", () => CopyEvents( track ) );
		}
		menu.AddOption( "Copy All Tracks", "content_copy", () => CopyEvents() ).Enabled = _document.Tracks.Count > 0;
		menu.AddOption( "Paste", "content_paste", () => PasteEvents( frame ), "Ctrl+V" ).Enabled = !ReadOnly
			&& (GraphClipboard.CanPaste( TracksClipboardIdent ) || GraphClipboard.CanPaste( EventClipboardIdent ));

		if ( IsLive( track ) )
		{
			menu.AddSeparator();
			var sync = menu.AddOption( "Sync Track", "sync", () => ToggleSyncTrack( track ) );
			sync.Checkable = true;
			sync.Checked = track.IsSyncTrack;
			sync.Enabled = !ReadOnly;
			sync.ToolTip = "Divide the clip into phases at this track's events";
			var disabled = menu.AddOption( "Disabled", "block", () => ToggleDisabled( track ) );
			disabled.Checkable = true;
			disabled.Checked = track.IsDisabled;
			disabled.Enabled = !ReadOnly;
			menu.AddSeparator();
			menu.AddOption( "Delete Track", "delete_sweep", () => DeleteTrack( track ) ).Enabled = !ReadOnly;
		}
		menu.OpenAtCursor( false );
	}

	private const string TracksClipboardIdent = "animgraph-clip-tracks";
	private const string EventClipboardIdent = "animgraph-clip-event";

	private void CopyEvents( ClipDocument.Track track = null, ClipDocument.Event item = null )
	{
		if ( _document is null || (track is not null && !IsLive( track )) || (item is not null && (track is null || !track.Events.Contains( item ))) )
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
		if ( _document is null || ReadOnly )
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
				foreach ( var item in track.Events )
				{
					item.StartFrame = (singleEvent ? (_lanes.SnapToFrames ? MathF.Round( frame ) : frame) : item.StartFrame).Clamp( 0, LastFrame );
					item.DurationFrames = item.DurationFrames.Clamp( 0, LastFrame - item.StartFrame );
				}
			FinishEdit?.Invoke();
			ClipDocument.Track selectedTrack;
			ClipDocument.Event selectedEvent = null;
			if ( singleEvent )
			{
				var source = copy.Tracks[0];
				selectedTrack = IsLive( _track ) && _track.EventClass == source.EventClass ? _track
					: _document.Tracks.FirstOrDefault( track => track.EventClass == source.EventClass ) ?? _document.AddTrack( source.EventClass );
				selectedEvent = selectedTrack.AddEvent( source.Events[0] );
			}
			else
			{
				selectedTrack = null;
				foreach ( var source in copy.Tracks )
				{
					var added = _document.AddTrack( source );
					if ( added.IsSyncTrack )
						_document.SetSyncTrack( added, true );
					selectedTrack ??= added;
				}
			}
			_lanes.SetClip( _document, _frameCount, _emptyHint );
			Select( selectedTrack, selectedEvent );
			Commit( "Paste Events" );
		}
		catch ( JsonException exception )
		{
			Log.Warning( $"Couldn't paste animation events: {exception.Message}" );
			_status.Text = "Couldn't paste animation events: invalid clipboard data.";
			_status.Color = Theme.Red;
			_status.Visible = true;
		}
	}

	public override void OnDestroyed()
	{
		FinishEditing();
		base.OnDestroyed();
	}
}
