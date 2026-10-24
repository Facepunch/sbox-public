using Sandbox.UI;
using static Sandbox.Diagnostics.DevTools.NetworkLog;

namespace Sandbox.Diagnostics;

/// <summary>
/// Per-object sync vars, RPCs and dormancy, with snapshots and session events in a shared lane.
/// </summary>
internal sealed class DevToolsNetTimeline : Panel
{
	internal sealed class Track
	{
		public string Name;
		public Kind Kind;
		public Color Color;
		public string Component;
		public string Label;
		public string GroupedLabel;
		public string ComponentExpanded;
		public string ComponentCollapsed;
		public readonly List<Entry> Events = new();
	}

	internal sealed class Group
	{
		public Guid Id;
		public string Name;
		public string Owner;
		public DevTools.NetDormancy Dormancy;
		public string ExpandedTitle;
		public string CollapsedTitle;
		public readonly List<Track> Tracks = new();
		(string Name, string Owner, DevTools.NetDormancy Dormancy) _titled;

		public void UpdateTitles()
		{
			if ( ExpandedTitle is not null && _titled == (Name, Owner, Dormancy) ) return;
			_titled = (Name, Owner, Dormancy);

			var dormancy = Dormancy.IsAsleep() ? " - Dormant" : "";
			var title = $"{Name}{(Owner is null ? "" : $" - {Owner}")}{dormancy}";
			ExpandedTitle = $"▾ {title}";
			CollapsedTitle = $"▸ {title}";
		}
	}

	internal readonly List<Group> Groups = new();

	internal bool GroupByComponent { get; private set; } = true;

	internal readonly HashSet<(Guid Id, string Component)> CollapsedComponents = new();
	internal readonly HashSet<Guid> Collapsed = new();
	internal readonly List<Entry> SessionEvents = new();
	DevTools.NetworkLog.Show _show = new( Sync: true, Rpc: true, Dormancy: true, Snapshots: true, Transforms: false, Resends: false, ActiveOnly: true );

	(Guid Id, string ObjectName, string Track)? _pin;
	internal Entry? Selected;

	internal bool HasEvents => _events.Count > 0;

	readonly List<Entry> _events = new();
	readonly List<Entry> _shown = new();
	readonly List<Entry> _matchingPinnedEvents = new();
	readonly Dictionary<Guid, Group> _groups = new();
	readonly DevTools.NetworkLog.Selection _selection = new();
	readonly Dictionary<(Guid ObjectId, string Name, Kind Kind), Track> _tracks = new();
	string _filterText;
	Filter _parsedFilter = Filter.Parse( null );
	readonly List<string> _shownKinds = new( 6 );
	DevTools.NetworkLog.Show? _summarized;

	bool _refreshing;
	int _clears = Clears;
	DateTime _since = SessionEnded;
	bool _focusPending;
	string _selectedRowKey;
	long _copied;
	bool _fullCopy = true;
	DevTools.NetworkLog.Show _selectedShow;

	const int MaxCopied = Capacity + Capacity / 4;

	const int MaxPinnedRows = 200;
	static readonly string[] SlotKeys = Enumerable.Range( 0, MaxPinnedRows ).Select( i => $"{i}" ).ToArray();
	readonly Entry[] _slotEntries = new Entry[MaxPinnedRows];
	readonly Action[] _slotClicks = new Action[MaxPinnedRows];

	readonly DevToolsFacts _facts;
	readonly TextEntry _filter;
	readonly Label _showText;
	readonly DevToolsNetTimelineView _view;
	readonly DevToolsSection _pinnedSection;
	readonly SplitContainer _split;
	readonly Label _live;
	readonly DevToolsTable _pinTable;
	Length? _dockSplit;

	const float DefaultDockHeight = 260f;
	const float MinDockHeight = 80f;
	const float MinTimelineHeight = 120f;

