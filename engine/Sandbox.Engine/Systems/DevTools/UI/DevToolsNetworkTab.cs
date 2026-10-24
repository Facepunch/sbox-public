using Sandbox.UI;
using static Sandbox.Diagnostics.DevTools.NetworkLog;

namespace Sandbox.Diagnostics;

/// <summary>
/// The network analyzer: every message sent and received and the session's events, the peers, the networked objects,
/// and bandwidth by message type with the busiest calls.
/// </summary>
internal sealed class DevToolsNetworkTab : DevToolsTab
{
	public override string TabId => "network";
	public override string Title => "Network";
	public override bool FillsPage => true;

	enum View { Traffic, Peers, Objects, Bandwidth }

	static readonly (string Label, Kind? Kind)[] KindFilters = [("All events", null), ("Sync vars", Kind.SyncVar), ("RPCs", Kind.Rpc), ("Objects", Kind.Object), ("Snapshots", Kind.Snapshot), ("Session", Kind.Session)];
	static readonly (string Label, bool? Outbound)[] DirectionFilters = [("All directions", null), ("Sent", true), ("Received", false)];

	readonly Dictionary<View, Label> _viewButtons = new();
	readonly Dictionary<View, Panel> _views = new();
	readonly Label _session;
	readonly Label _record, _pause;
	readonly TextEntry _search;
	readonly Label _kindFilter, _directionFilter;
	readonly VirtualList _list;
	readonly Label _footerCount, _footerRate;
	readonly DevToolsTable _peers, _objects;
	readonly DevToolsFacts _facts;
	readonly DevToolsSection _bandwidth, _calls;

	View _view = View.Traffic;
	int _kindIndex, _directionIndex;
	bool _paused;
	RealTimeUntil _rateUpdate;
	int _shownVersion = -1;
	string _shownSearch;

	public DevToolsNetworkTab()
	{
		AddClass( "network" );

		var toolbar = AddChild( new Panel( null, "net-toolbar" ) );
		var segments = toolbar.AddChild( new Panel( null, "segments" ) );
		foreach ( var view in Enum.GetValues<View>() )
		{
			var button = segments.AddChild( new Label( $"{view}", "segment" ) );
			button.AddEventListener( "onclick", () => Show( view ) );
			_viewButtons[view] = button;
		}

		_session = toolbar.AddChild( new Label( "", "net-session" ) );
		toolbar.AddChild( new Panel( null, "grow" ) );
		_record = Button( toolbar, "fiber_manual_record", "Capture", () => Capturing = !Capturing );
		_record.AddClass( "record" );
		_pause = Button( toolbar, "pause", "Hold the list where it is. Capture carries on.", () => _paused = !_paused );
		Button( toolbar, "delete", "Clear", () => { DevTools.NetworkLog.Clear(); _paused = false; } );
		Button( toolbar, "content_copy", "Copy the events shown, tab separated", CopyShown );

		// Traffic: what went where, newest first
		var traffic = AddView( View.Traffic );
		var filters = traffic.AddChild( new Panel( null, "net-filters" ) );
		_search = filters.AddChild( new TextEntry() );
		_search.AddClass( "net-search" );
		_search.Placeholder = "Search";
		_kindFilter = Select( filters, () => _kindIndex = (_kindIndex + 1) % KindFilters.Length );
		_directionFilter = Select( filters, () => _directionIndex = (_directionIndex + 1) % DirectionFilters.Length );

		var head = traffic.AddChild( new Panel( null, "net-row net-head" ) );
		foreach ( var title in new[] { "Time (UTC)", "Event", "Name", "Peer", "Size" } )
			head.AddChild( new Label( title, title == "Size" ? "net-cell number" : "net-cell" ) );

		_list = traffic.AddChild( new VirtualList() );
		_list.AddClass( "net-list" );
		_list.ItemHeight = 22;
		_list.OnCreateCell = ( cell, data ) => Row( cell, (Entry)data );

		var footer = traffic.AddChild( new Panel( null, "net-footer" ) );
		_footerCount = footer.AddChild( new Label( "", "net-footer-count" ) );
		footer.AddChild( new Panel( null, "grow" ) );
		_footerRate = footer.AddChild( new Label( "", "net-footer-rate" ) );

		_peers = AddView( View.Peers, scrolls: true ).AddChild( new DevToolsTable( "minmax(0, 1fr) 64px 52px 64px 64px 52px 64px 104px", "Peers", "Role", "#ping", "#in/s", "#out/s", "#quality", "#connected", "#messages" ).Banded() );
		_objects = AddView( View.Objects, scrolls: true ).AddChild( new DevToolsTable( "minmax(0, 1fr) minmax(0, 0.6fr) 56px 64px", "Networked objects", "Owner", "Mode", "Culled" ).Banded() );

		var bandwidth = AddView( View.Bandwidth, scrolls: true );
		_facts = bandwidth.AddChild( new DevToolsFacts() );
		_bandwidth = bandwidth.AddChild( new DevToolsSection( "Bandwidth" ) );
		_bandwidth.Body.AddChild( new DevToolsOverlayView( DebugOverlay.NetworkGraph.Draw ) );
		_calls = bandwidth.AddChild( new DevToolsSection( "Busiest calls" ) );
		_calls.Body.AddChild( new DevToolsOverlayView( DebugOverlay.NetworkCalls.Draw ) );

		Show( View.Traffic );
	}

