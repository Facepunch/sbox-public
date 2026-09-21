using Sandbox.UI;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Editor;

public sealed partial class PanelDockWindows
{
	/// <summary>
	/// Saves the main layout and every floating window, including closed registrations, tab selection,
	/// splits, desktop position and UI size. Does not move panels. Call between drag/layout operations.
	/// </summary>
	public string State
	{
		get => CaptureState();
		set => RestoreState( value );
	}

	string CaptureState()
	{
		if ( _disposed || _disposeRequested || _busy != 0 || _session is not null )
			throw new InvalidOperationException( "Cannot save a docking workspace during a drag, layout operation or disposal." );
		var main = RegionState.Capture( _main );
		var windows = _windows.Where( x => x.Value.Items.Any( item => x.Value.IsOpen( item.Id ) ) ).ToArray();
		// An empty window may be queued for frame-end disposal. Its closed registrations return home.
		main.Items = main.Items.Concat( _windows.Where( x => !windows.Any( w => w.Key == x.Key ) )
			.SelectMany( x => x.Value.Items ).Select( x => x.Id ) ).ToArray();
		return JsonSerializer.Serialize( new WorkspaceState
		{
			Version = 1,
			Main = main,
			Windows = windows.Select( x => new WindowState
			{
				Region = RegionState.Capture( x.Value ),
				X = x.Key.Position.x,
				Y = x.Key.Position.y,
				Width = x.Key.Size.x,
				Height = x.Key.Size.y
			} ).ToArray()
		}, WorkspaceState.JsonOptions );
	}