	public DevToolsNetTimeline()
	{
		AddClass( "nettl-page" );
		_facts = AddChild( new DevToolsFacts() );
		var bar = AddChild( new Panel( null, "nettl-bar" ) );
		_filter = bar.AddChild( new TextEntry() );
		_filter.AddClass( "nettl-filter" );
		_filter.Placeholder = "Filter: words, name:, tag:, owner:, comp:, id:  (a,b any of; * ? wildcards)";
		_filter.AddEventListener( "onchange", Rebuild );
		_filter.Tooltip = "Space-separated, all must match. Commas inside one mean any of: name:Bot*,Manager. Plain words match object or track names; name:, tag:, owner:, comp:, id: match one thing. * and ? are wildcards over the whole text: Crate*, tag:pla*, comp:Timeline*";

		var show = bar.AddChild( new Panel( null, "nettl-select" ) );
		show.Tooltip = "Which events the timeline shows";
		_showText = show.AddChild( new Label( "", "nettl-select-text" ) );
		show.AddChild( new Label( "expand_more", "nettl-select-icon" ) );
		show.AddEventListener( "onclick", () => OpenShowMenu( show ) );

		var copy = bar.AddChild( new Label( "content_copy", "icon-button" ) );
		copy.Tooltip = "Copy the events shown, newest first, tab separated. Paused, it's what the timeline holds.";
		copy.AddEventListener( "onclick", CopyShown );

		_live = bar.AddChild( new Label( "fast_forward", "icon-button" ) );
		_live.Tooltip = "Back to now. Panning holds the view where it is while new events keep coming in.";
		_live.AddEventListener( "onclick", () => _view.Follow() );

		var clear = bar.AddChild( new Label( "delete_sweep", "icon-button" ) );
		clear.Tooltip = "Clear the network log";
		clear.AddEventListener( "onclick", () =>
		{
			DevTools.NetworkLog.Clear();
			Refresh();
		} );

		_split = AddChild( new SplitContainer { Vertical = true } );
		_split.AddClass( "nettl-split" );
		_split.Splitter.AddClass( "nettl-grip" );
		_split.Splitter.AddChild( new Panel( null, "nettl-grip-bar" ) );
		_split.Splitter.Tooltip = "Drag to resize, double-click to reset";
		_split.Splitter.AddEventListener( "ondoubleclick", ResetDock );

		var section = _split.Left.AddChild( new DevToolsSection( "Timeline", "nettl-fill" ) );
		section.Aside.Text = "wheel zoom - drag pan - scrollbar scrolls - click event, track or object to pin, a component to fold";
		_view = section.Body.AddChild( new DevToolsNetTimelineView( this ) );
		_pinnedSection = _split.Right.AddChild( new DevToolsSection( "Pinned", "nettl-dock" ) );
		var unpin = _pinnedSection.Head.AddChild( new Label( "close", "icon-button nettl-unpin" ) );
		unpin.Tooltip = "Unpin";
		unpin.AddEventListener( "onclick", ClearPin );
		_pinTable = _pinnedSection.Body.AddChild( new DevToolsTable( "64px 64px minmax(0, 1.3fr) minmax(0, 0.9fr) 56px minmax(0, 1.4fr)", "Time", "Dir", "Track", "Peer", "#Bytes", "Value" ) );
		ShowDock( false );
		for ( int i = 0; i < MaxPinnedRows; i++ )
		{
			var slot = i;
			_slotClicks[i] = () => SelectPinned( _slotEntries[slot] );
		}
	}

	void ResetDock()
	{
		var height = _split.Box.Rect.Height / ScaleToScreen;
		_split.UpdateSplitFraction( height > 0 ? 1 - DefaultDockHeight / height : 0.6f );
	}

	void ShowDock( bool show )
	{
		_split.Splitter.SetClass( "hidden", !show );
		_split.Right.SetClass( "hidden", !show );

		if ( show )
		{
			if ( _dockSplit is { } split )
			{
				_split.UpdateSplitFraction( split.Value / 100f );
			}
			else
			{
				ResetDock();
			}
			return;
		}

		if ( _split.Left.Style.Height is { } height ) _dockSplit = height;
		_split.Left.Style.Height = null;
		_split.Right.Style.Height = null;
	}