	Panel AddView( View view, bool scrolls = false )
	{
		var panel = AddChild( new Panel( null, scrolls ? "net-view scrolls" : "net-view" ) );
		_views[view] = panel;
		return panel;
	}

	static Label Button( Panel parent, string icon, string tooltip, Action onClick )
	{
		var button = parent.AddChild( new Label( icon, "icon-button" ) );
		button.Tooltip = tooltip;
		button.AddEventListener( "onclick", onClick );
		return button;
	}

	/// <summary>
	/// A choice that steps to the next option each click - the analyzer's filters, which have a handful each.
	/// </summary>
	static Label Select( Panel parent, Action next )
	{
		var select = parent.AddChild( new Panel( null, "net-select" ) );
		var text = select.AddChild( new Label( "", "net-select-text" ) );
		select.AddChild( new Label( "expand_more", "net-select-icon" ) );
		select.AddEventListener( "onclick", next );
		return text;
	}

	void Show( View view )
	{
		_view = view;
		foreach ( var (v, button) in _viewButtons ) button.SetClass( "active", v == view );
		foreach ( var (v, panel) in _views ) panel.SetClass( "hidden", v != view );
		_shownVersion = -1;
	}

	static void Row( Panel cell, Entry e )
	{
		cell.AddClass( "net-row" );
		cell.AddChild( new Label( $"{e.Time:HH:mm:ss.fff}", "net-cell mono" ) );

		var kind = cell.AddChild( new Panel( null, "net-cell net-event" ) );
		kind.AddChild( new Label( e.Kind == Kind.Session ? "power" : e.Outbound ? "arrow_upward" : "arrow_downward", $"net-icon {(e.Kind == Kind.Session ? "session" : e.Outbound ? "sent" : "received")}" ) );
		kind.AddChild( new Label( KindName( e.Kind ), "net-kind" ) );

		cell.AddChild( new Label( e.Name ?? "", "net-cell" ) );
		cell.AddChild( new Label( e.Peer ?? "", "net-cell dim" ) );
		cell.AddChild( new Label( DevToolsFormat.Bytes( e.Size ), "net-cell number mono" ) );
	}

	static string KindName( Kind kind ) => kind switch
	{
		Kind.SyncVar => "SyncVar",
		Kind.Rpc => "RPC",
		Kind.Object => "Object",
		Kind.Snapshot => "Snapshot",
		_ => "Session",
	};

