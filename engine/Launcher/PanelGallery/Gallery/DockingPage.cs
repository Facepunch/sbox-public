namespace Sandbox.PanelGallery;

/// <summary>
/// A stateful docking workspace with layout persistence and native floating windows.
/// </summary>
public class DockingPage : GalleryPage
{
	readonly DockHost _host;
	readonly PanelDockWindows _windows;
	readonly Sandbox.UI.Label _status;
	readonly string _defaultLayout;
	string _savedLayout;
	string _message = "Default layout ready.";
	int _objects;
	int _runs;
	int _imports;
	int _commands;

	/// <summary>
	/// Builds an editor workspace whose panels retain their text and counters when moved.
	/// </summary>
	public DockingPage() : base( "Docking", "Drag a tab to preview a new dock position. Right-click a tab and choose Float to open a separate window. Hover a highlighted section to reveal docking guides: " +
		"drop on an edge guide to split or the center guide to group tabs; the outer guides split the whole workspace. " +
		"Drop on a tab strip to reorder. Release away from docking targets or press Escape to leave the tab where it was. " +
		"Save and Restore preserve floating windows, their positions and sizes. Reset restores the default docked layout. Panel contents are preserved." )
	{
		AddClass( "docking-page" );
		_host = new DockHost();
		_host.AddClass( "demo-dock-workspace" );
		_windows = new PanelDockWindows( _host )
		{
			ConfigureWindow = root =>
			{
				root.AddClass( "editor-window demo-dock-window" );
				root.SetClass( "style-light", Ancestors.Any( x => x.HasClass( "style-light" ) ) );
				root.StyleSheet.Load( "/styles/editor.scss" );
				root.StyleSheet.Load( "/styles/controlgallery.scss" );
			}
		};

		var toolbar = Case( "Layout" );
		toolbar.AddChild( new Sandbox.UI.Button( "Reset", "restart_alt", "flatbutton", () => Restore( _defaultLayout, "Default layout restored." ) ) );
		toolbar.AddChild( new Sandbox.UI.Button( "Save", "save", "primarybutton", () =>
		{
			_savedLayout = _windows.State;
			_message = $"Workspace saved locally ({_savedLayout.Length} characters), including floating windows.";
			UpdateStatus();
		} ) );
		toolbar.AddChild( new Sandbox.UI.Button( "Restore", "restore", "flatbutton", () => Restore( _savedLayout, "Saved layout restored." ) ) );

		var reopen = Case( "Reopen / focus a panel (returns floating panels first)" );
		foreach ( var (id, title) in new[] { ("hierarchy", "Hierarchy"), ("inspector", "Inspector"), ("assets", "Assets"), ("console", "Console") } )
		{
			reopen.AddChild( new Sandbox.UI.Button( title, "tab", "flatbutton", () =>
			{
				_windows.DockAll();
				if ( !_host.IsOpen( id ) ) _host.Dock( id );
				_host.Activate( id );
				_message = $"{title} opened. Existing text and counters are unchanged.";
				UpdateStatus();
			} ) );
		}

		var sizing = Case( "Workspace height" );
		var height = new Sandbox.UI.SliderControl( 360, 820, 10 ) { Value = 560 };
		var heightLabel = sizing.Add.Label( "560 px" );
		height.OnValueChanged = value =>
		{
			_host.Style.Height = value;
			heightLabel.Text = $"{value:0} px";
		};
		sizing.AddChild( height );
		AddChild( _host );

		BuildPanels();
		_host.Dock( "scene" );
		_host.Dock( "hierarchy", position: DockPosition.Left, fraction: 0.22f );
		_host.Dock( "inspector", position: DockPosition.Right, fraction: 0.25f );
		_host.Dock( "assets", "scene", DockPosition.Bottom, 0.35f );
		_host.Dock( "console", "assets" );
		_host.Activate( "assets" );
		_defaultLayout = _windows.State;
		_savedLayout = _defaultLayout;

		_status = Output();
		_host.LayoutChanged += UpdateStatus;
		UpdateStatus();
	}