	void OpenShowMenu( Panel source )
	{
		var menu = DevToolsMenus.Menu( source, Popup.PositionMode.BelowLeft );
		menu.AddChild( new Label( "Events", "menu-section" ) );
		Option( "Sync vars", null, () => _show.Sync, () => _show = _show with { Sync = !_show.Sync } );
		Option( "RPCs", null, () => _show.Rpc, () => _show = _show with { Rpc = !_show.Rpc } );
		Option( "Dormancy", "When objects we send fall asleep and wake. Other machines' objects aren't known here.", () => _show.Dormancy, () => _show = _show with { Dormancy = !_show.Dormancy } );
		Option( "Snapshots", "Scene snapshots and object create, destroy and refresh messages, in the Session lane", () => _show.Snapshots, () => _show = _show with { Snapshots = !_show.Snapshots } );
		Option( "Resends", "Sync vars sent or received again with the same value", () => _show.Resends, () => _show = _show with { Resends = !_show.Resends } );
		Option( "Transforms", "Position, rotation and scale updates in delta snapshots, sent and received. Only recorded while this is on: everything that moves adds one a tick.", () => CaptureTransforms, () => CaptureTransforms = !CaptureTransforms );

		menu.AddChild( new Label( "Objects", "menu-section" ) );
		Option( "Active only", "Only objects that have sent or received something shown here, hiding ones that have only slept", () => _show.ActiveOnly, () => _show = _show with { ActiveOnly = !_show.ActiveOnly } );
		Option( "Group by component", "Each component's sync vars and RPCs under a header of its own, which folds away", () => GroupByComponent, () => GroupByComponent = !GroupByComponent );

		void Option( string text, string tooltip, Func<bool> isOn, Action flip )
		{
			var row = DevToolsMenus.Toggle( menu, text, isOn, () =>
			{
				flip();
				Rebuild();
			} );
			row.Tooltip = tooltip;
		}
	}

	void UpdateShowSummary( in DevTools.NetworkLog.Show show )
	{
		if ( _summarized == show ) return;
		_summarized = show;
		_shownKinds.Clear();
		if ( show.Sync ) _shownKinds.Add( "Sync" );
		if ( show.Rpc ) _shownKinds.Add( "RPC" );
		if ( show.Dormancy ) _shownKinds.Add( "Dormancy" );
		if ( show.Snapshots ) _shownKinds.Add( "Snapshots" );
		if ( show.Resends ) _shownKinds.Add( "Resends" );
		if ( show.Transforms ) _shownKinds.Add( "Transforms" );
		var text = _shownKinds.Count switch { 0 => "Session events", 6 => "All events", _ => string.Join( ", ", _shownKinds ) };
		_showText.Text = show.ActiveOnly ? $"{text} - active" : text;
	}

	public void Refresh()
	{
		var rebuild = false;
		if ( _clears != Clears || _since != SessionEnded )
		{
			_clears = Clears;
			_since = SessionEnded;
			Selected = null;
			_events.Clear();
			ClearPin();
			_fullCopy = true;
			rebuild = true;
		}

		if ( !DevTools.Paused && (_fullCopy || _copied != Added) )
		{
			var from = _events.Count;
			if ( _fullCopy || from > MaxCopied || !CopyAdded( _events, ref _copied ) )
			{
				_copied = CopyTimeline( _events, SessionEnded );
				_fullCopy = false;
				rebuild = true;
			}
			else if ( !rebuild )
			{
				rebuild = !AppendShown( from );
			}
		}

		_refreshing = true;
		if ( rebuild )
		{
			Rebuild();
		}
		else
		{
			UpdateFacts();
			RefreshPinned();
		}
		_refreshing = false;
	}

	DevTools.NetworkLog.Show CurrentShow => _show with { Transforms = CaptureTransforms };

	bool AppendShown( int from )
	{
		if ( _filterText != _filter.Text || _selectedShow != CurrentShow ) return false;

		for ( int i = from; i < _events.Count; i++ )
		{
			if ( _selection.Activate( _events[i] ) ) return false;
		}

		var scene = Game.ActiveScene;
		var added = false;
		for ( int i = from; i < _events.Count; i++ )
		{
			var e = _events[i];
			if ( !_selection.Keeps( e ) ) continue;
			_shown.Add( e );
			added |= Place( e, scene );
		}

		Order( added );
		return true;
	}

	void Rebuild()
	{
		var show = CurrentShow;
		UpdateShowSummary( show );

		if ( _filterText != _filter.Text )
		{
			_filterText = _filter.Text;
			_parsedFilter = Filter.Parse( _filterText );
		}

		_selectedShow = show;
		_selection.Select( _events, show, _parsedFilter, _shown );

		var scene = Game.ActiveScene;
		foreach ( var group in Groups )
		{
			group.Tracks.Clear();
		}
		foreach ( var track in _tracks.Values )
		{
			track.Events.Clear();
		}
		Groups.Clear();
		SessionEvents.Clear();

		foreach ( var e in _shown )
		{
			Place( e, scene );
		}

		foreach ( var (key, track) in _tracks )
		{
			if ( track.Events.Count == 0 ) _tracks.Remove( key );
		}
		foreach ( var (id, group) in _groups )
		{
			if ( group.Tracks.Count == 0 ) _groups.Remove( id );
		}

		Order( true );
		UpdateFacts();
		RefreshPinned();
	}