	bool Matches( Entry e )
	{
		if ( KindFilters[_kindIndex].Kind is { } kind && e.Kind != kind ) return false;
		if ( DirectionFilters[_directionIndex].Outbound is { } outbound && (e.Kind == Kind.Session || e.Outbound != outbound) ) return false;

		var search = _search.Text;
		if ( string.IsNullOrWhiteSpace( search ) ) return true;
		return (e.Name?.Contains( search, StringComparison.OrdinalIgnoreCase ) ?? false)
			|| (e.Peer?.Contains( search, StringComparison.OrdinalIgnoreCase ) ?? false)
			|| KindName( e.Kind ).Contains( search, StringComparison.OrdinalIgnoreCase );
	}

	Entry[] Shown() => DevTools.NetworkLog.Snapshot().Where( Matches ).Reverse().ToArray();

	void CopyShown()
	{
		var text = new System.Text.StringBuilder( "Time (UTC)\tEvent\tDirection\tName\tPeer\tBytes\n" );
		foreach ( var e in Shown() )
			text.Append( $"{e.Time:yyyy-MM-dd HH:mm:ss.fff}\t{KindName( e.Kind )}\t{(e.Kind == Kind.Session ? "" : e.Outbound ? "sent" : "received")}\t{e.Name}\t{e.Peer}\t{e.Size}\n" );
		NativeEngine.Sdl.SetClipboardText( text.ToString() );
	}

	public override void Tick()
	{
		base.Tick();

		_record.SetClass( "on", Capturing );
		_record.Tooltip = Capturing ? "Capturing - click to stop" : "Not capturing - click to start";
		_pause.Text = _paused ? "play_arrow" : "pause";
		_pause.SetClass( "active", _paused );
		_kindFilter.Text = KindFilters[_kindIndex].Label;
		_directionFilter.Text = DirectionFilters[_directionIndex].Label;

		_session.Text = !Networking.IsActive ? "Offline" : Networking.IsHost ? "Hosting" : "Connected";
		_session.SetClass( "live", Networking.IsActive );

		if ( _view != View.Traffic || HasClass( "hidden" ) ) return;

		// The list follows the log as it grows, unless it's held
		var key = $"{_kindIndex}:{_directionIndex}:{_search.Text}";
		if ( !_paused && (DevTools.NetworkLog.Version != _shownVersion || key != _shownSearch) )
		{
			_shownVersion = DevTools.NetworkLog.Version;
			_shownSearch = key;
			_list.SetItems( Shown().Select( x => (object)x ) );
		}

		var count = DevTools.NetworkLog.Count;
		_footerCount.Text = _paused ? $"Held - {count:N0} events" : Capturing ? $"Capturing {count:N0} events" : $"Stopped - {count:N0} events";

		// What the log saw go by in the last second - the connections' own rates aren't kept everywhere
		if ( _rateUpdate )
		{
			_rateUpdate = 0.5f;
			var (sent, received) = LastSecond();
			_footerRate.Text = $"↑ {DevToolsFormat.Bytes( sent )}/s   ↓ {DevToolsFormat.Bytes( received )}/s";
		}
	}

	public override void Refresh( DevTools.Summary s )
	{
		switch ( _view )
		{
			case View.Peers: RefreshPeers(); break;
			case View.Objects: RefreshObjects(); break;
			case View.Bandwidth: RefreshBandwidth(); break;
		}
	}

