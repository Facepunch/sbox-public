using Sandbox.UI;
using static Sandbox.Diagnostics.DevTools.NetworkLog;

namespace Sandbox.Diagnostics;

/// <summary>
/// Network activity, peers, objects and bandwidth.
/// </summary>
internal sealed class DevToolsNetworkTab : DevToolsTab
{
	public override string TabId => "network";
	public override string Title => "Network";
	public override bool FillsPage => true;

	enum View { Timeline, Peers, Objects, Bandwidth }

	readonly Dictionary<View, Label> _viewButtons = new();
	readonly Dictionary<View, Panel> _views = new();
	readonly Label _session;
	readonly Label _record;
	readonly DevToolsTable _peers, _objects;
	readonly DevToolsFacts _facts;
	readonly DevToolsSection _bandwidth, _calls;
	readonly DevToolsNetTimeline _timeline;

	View _view = View.Timeline;

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

		_timeline = AddView( View.Timeline ).AddChild( new DevToolsNetTimeline() );
		_peers = AddView( View.Peers, scrolls: true ).AddChild( new DevToolsTable( "minmax(0, 1fr) 64px 52px 64px 64px 52px 64px 104px", "Peers", "Role", "#ping", "#in/s", "#out/s", "#quality", "#connected", "#messages" ).Banded() );
		_objects = AddView( View.Objects, scrolls: true ).AddChild( new DevToolsTable( "minmax(0, 1fr) minmax(0, 0.6fr) 56px 64px 64px", "Networked objects", "Owner", "Mode", "Culled", "Dormant" ).Banded() );

		var bandwidth = AddView( View.Bandwidth, scrolls: true );
		_facts = bandwidth.AddChild( new DevToolsFacts() );
		_bandwidth = bandwidth.AddChild( new DevToolsSection( "Bandwidth" ) );
		_bandwidth.Body.AddChild( new DevToolsOverlayView( DebugOverlay.NetworkGraph.Draw ) );
		_calls = bandwidth.AddChild( new DevToolsSection( "Busiest calls" ) );
		_calls.Body.AddChild( new DevToolsOverlayView( DebugOverlay.NetworkCalls.Draw ) );

		Show( View.Timeline );
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

	void Show( View view )
	{
		_view = view;
		foreach ( var (v, button) in _viewButtons ) button.SetClass( "active", v == view );
		foreach ( var (v, panel) in _views ) panel.SetClass( "hidden", v != view );
	}

	public override void Tick()
	{
		base.Tick();

		_record.SetClass( "on", Capturing );
		_record.Tooltip = Capturing ? "Capturing - click to stop" : "Not capturing - click to start";

		_session.Text = !Networking.IsActive ? "Offline" : Networking.IsHost ? "Hosting" : "Connected";
		_session.SetClass( "live", Networking.IsActive );
	}

	public override void Refresh( DevTools.Summary s )
	{
		switch ( _view )
		{
			case View.Timeline: _timeline.Refresh(); break;
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
					tooltip: $"{ConnectionName( c )} [{c.SteamId}]\nSent / received messages, ping in ms" );
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
					new( go.IsNetworkCulled ? "yes" : "", "number dim" ),
					Dormant( NetworkDebugSystem.GetDormancyState( o ) )],
					tooltip: $"{go.Name} ({go.Id})" );
			}
		}

		t.End( "No networked objects" );
	}

	static DevToolsTable.Cell Dormant( NetworkDebugSystem.DormancyState state ) => state switch
	{
		NetworkDebugSystem.DormancyState.RemoteUnknown => new( "-", "number dim" ),
		NetworkDebugSystem.DormancyState.AckClean or NetworkDebugSystem.DormancyState.VisibilitySleep => new( "true", "number" ),
		_ => new( "false", "number dim" ),
	};

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