	bool Place( in Entry e, Scene scene )
	{
		var added = false;
		if ( e.Kind == Kind.Session ) SessionEvents.Add( e );

		if ( !_groups.TryGetValue( e.ObjectId, out var group ) )
		{
			group = new Group { Id = e.ObjectId };
			_groups.Add( e.ObjectId, group );
		}

		if ( group.Tracks.Count == 0 )
		{
			group.Owner = OwnerName( scene?.Directory.FindByGuid( e.ObjectId ) );
			group.Dormancy = default;
			Groups.Add( group );
			added = true;
		}

		var trackKey = (e.ObjectId, e.Name, e.Kind);
		if ( !_tracks.TryGetValue( trackKey, out var track ) )
		{
			track = CreateTrack( e );
			_tracks.Add( trackKey, track );
		}
		if ( track.Events.Count == 0 )
		{
			group.Tracks.Add( track );
			added = true;
		}

		track.Events.Add( e );
		if ( e.Kind == Kind.Dormancy ) group.Dormancy = e.Dormancy;
		group.Name = e.ObjectName;
		return added;
	}

	void Order( bool sort )
	{
		if ( sort )
		{
			Groups.Sort( CompareGroups );
			Comparison<Track> compareTracks = GroupByComponent ? CompareGroupedTracks : CompareTracks;
			foreach ( var group in Groups )
			{
				group.Tracks.Sort( compareTracks );
			}
		}

		foreach ( var group in Groups )
		{
			group.UpdateTitles();
		}
	}

	void UpdateFacts()
	{
		_facts.Set( "Session", Networking.IsActive ? Networking.IsHost ? "host" : "client" : "not connected" );
		_facts.Set( "Objects", $"{Groups.Count}" );
		_facts.Set( "Tracks", $"{_tracks.Count}" );
		_facts.Set( "Events", $"{_shown.Count:N0} of {_events.Count:N0}" );
		_facts.Set( "History", History(), Dropped > 0 ? DevTools.Severity.Warning : null,
			Dropped > 0 ? $"How far back the timeline goes. Only the newest {Capacity:N0} events are kept, so older ones are gone." : "How far back the timeline goes. Nothing has been let go yet." );
	}