	void RefreshPeers()
	{
		var t = _peers;
		t.Begin();

		if ( Networking.IsActive )
		{
			foreach ( var c in Connection.All.OrderByDescending( x => x.IsHost ) )
			{
				var local = c == Connection.Local;
				var stats = c.Stats;
				var role = c.IsHost ? "host" : "client";
				t.Row( c.Id.ToString(), [
					PeerName( c ),
					new( local ? $"{role}, local" : role, "dim" ),
					local ? new( "-", "number dim" ) : DevToolsTable.Number( $"{stats.Ping}", stats.Ping > 300 ? DevTools.Severity.Bad : stats.Ping > 150 ? DevTools.Severity.Warning : null ),
					local ? new( "-", "number dim" ) : DevToolsTable.Number( DevToolsFormat.Bytes( stats.InBytesPerSecond ) ),
					local ? new( "-", "number dim" ) : DevToolsTable.Number( DevToolsFormat.Bytes( stats.OutBytesPerSecond ) ),
					local ? new( "-", "number dim" ) : DevToolsTable.Number( $"{stats.ConnectionQuality * 100:0}%", stats.ConnectionQuality is > 0 and < 0.8f ? DevTools.Severity.Warning : null ),
					new( Since( c.ConnectionTime ), "number dim" ),
					new( $"{c.MessagesSent:N0} / {c.MessagesRecieved:N0}", "number dim" )],
					tooltip: $"{c.Name} [{c.SteamId}]\nSent / received messages, ping in ms" );
			}
		}

		t.End( Networking.IsActive ? "No peers" : "Not in a session" );
	}

	static string Since( DateTimeOffset when )
	{
		if ( when == default ) return "-";
		var t = DateTimeOffset.UtcNow - when;
		return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";
	}

	void RefreshObjects()
	{
		var t = _objects;
		t.Begin();

		if ( Game.ActiveScene is { } scene )
		{
			foreach ( var o in scene.networkedObjects.OrderBy( x => x.GameObject?.Name ) )
			{
				var go = o.GameObject;
				if ( go is null ) continue;

				var owner = o.Owner == Guid.Empty ? "host" : Connection.Find( o.Owner ) is { } c ? PeerName( c ) : $"{o.Owner}";
				t.Row( go.Id.ToString(), [
					go.Name,
					new( owner, "dim" ),
					new( o.IsOwner ? "owner" : o.IsProxy ? "proxy" : "-", "dim" ),
					new( go.IsNetworkCulled ? "yes" : "", "number dim" )],
					tooltip: $"{go.Name} ({go.Id})" );
			}
		}

		t.End( "No networked objects" );
	}

	void RefreshBandwidth()
	{
		_bandwidth.SetClass( "hidden", !Networking.IsActive );
		_calls.SetClass( "hidden", !Networking.IsActive );

		if ( !Networking.IsActive )
		{
			_facts.Set( "Session", "not connected" );
			foreach ( var key in NetworkKeys ) _facts.Hide( key );
			return;
		}

		_facts.Set( "Session", Networking.IsHost ? "host" : "client" );
		_facts.Set( "Connections", $"{Connection.All.Count}" );

		float bytesIn = 0, bytesOut = 0;
		foreach ( var c in Connection.All )
		{
			if ( c == Connection.Local ) continue;
			bytesIn += c.Stats.InBytesPerSecond;
			bytesOut += c.Stats.OutBytesPerSecond;
		}

		_facts.Set( "In", $"{DevToolsFormat.Bytes( bytesIn )}/s" );
		_facts.Set( "Out", $"{DevToolsFormat.Bytes( bytesOut )}/s" );

		if ( !Networking.IsHost && Connection.Host is { } host )
		{
			var ping = host.Stats.Ping;
			var quality = host.Stats.ConnectionQuality;
			_facts.Set( "Ping", $"{ping} ms", ping > 300 ? DevTools.Severity.Bad : ping > 150 ? DevTools.Severity.Warning : null );
			_facts.Set( "Quality", $"{quality * 100:0}%", quality is > 0 and < 0.8f ? DevTools.Severity.Warning : null );

			// What the host says about itself
			var hostStats = Networking.HostStats;
			_facts.Set( "Host fps", $"{hostStats.Fps}" );
			_facts.Set( "Host in / out", $"{DevToolsFormat.Bytes( hostStats.InBytesPerSecond )}/s / {DevToolsFormat.Bytes( hostStats.OutBytesPerSecond )}/s" );
		}
	}

	static readonly string[] NetworkKeys = ["Connections", "In", "Out", "Ping", "Quality", "Host fps", "Host in / out"];
}
