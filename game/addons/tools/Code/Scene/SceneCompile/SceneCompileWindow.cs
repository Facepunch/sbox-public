using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Collections.Generic;
using System.Globalization;
using PanelButton = Sandbox.UI.Button;
using PanelLabel = Sandbox.UI.Label;

namespace Editor;

/// <summary>
/// Selects and runs scene compile jobs and displays the session's live output.
/// </summary>
[StyleSheet.Inline( "scene-compile", Styles )]
internal sealed partial class SceneCompileWindow : Panel
{
	readonly SceneCompileSession session = SceneCompileSession.Current;
	readonly PanelWindow window;
	readonly PanelLabel name;
	readonly Sandbox.UI.StatusBar status;
	readonly Panel steps;
	readonly Panel log;
	readonly PanelProgressBar progress;
	readonly NumberEntry cost;
	readonly NumberEntry size;
	readonly PanelButton compile;
	readonly PanelButton reset;
	readonly PanelButton clean;
	readonly PanelButton clear;
	readonly Sandbox.UI.TabPanel tabs;
	readonly Sandbox.UI.Tab diagnosticsTab;
	readonly Sandbox.UI.Tab compileTab;
	readonly SceneCompileResults results;
	readonly HashSet<string> excluded = new();
	IReadOnlyDictionary<string, int> groups;
	IReadOnlySet<string> clearableGroups;
	IReadOnlyList<string> lines;
	int shown;
	bool showingRunningGroups;
	bool actionsEnabled;
	Scene scene;

	[Event( "scene.compile.show-report" )]
	internal static void ShowReport( string page ) => Open( showResults: true );

	internal static void Open( bool showResults = false )
	{
		SceneCompileSession.Current.Refresh();
		var existing = PanelWindow.All.SelectMany( w => w.Root.Children ).OfType<SceneCompileWindow>().FirstOrDefault();
		if ( existing is not null )
		{
			if ( showResults ) existing.tabs.TabBar.SelectTab( existing.diagnosticsTab );
			existing.window.Focus();
			return;
		}

		var window = new PanelWindow( "Scene Compiler", new Vector2( 840, 530 ) ) { MinSize = new Vector2( 640, 420 ) };
		var panel = new SceneCompileWindow( window ) { Parent = window.Root };
		if ( showResults ) panel.tabs.TabBar.SelectTab( panel.diagnosticsTab );
		window.MoveToCenter();
		window.Focus();
	}

	SceneCompileWindow( PanelWindow window )
	{
		this.window = window;
		AddClass( "scene-compile" );
		tabs = AddChild<Sandbox.UI.TabPanel>();
		var body = new Panel();
		compileTab = tabs.AddTab( "Compile", body );
		var sidebar = body.Add.Panel( "sidebar" );
		name = sidebar.Add.Label( "", "scene-name" );
		sidebar.Add.Label( "Compile stages", "heading" );
		steps = sidebar.Add.Panel( "steps" );

		log = body.Add.Panel( "log" );
		log.PreferScrollToBottom = true;

		var settings = new Panel();
		settings.AddClass( "advanced" );
		tabs.AddTab( "Advanced", settings );
		settings.Add.Label( "Geometry", "heading" );
		var options = settings.AddChild<Form>();
		cost = Number( options, "Aggregate cost",
			"Higher values group more geometry into each draw call. Lower values let the renderer skip smaller groups when they’re out of view. Default: 32.",
			() => session.AggregateCost, v => session.AggregateCost = v );
		size = Number( options, "Target chunk size",
			"Large meshes are split into chunks of roughly this size in world units. Smaller chunks help when only part of a mesh is visible. Whole triangles stay intact. Default: 2048.",
			() => session.MaxChunkSize, v => session.MaxChunkSize = v );
		reset = options.AddChild( new PanelButton( "Reset defaults", session.ResetSettings ) );

		results = new SceneCompileResults();
		results.AddClass( "diagnostics" );
		diagnosticsTab = tabs.AddTab( "Diagnostics", results );
		progress = AddChild<PanelProgressBar>();
		status = AddChild<Sandbox.UI.StatusBar>();
		status.AddClass( "footer" );
		clear = status.AddRight( new PanelButton( "Clear all", "delete_sweep", ClearAll ) );
		clear.Tooltip = "Clear bake references and delete the entire scene data folder and compiled runtime scene.";
		clean = status.AddRight( new PanelButton( "Clean compile", "restart_alt", () => Run( cleanCompile: true ) ) );
		clean.Tooltip = "Delete the entire scene data folder and compiled runtime scene, then rebuild all stages.";
		compile = status.AddRight( new PanelButton( "Compile", "hardware", () => Run( cleanCompile: false ) ) );
		compile.AddClass( "primary" );

		session.Changed += Refresh;
		Refresh();
	}

	NumberEntry Number( Form form, string title, string description, Func<float> get, Action<float> set )
	{
		var entry = new NumberEntry { NumberFormat = "G9" };
		form.AddRow( title, entry );
		form.Add.Label( description, "description" );

		void Apply()
		{
			var value = float.Parse( entry.FixNumeric(), CultureInfo.InvariantCulture );
			if ( scene == session.Scene && !session.Running && float.IsFinite( value ) && value > 0 && value != get() )
				set( value );
			entry.Text = get().ToString( "G9", CultureInfo.InvariantCulture );
		}
		entry.AddEventListener( "onblur", Apply );
		entry.AddEventListener( "onsubmit", Apply );
		return entry;
	}

