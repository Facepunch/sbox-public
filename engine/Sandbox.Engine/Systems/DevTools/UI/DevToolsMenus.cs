using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The menus behind the dev tools' strip buttons: the tools visualization mode, and settings - debug drawing toggles and
/// the dev tools' own scale. They're engine <see cref="Popup"/>s, so they open in a window of their own from the popped
/// out dev tools and float over the surface everywhere else.
/// </summary>
internal static class DevToolsMenus
{
	static readonly (SceneCameraDebugMode Mode, DisplayInfo Info)[] Modes = DisplayInfo.ForEnumValues<SceneCameraDebugMode>();

	/// <summary>
	/// The render debug mode the game is drawn with: the active camera's, else <c>mat_toolsvis</c>.
	/// </summary>
	internal static SceneCameraDebugMode VisualizationMode
	{
		get
		{
			var camera = Game.ActiveScene?.Camera?.DebugMode ?? SceneCameraDebugMode.Normal;
			return camera != SceneCameraDebugMode.Normal ? camera : DebugOverlay.ToolsVisualization.mat_toolsvis;
		}
	}

	internal static string VisualizationTitle( SceneCameraDebugMode mode )
	{
		foreach ( var (m, info) in Modes )
			if ( m == mode ) return info.Name ?? mode.ToString();

		return mode.ToString();
	}

	/// <summary>
	/// Set the mode the way the console's render mode picker does: the convar, and the active camera.
	/// </summary>
	static void SetVisualization( SceneCameraDebugMode mode )
	{
		ConVarSystem.Run( $"mat_toolsvis {(int)mode}" );

		if ( Game.ActiveScene?.Camera is { } camera )
			camera.DebugMode = mode;
	}

	internal static void OpenVisualization( Panel source, Popup.PositionMode position )
	{
		var menu = Menu( source, position );
		menu.AddChild( new Label( "Tools visualization", "menu-section" ) );

		var current = VisualizationMode;
		foreach ( var (mode, info) in Modes )
			Row( menu, info.Icon ?? "image", info.Name ?? mode.ToString(), mode == current, () => SetVisualization( mode ) );
	}

	/// <summary>
	/// The debug drawing worth a click rather than a console command: a convar each, and what to call it.
	/// </summary>
	static readonly (string ConVar, string Title)[] DebugToggles =
	[
		("physics_debug_draw", "Physics shapes"),
		("physics_debug_draw_contacts", "Physics contacts"),
		("physics_debug_draw_joints", "Physics joints"),
		("physics_debug_draw_mass", "Physics mass"),
		("physics_debug_draw_bounds", "Physics bounds"),
		("debug_hitbox", "Hitboxes"),
		("mat_wireframe", "Wireframe"),
		("r_showsceneobjectbounds", "Scene object bounds"),
		("r.shadows.debug", "Shadow budget"),
		("ui_visualize_batches", "UI batches"),
	];

	internal static void OpenSettings( Panel source, Popup.PositionMode position )
	{
		var menu = Menu( source, position );

		menu.AddChild( new Label( "Debug", "menu-section" ) );
		foreach ( var (name, title) in DebugToggles )
		{
			if ( ConVarSystem.Find( name ) is not { IsVariable: true } convar ) continue;

			// Cheat convars refuse to change without cheats; say so rather than do nothing
			var locked = convar.IsCheat && !Game.CheatsEnabled;
			var row = Toggle( menu, title, () => IsOn( convar.Value ), () => ConVarSystem.Run( $"{name} {(IsOn( convar.Value ) ? 0 : 1)}" ) );
			row.SetClass( "disabled", locked );
			row.Tooltip = locked ? $"{name} - needs sv_cheats" : name;
		}

		menu.AddChild( new Label( "Dev tools", "menu-section" ) );

		var scale = menu.AddChild( new Panel( null, "menu-row stepper" ) );
		scale.AddChild( new Label( "", "menu-check" ) );
		scale.AddChild( new Label( "Scale", "menu-text" ) );
		var down = scale.AddChild( new Label( "remove", "menu-step" ) );
		var value = scale.AddChild( new Label( $"{DevTools.WindowScale:0.0}x", "menu-value" ) );
		var up = scale.AddChild( new Label( "add", "menu-step" ) );
		down.AddEventListener( "onclick", () => Step( -0.1f ) );
		up.AddEventListener( "onclick", () => Step( 0.1f ) );

		void Step( float by )
		{
			DevTools.WindowScale = MathF.Round( Math.Clamp( DevTools.WindowScale + by, 0.5f, 3f ) * 10f ) / 10f;
			value.Text = $"{DevTools.WindowScale:0.0}x";
		}

		// Always on in the editor and with cheats, so there's nothing to choose there
		var forced = Application.IsEditor || Application.CheatsEnabled;
		var advanced = Toggle( menu, "All tabs", () => DevTools.ShowAdvanced, () => DevTools.AdvancedEnabled = !DevTools.AdvancedEnabled );
		advanced.SetClass( "disabled", forced );
		advanced.Tooltip = forced ? "Always on in the editor and with sv_cheats" : "Every tab, not just the overview (devtools_advanced)";
	}

	static bool IsOn( string value ) =>
		!string.IsNullOrEmpty( value ) && value != "0" && !value.Equals( "false", StringComparison.OrdinalIgnoreCase );

	static Popup Menu( Panel source, Popup.PositionMode position )
	{
		var menu = new Popup( source, position, 0 );
		menu.AddClass( "devtools-menu" );
		menu.CloseWhenParentIsHidden = true;
		return menu;
	}

	/// <summary>
	/// A choice: a check where it's the current one, an optional icon, and its name. Picking one closes the menu.
	/// </summary>
	static Panel Row( Panel menu, string icon, string text, bool selected, Action onPick )
	{
		var row = menu.AddChild( new Panel( null, "menu-row" ) );
		row.SetClass( "selected", selected );
		row.AddChild( new Label( selected ? "check" : "", "menu-check" ) );
		if ( icon is not null ) row.AddChild( new Label( icon, "menu-icon" ) );
		row.AddChild( new Label( text, "menu-text" ) );
		row.AddEventListener( "onclick", () =>
		{
			if ( row.HasClass( "disabled" ) ) return;
			BasePopup.CloseAll();
			onPick();
		} );
		return row;
	}

	/// <summary>
	/// An on and off setting: a check while it's on. Flipping one leaves the menu open, so several can be set in a row.
	/// </summary>
	static Panel Toggle( Panel menu, string text, Func<bool> isOn, Action flip )
	{
		var row = menu.AddChild( new Panel( null, "menu-row" ) );
		var check = row.AddChild( new Label( "", "menu-check" ) );
		row.AddChild( new Label( text, "menu-text" ) );

		void Show()
		{
			var on = isOn();
			check.Text = on ? "check" : "";
			row.SetClass( "selected", on );
		}

		Show();
		row.AddEventListener( "onclick", () =>
		{
			if ( row.HasClass( "disabled" ) ) return;
			flip();
			Show();
		} );
		return row;
	}
}