	static string History()
	{
		if ( Oldest is not { } oldest ) return "-";
		var span = DateTime.UtcNow - (oldest > SessionEnded ? oldest : SessionEnded);
		return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds:00}s" : $"{span.TotalSeconds:0}s";
	}

	void CopyShown()
	{
		var text = new System.Text.StringBuilder( "Time (UTC)\tEvent\tDirection\tObject\tTrack\tPeer\tBytes\tValue\n" );
		for ( int i = _shown.Count - 1; i >= 0; i-- )
		{
			var e = _shown[i];
			var direction = e.Kind is Kind.Dormancy or Kind.Session ? "" : e.Outbound ? "sent" : "received";
			text.Append( $"{e.Time:yyyy-MM-dd HH:mm:ss.fff}\t{e.Kind}\t{direction}\t{e.ObjectName}\t{e.Name}\t{e.Peer}\t{e.Size}\t{e.Value}\n" );
		}

		NativeEngine.Sdl.SetClipboardText( text.ToString() );
	}

	internal void Pin( Guid id, string objectName, string track, Entry? select = null )
	{
		if ( _pin is not { } pin || pin.Id != id || pin.Track != track )
		{
			var shown = _pin is not null;
			_pin = (id, objectName, track);
			_pinnedSection.TitleLabel.Text = track is null ? $"{objectName} - everything" : $"{objectName} - {TrackLabel( track )}";
			if ( !shown ) ShowDock( true );
		}

		Selected = select;
		_focusPending = true;
		RefreshPinned();
	}

	void ClearPin()
	{
		if ( _pin is null ) return;
		_pin = null;
		Selected = null;
		ShowDock( false );
	}

	static Track CreateTrack( Entry e )
	{
		var label = TrackLabel( e.Name );
		var dot = label.LastIndexOf( '.' );
		var component = (e.Kind is Kind.SyncVar or Kind.Rpc) && dot > 0 ? label[..dot] : null;
		var icon = e.Kind switch { Kind.SyncVar => "⇅", Kind.Rpc => "⚡", Kind.Snapshot => "■", Kind.Object => "+", Kind.Session => "|", Kind.Transform => "→", _ => "z" };
		return new Track
		{
			Name = e.Name,
			Kind = e.Kind,
			Component = component,
			Color = DevToolsStyles.Series[(e.Name.GetHashCode() & int.MaxValue) % DevToolsStyles.Series.Length],
			Label = $"{icon} {label}",
			GroupedLabel = $"{icon} {(component is null ? label : label[(dot + 1)..])}",
			ComponentExpanded = component is null ? null : $"▾ {component}",
			ComponentCollapsed = component is null ? null : $"▸ {component}",
		};
	}

	static int CompareGroups( Group a, Group b )
	{
		if ( (a.Id == SessionLane) != (b.Id == SessionLane) ) return a.Id == SessionLane ? -1 : 1;
		var byName = string.Compare( a.Name, b.Name, StringComparison.OrdinalIgnoreCase );
		return byName != 0 ? byName : a.Id.CompareTo( b.Id );
	}

	static int CompareTracks( Track a, Track b )
	{
		static int Order( Kind kind ) => kind == Kind.Dormancy ? -1 : (int)kind;
		return a.Kind != b.Kind ? Order( a.Kind ).CompareTo( Order( b.Kind ) ) : string.Compare( a.Name, b.Name, StringComparison.Ordinal );
	}

	static int CompareGroupedTracks( Track a, Track b )
	{
		if ( (a.Component is null) != (b.Component is null) ) return a.Component is null ? -1 : 1;
		var byComponent = string.Compare( a.Component, b.Component, StringComparison.Ordinal );
		return byComponent != 0 ? byComponent : CompareTracks( a, b );
	}

	public override void Tick()
	{
		base.Tick();

		var height = _split.Box.Rect.Height / ScaleToScreen;
		if ( height > 0 )
		{
			_split.MinimumFractionLeft = MathF.Min( 0.5f, MinTimelineHeight / height );
			_split.MinimumFractionRight = MathF.Min( 0.5f, MinDockHeight / height );
		}

		_live.SetClass( "hidden", _view.Following );

		if ( !_focusPending ) return;
		var body = _pinnedSection.Body;
		if ( _selectedRowKey is null )
		{
			body.ScrollOffset = Vector2.Zero;
			_focusPending = false;
			return;
		}

		var row = _pinTable.GetRow( _selectedRowKey );
		if ( row is null || row.Box.Rect.Height <= 0 ) return;
		body.ScrollIntoView( row.Box.Rect );
		_focusPending = false;
	}

	void RefreshPinned()
	{
		if ( _pin is null || (_refreshing && _pinnedSection.HasHovered) ) return;

		var (id, _, track) = _pin.Value;
		var matching = CollectPinned( id, track );

		var at = Selected is { } sel ? matching.IndexOf( sel ) : -1;
		var first = at < 0 ? 0 : Math.Clamp( at - MaxPinnedRows / 2, 0, Math.Max( 0, matching.Count - MaxPinnedRows ) );
		_selectedRowKey = at < 0 ? null : SlotKeys[at - first];

		var now = _view.Now;
		var count = Math.Min( matching.Count - first, MaxPinnedRows );
		_pinTable.Begin();
		for ( int slot = 0; slot < count; slot++ )
		{
			var e = matching[first + slot];
			_slotEntries[slot] = e;
			var dir = e.Kind switch { Kind.Dormancy => "state", Kind.Session => "event", _ => e.Outbound ? "▲ sent" : "▽ recv" };
			_pinTable.Row( SlotKeys[slot],
				[
					new( $"{e.Seconds - now:0.00}s" ),
					new( dir, e.Resend ? "dim" : null ),
					new( TrackLabel( e.Name ), track is null ? null : "dim" ),
					new( e.Peer, "dim" ),
					DevToolsTable.Number( DevToolsFormat.Bytes( e.Size ) ),
					new( e.Value, Tooltip: e.Value ),
				],
				_slotClicks[slot],
				rowClass: Selected == e ? "selected" : null );
		}

		_pinTable.End( "Nothing recorded on this track yet" );
	}

	List<Entry> CollectPinned( Guid id, string track )
	{
		var matching = _matchingPinnedEvents;
		matching.Clear();
		for ( int i = _events.Count - 1; i >= 0; i-- )
		{
			var e = _events[i];
			if ( e.ObjectId != id || (track is not null && e.Name != track) ) continue;
			if ( e.Resend && !_show.Resends && Selected != e ) continue;
			matching.Add( e );
		}
		return matching;
	}

	void SelectPinned( Entry e )
	{
		Selected = e;
		_focusPending = true;
		_view.CenterOn( e.Seconds );
		RefreshPinned();
	}
}