	async void ClearAll()
	{
		if ( clear.Disabled )
			return;

		tabs.TabBar.SelectTab( compileTab );
		await session.ClearAsync();
	}

	async void Run( bool cleanCompile )
	{
		if ( session.Running )
		{
			session.RequestCancel();
			return;
		}

		tabs.TabBar.SelectTab( compileTab );
		if ( cleanCompile && !clean.Disabled )
			await session.CleanCompileAsync();
		else if ( !cleanCompile && !compile.Disabled )
			await session.StartAsync( groups.Where( g => g.Value > 0 && !excluded.Contains( g.Key ) ).Select( g => g.Key ).ToHashSet() );
	}

	async void RunStage( string group )
	{
		if ( session.CanCompile && session.WorkGroups.TryGetValue( group, out var count ) && count > 0 )
			await session.RunStageAsync( group );
	}

	async void ClearStage( string group )
	{
		if ( session.CanCompile && session.ClearableGroups.Contains( group ) )
			await session.ClearAsync( group );
	}

	void Refresh()
	{
		var sceneChanged = scene != session.Scene;
		if ( sceneChanged )
		{
			scene = session.Scene;
			excluded.Clear();
			if ( session.Running )
				excluded.UnionWith( session.WorkGroups.Keys.Except( session.RunningGroups ) );
		}

		name.Text = session.Name;
		// Keep row panels alive across asset notifications so a queued click retains its target.
		var updateRows = sceneChanged || showingRunningGroups != session.Running || actionsEnabled != session.CanCompile
			|| groups != session.WorkGroups && (groups is null || !groups.SequenceEqual( session.WorkGroups ))
			|| clearableGroups != session.ClearableGroups && (clearableGroups is null || !clearableGroups.SetEquals( session.ClearableGroups ));
		groups = session.WorkGroups;
		clearableGroups = session.ClearableGroups;
		if ( updateRows )
		{
			showingRunningGroups = session.Running;
			actionsEnabled = session.CanCompile;
			steps.DeleteChildren( true );
			foreach ( var (group, count) in groups )
			{
				var row = steps.Add.Panel( "stage" );
				var box = row.AddChild( new Sandbox.UI.Checkbox
				{
					LabelText = count > 1 ? $"{group} ({count})" : group,
					Checked = session.Running ? session.RunningGroups.Contains( group ) : count > 0 && !excluded.Contains( group ),
					AcceptsFocus = !session.Running && count > 0
				} );
				box.SetClass( "unavailable", count == 0 );
				box.ValueChanged = enabled =>
				{
					if ( session.Running || count == 0 )
					{
						box.Checked = session.Running && session.RunningGroups.Contains( group );
						return;
					}
					if ( enabled ) excluded.Remove( group );
					else excluded.Add( group );
					Refresh();
				};

				row.AddChild( new PanelButton( null, "play_arrow", () => RunStage( group ) )
				{
					Tooltip = $"Run {group}",
					Disabled = !session.CanCompile || count == 0
				} );
				row.AddChild( new PanelButton( null, "delete", () => ClearStage( group ) )
				{
					Tooltip = $"Clear {group}",
					Disabled = !session.CanCompile || !session.ClearableGroups.Contains( group )
				} );
			}
		}

		if ( lines != session.Lines )
		{
			log.DeleteChildren( true );
			lines = session.Lines;
			shown = 0;
		}
		while ( shown < lines.Count )
			log.Add.Label( lines[shown++], "line" ).Selectable = true;

		var running = session.Running;
		steps.SetClass( "locked", running );
		cost.Disabled = size.Disabled = reset.Disabled = running || !scene.IsValid();
		if ( sceneChanged || !cost.HasFocus ) cost.Text = session.AggregateCost.ToString( "G9", CultureInfo.InvariantCulture );
		if ( sceneChanged || !size.HasFocus ) size.Text = session.MaxChunkSize.ToString( "G9", CultureInfo.InvariantCulture );
		results.Refresh();
		clear.Disabled = clean.Disabled = !session.CanCompile;
		var message = session.Error ?? (!running && scene?.Editor?.HasUnsavedChanges == true ? "Save the scene before compiling"
			: session.Status.Length > 0 ? session.Status : !scene.IsValid() ? "Open a scene"
			: !groups.Values.Any( count => count > 0 ) ? "Nothing to compile" : "Ready to compile");
		status.ShowMessage( message, seconds: 0 );
		progress.Fraction = running && session.Fraction < 0 ? null : session.Fraction;
		compile.Text = running ? session.Cancelling ? "Cancelling…" : "Cancel" : "Compile";
		compile.Icon = running ? "close" : "hardware";
		compile.Disabled = running ? session.Cancelling : !session.CanCompile || !groups.Any( g => g.Value > 0 && !excluded.Contains( g.Key ) );
	}

	/// <summary>
	/// Disconnects the window from the compile when it closes.
	/// </summary>
	public override void OnDeleted()
	{
		session.Changed -= Refresh;
		base.OnDeleted();
	}
}