	void BuildPanels()
	{
		var hierarchy = Pane( "hierarchy", "Hierarchy" );
		hierarchy.AddChild( new Sandbox.UI.TextEntry { Placeholder = "Filter objects", Icon = "search" } );
		hierarchy.Add.Label( "untitled.scene", "dock-demo-heading" );
		var objects = hierarchy.Add.Panel( "dock-demo-list" );
		foreach ( var name in new[] { "Main Camera", "Directional Light", "Player", "Ground" } )
			objects.Add.Label( name, "dock-demo-object" );
		hierarchy.AddChild( new Sandbox.UI.Button( "Add object", "add", "flatbutton", () =>
			objects.Add.Label( $"New Object {++_objects}", "dock-demo-object" ) ) );

		var scene = Pane( "scene", "Scene", false );
		scene.AddClass( "dock-demo-scene" );
		scene.Add.Label( "PERSPECTIVE / LIT", "dock-demo-heading" );
		var viewport = scene.Add.Panel( "dock-demo-viewport" );
		viewport.Add.Icon( "deployed_code", "dock-demo-scene-icon" );
		viewport.Add.Label( "Scene preview", "dock-demo-heading" );
		viewport.Add.Label( "This pane cannot be closed.", "dock-demo-muted" );
		var runs = scene.Add.Label( "Simulation runs: 0", "dock-demo-muted" );
		scene.AddChild( new Sandbox.UI.Button( "Run simulation", "play_arrow", "primarybutton", () =>
			runs.Text = $"Simulation runs: {++_runs}" ) );

		var inspector = Pane( "inspector", "Inspector" );
		inspector.Add.Label( "OBJECT", "dock-demo-heading" );
		inspector.AddChild( new Sandbox.UI.TextEntry { Text = "Player", Placeholder = "Object name" } );
		inspector.Add.Label( "Notes", "dock-demo-muted" );
		var notes = new Sandbox.UI.TextEntry { Multiline = true, Text = "Spawn near the courtyard.\nKeep these notes while moving this pane." };
		notes.AddClass( "dock-demo-notes" );
		inspector.AddChild( notes );
		var applied = inspector.Add.Label( "No changes applied", "dock-demo-muted" );
		int applies = 0;
		inspector.AddChild( new Sandbox.UI.Button( "Apply", "check", "primarybutton", () =>
			applied.Text = $"Applied {++applies} times" ) );

		var assets = Pane( "assets", "Assets" );
		assets.AddChild( new Sandbox.UI.TextEntry { Placeholder = "Search assets", Icon = "search" } );
		var files = assets.Add.Panel( "dock-demo-files" );
		foreach ( var name in new[] { "Scenes", "Models", "Materials", "Sounds" } )
		{
			files.AddChild( new Sandbox.UI.Button( name, "folder", "flatbutton", () =>
			{
				_message = $"Browsing Assets / {name}";
				UpdateStatus();
			} ) );
		}
		var importRow = assets.Add.Panel( "dock-demo-import" );
		var imports = importRow.Add.Label( "Imported assets: 0", "dock-demo-muted" );
		importRow.AddChild( new Sandbox.UI.Button( "Import asset", "file_upload", "flatbutton", () =>
			imports.Text = $"Imported assets: {++_imports}" ) );

		var console = Pane( "console", "Console" );
		var command = new Sandbox.UI.TextEntry { Text = "scene.validate", Placeholder = "Command" };
		console.AddChild( command );
		var log = console.Add.Label( "Ready. Enter a command and click Run.", "dock-demo-log" );
		console.AddChild( new Sandbox.UI.Button( "Run", "terminal", "flatbutton", () =>
			log.Text = $"[{++_commands}] {command.Text}\nCompleted in the gallery demo." ) );
	}

	Panel Pane( string id, string title, bool canClose = true )
	{
		var panel = new Panel();
		panel.AddClass( "dock-demo-pane" );
		var icon = id switch
		{
			"hierarchy" => "account_tree",
			"scene" => "videocam",
			"inspector" => "tune",
			"assets" => "folder",
			"console" => "terminal",
			_ => null
		};
		_host.Register( id, title, panel, canClose, icon );
		return panel;
	}

	void Restore( string layout, string message )
	{
		_message = _windows.RestoreState( layout )
			? $"{message} Floating windows and panel contents were preserved."
			: "Could not restore the workspace layout.";
		UpdateStatus();
	}

	void UpdateStatus()
	{
		if ( !_status.IsValid || IsDeleting ) return;
		var panels = new[] { "hierarchy", "scene", "inspector", "assets", "console" };
		_status.Text = _message + "\n" + string.Join( " | ", panels.Select( id =>
			$"{id}: {(_host.Find( id ) is null ? "floating" : _host.IsOpen( id ) ? "docked" : "closed")}" ) );
	}

	/// <summary>
	/// Closes floating windows when the gallery page is removed.
	/// </summary>
	public override void OnDeleted()
	{
		_host.LayoutChanged -= UpdateStatus;
		_windows.Dispose();
		base.OnDeleted();
	}
}