	/// <summary>
	/// Restores a workspace using the existing content instances. Invalid data leaves the workspace untouched.
	/// Returns false during a drag/layout operation or if windows cannot be created. Desktop windows require
	/// the main host to belong to an editor PanelWindow. This saves layout, not the contents of each panel.
	/// </summary>
	public bool RestoreState( string json )
	{
		if ( _disposed || _disposeRequested || _busy != 0 || _session is not null || !IsAlive( _main ) ) return false;
		var hosts = _reservations.Keys.ToArray();
		var items = hosts.SelectMany( x => x.Items ).ToArray();
		var required = hosts.SelectMany( x => x.Items.Where( item => !item.CanClose && x.IsOpen( item.Id ) ) ).Select( x => x.Id );
		if ( !WorkspaceState.TryRead( json, items.Select( x => x.Id ), required, out var state ) ) return false;
		if ( state.Windows.Length > 0 && PanelWindow.FromPanel( _main ) is not { IsOpen: true } ) return false;
		if ( items.Any( x => !x.IsAlive ) ) return false;
		var before = hosts.Select( x => x.State ).ToArray();

		var created = new List<FloatingWindow>();
		var notifications = new List<IDisposable>();
		_busy++;
		try
		{
			// Create only panels that will be open, before transferring any registrations.
			foreach ( var region in state.Windows.Select( x => x.Region ).Prepend( state.Main ) )
			{
				var layout = new DockLayout();
				layout.Restore( region.Layout, region.Items );
				foreach ( var item in items.Where( x => layout.FindGroup( x.Id ) is not null ) ) item.EnsureContent();
			}

			// Allocate windows before moving any content. A native creation failure leaves the old layout intact.
			foreach ( var saved in state.Windows )
				created.Add( CreateFloatingWindow( "Dock panels", new Vector2( saved.Width, saved.Height ), new Vector2( saved.X, saved.Y ) ) );

			// ConfigureWindow is user code. Recheck ownership before committing any transfer.
			if ( _disposeRequested || hosts.Any( x => !IsAlive( x ) )
				|| hosts.Where( ( x, i ) => x.State != before[i] ).Any()
				|| hosts.Sum( x => x.Items.Count ) != items.Length
				|| items.Any( x => !x.IsAlive || !hosts.Any( h => h.Find( x.Id ) == x ) )
				|| created.Any( x => !x.IsOpen || !_windows.ContainsKey( x ) || _windows[x].Items.Count != 0 ) ) return false;

			foreach ( var host in _reservations.Keys.ToArray() ) notifications.Add( host.DeferLayoutNotifications() );
			var destinations = new Dictionary<string, DockHost>( StringComparer.Ordinal );
			for ( int i = 0; i < created.Count; i++ )
			{
				var target = _windows[created[i]];
				foreach ( var id in state.Windows[i].Region.Items ) destinations.Add( id, target );
			}
			foreach ( var source in hosts )
			{
				foreach ( var item in source.Items.ToArray() )
				{
					var target = destinations.GetValueOrDefault( item.Id, _main );
					if ( source != target ) Transfer( source, target, item, open: false );
				}
			}
			_main.RestoreWorkspaceLayout( state.Main.Layout );
			for ( int i = 0; i < created.Count; i++ )
			{
				var host = _windows[created[i]];
				host.RestoreWorkspaceLayout( state.Windows[i].Region.Layout );
				created[i].Title = host.Items.FirstOrDefault( x => host.IsOpen( x.Id ) )?.Title ?? "Dock panels";
			}
			foreach ( var window in _windows.Keys.Except( created ).ToArray() ) window.Dispose();
			UpdateReservations();
			created.Clear(); // Committed windows now belong to the workspace.
			return true;
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, "Could not restore docking workspace" );
			return false;
		}
		finally
		{
			// Returning content before closing also protects it if a transfer failed after creation.
			foreach ( var window in created ) Try( window.Dispose );
			_busy--;
			foreach ( var notification in notifications ) Try( notification.Dispose );
			QueueRequests();
		}
	}

	sealed class LayoutJsonConverter : JsonConverter<string>
	{
		public override string Read( ref Utf8JsonReader reader, Type type, JsonSerializerOptions options )
		{
			if ( reader.TokenType == JsonTokenType.String ) return reader.GetString();
			using var document = JsonDocument.ParseValue( ref reader );
			return document.RootElement.GetRawText();
		}

		public override void Write( Utf8JsonWriter writer, string value, JsonSerializerOptions options )
		{
			using var document = JsonDocument.Parse( value );
			document.RootElement.WriteTo( writer );
		}
	}

	internal sealed class RegionState
	{
		[JsonRequired]
		public string[] Items { get; set; }
		[JsonRequired]
		[JsonConverter( typeof( LayoutJsonConverter ) )]
		public string Layout { get; set; }

		internal static RegionState Capture( DockHost host ) => new()
		{
			Items = host.Items.Select( x => x.Id ).ToArray(),
			Layout = host.State
		};
	}

	internal sealed class WindowState
	{
		[JsonRequired]
		public RegionState Region { get; set; }
		[JsonRequired]
		public float X { get; set; }
		[JsonRequired]
		public float Y { get; set; }
		[JsonRequired]
		public float Width { get; set; }
		[JsonRequired]
		public float Height { get; set; }
	}

	internal sealed class WorkspaceState
	{
		internal static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
		};

		[JsonRequired]
		public int Version { get; set; }
		[JsonRequired]
		public RegionState Main { get; set; }
		[JsonRequired]
		public WindowState[] Windows { get; set; }

		internal static bool TryRead( string json, IEnumerable<string> knownIds, IEnumerable<string> requiredIds, out WorkspaceState state )
		{
			state = null;
			if ( json is null ) return false;
			try
			{
				var candidate = JsonSerializer.Deserialize<WorkspaceState>( json, JsonOptions );
				if ( candidate is not { Version: 1, Main: not null, Windows: not null } ) return false;
				var known = new HashSet<string>( knownIds, StringComparer.Ordinal );
				var assigned = new HashSet<string>( StringComparer.Ordinal );
				var open = new HashSet<string>( StringComparer.Ordinal );
				bool ValidateRegion( RegionState region, bool floating )
				{
					if ( region?.Items is null ) return false;
					foreach ( var id in region.Items )
						if ( string.IsNullOrWhiteSpace( id ) || !known.Contains( id ) || !assigned.Add( id ) ) return false;
					var layout = new DockLayout();
					if ( !layout.Restore( region.Layout, region.Items ) || (floating && layout.Root is null) ) return false;
					foreach ( var id in region.Items )
						if ( layout.FindGroup( id ) is not null ) open.Add( id );
					return true;
				}
				if ( !ValidateRegion( candidate.Main, false ) ) return false;
				foreach ( var window in candidate.Windows )
				{
					if ( window is null || !float.IsFinite( window.X ) || !float.IsFinite( window.Y )
						|| !float.IsFinite( window.Width ) || !float.IsFinite( window.Height )
						|| MathF.Abs( window.X ) > 1_000_000 || MathF.Abs( window.Y ) > 1_000_000
						|| window.Width <= 0 || window.Height <= 0 || window.Width > 32768 || window.Height > 32768
						|| !ValidateRegion( window.Region, true ) ) return false;
				}
				if ( requiredIds.Any( id => !open.Contains( id ) ) ) return false;
				state = candidate;
				return true;
			}
			catch ( JsonException ) { return false; }
		}
	}
}
