namespace Editor;

/// <summary>
/// Quick scene compilation and status, with settings and diagnostics available on demand.
/// </summary>
sealed class SceneCompileToolbar : Widget
{
	readonly SceneCompileSession _session = SceneCompileSession.Current;
	readonly IconButton _button;
	ContextMenu _menu;
	Label _name;
	Label _state;
	Label _description;
	Option _cancel;
	Widget _progress;
	Menu _advanced;
	Option _report;
	Option _log;
	bool _menuReady;

	bool _wasRunning;

	public SceneCompileToolbar( Widget parent ) : base( parent )
	{
		FixedWidth = Theme.ControlHeight + Theme.RowHeight * 0.5f;
		FixedHeight = Theme.ControlHeight;
		Layout = Layout.Row();
		Layout.Spacing = 0;
		_button = Layout.Add( new IconButton( "hardware", Compile )
		{
			FixedSize = Theme.ControlHeight,
			IconSize = HeaderBarStyle.IconSize,
			Background = Color.Transparent,
			Foreground = Theme.TextLight
		} );
		Layout.Add( new IconButton( "arrow_drop_down", OpenMenu )
		{
			FixedWidth = Theme.RowHeight * 0.5f,
			FixedHeight = Theme.ControlHeight,
			IconSize = HeaderBarStyle.IconSize,
			Background = Color.Transparent,
			Foreground = Theme.TextLight,
			ToolTip = "Scene compile status and settings"
		} );

		_wasRunning = _session.Running;
		_session.Changed += OnSessionChanged;
	}

	public override void OnDestroyed()
	{
		_session.Changed -= OnSessionChanged;
		_menu?.Close();
		base.OnDestroyed();
	}

	[EditorEvent.Frame]
	void UpdateScene()
	{
		if ( !IsValid )
			return;

		var active = SceneEditorSession.Active;
		Visible = !Game.IsPlaying && GetAncestor<SceneViewWidget>().IsValid() && active is { IsPrefabSession: false };
		if ( !Visible )
		{
			_menu?.Close();
			return;
		}

		UpdateControls();
	}

	void OnSessionChanged()
	{
		if ( _wasRunning != _session.Running )
		{
			_wasRunning = _session.Running;
			_menu?.Close();
		}

		UpdateControls();
	}

	(string Title, string Detail, Color Color) Status()
	{
		if ( _session.Running )
			return (_session.Status, "", Theme.Blue);

		if ( Game.IsPlaying )
			return ("Play mode", "Stop playing before compiling the scene.", Theme.TextLight);

		if ( _session.Error is null && _session.HasSources && !_session.HasCompileGeometry )
			return ("Nothing to compile", "This scene has no geometry to bake.", Theme.TextLight);

		if ( _session.Scene?.Editor?.HasUnsavedChanges == true )
			return ("Unsaved changes", "Save before compiling the scene.", Theme.Yellow);

		if ( _session.HasResult && _session.Status == "Failed" )
			return ("Compile failed", _session.Error ?? "Open the log to see why compilation failed.", Theme.Red);

		if ( _session.Error is not null )
			return ("Cannot compile", _session.Error, Theme.Yellow);

		return ("Ready to compile", "Compile geometry into .scene_c. Changed source recompiles normally; an unchanged save keeps the bake.", Theme.TextLight);
	}

	void UpdateControls()
	{
		if ( !IsValid )
			return;

		var status = Status();
		var keys = EditorShortcuts.GetDisplayKeys( "scene.compile" );
		_button.Enabled = _session.CanCompile;
		_button.ToolTip = string.IsNullOrEmpty( keys )
			? $"Compile Scene: {status.Title}"
			: $"Compile Scene [{keys}]: {status.Title}";
		if ( !_button.Enabled && !string.IsNullOrEmpty( status.Detail ) )
			_button.ToolTip += $"\n{status.Detail}";
		_button.Cursor = _button.Enabled ? CursorShape.Finger : CursorShape.Arrow;
		_button.Foreground = status.Color;
		_button.Update();
		Update();

		if ( !_menuReady || !_menu.IsValid() )
			return;

		_name.Text = _session.Name;
		_state.Text = status.Title;
		_state.Color = status.Color;
		_description.Text = status.Detail;
		_description.ToolTip = _session.Error ?? "";
		_description.Visible = !string.IsNullOrEmpty( status.Detail );
		if ( _cancel.IsValid() )
		{
			_cancel.Text = _session.Cancelling ? "Cancelling..." : "Cancel compile";
			_cancel.Enabled = _session.Running && !_session.Cancelling;
		}
		_progress.Visible = _session.Running;
		_progress.Update();
		_advanced.Enabled = !_session.Running && _session.Scene.IsValid() && !Game.IsPlaying;
		_report.Enabled = _session.HasSources || _session.Error is not null;
		_log.Enabled = _session.Lines.Count > 0;
	}

	[Menu( "Editor", "Scene/Compile Scene", "hardware", Priority = 1001 )]
	[Shortcut( "scene.compile", "F9", typeof( SceneViewWidget ) )]
	public static async void Compile()
	{
		if ( Game.IsPlaying )
			return;

		await SceneCompileSession.Current.StartAsync();
	}

	void OpenMenu()
	{
		if ( _menu.IsValid() )
		{
			_menu.Close();
			return;
		}

		if ( !_session.Running )
			_session.Refresh();

		_menuReady = false;
		_menu = new ContextMenu( this );
		var content = new Widget( _menu ) { FixedWidth = 350 };
		content.OnPaintOverride = () =>
		{
			Paint.SetBrushAndPen( Theme.WidgetBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( content.LocalRect.Shrink( 2 ), 2 );
			return true;
		};
		content.Layout = Layout.Column();
		content.Layout.Margin = 8;
		content.Layout.Spacing = 6;
		var heading = content.Layout.AddRow();
		heading.Spacing = 12;
		_name = heading.Add( new Label( "" ) { WordWrap = true, MaximumWidth = 200 } );
		heading.AddStretchCell();
		_state = heading.Add( new Label( "" ) );
		_description = content.Layout.Add( new Label( "" ) { WordWrap = true } );
		_progress = content.Layout.Add( new Widget() { FixedHeight = 3 } );
		_progress.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( _progress.LocalRect, 2 );
			Paint.SetBrush( Theme.Primary );
			var rect = SceneCompileProgress.Fill( _progress.LocalRect, _session.Fraction );
			Paint.DrawRect( rect, 2 );
			return true;
		};
		_menu.AddWidget( content );
		_menu.AddSeparator();
		_cancel = null;
		if ( _session.Running )
		{
			_cancel = _menu.AddOption( "Cancel compile", "close", _session.RequestCancel );
			_menu.AddSeparator();
		}

		_advanced = _menu.AddMenu( "Advanced settings", "tune" );
		_advanced.AddWidget( new SceneCompileSettingsWidget( _advanced ) { FixedWidth = 350 } );
		_report = _menu.AddOption( "View report", "list", () => SceneCompilerWindow.Open() );
		_log = _menu.AddOption( "View log", "notes", () => SceneCompilerWindow.Open( "Log" ) );
		_menuReady = true;
		UpdateControls();
		_menu.OpenAt( ScreenRect.BottomLeft + new Vector2( 0, 4 ), false );
	}

	protected override void OnPaint()
	{
		base.OnPaint();
		if ( !_session.Running )
			return;

		Paint.ClearPen();
		Paint.SetBrush( Theme.Blue );
		Paint.DrawRect( SceneCompileProgress.Fill( new Rect( 2, Height - 2, Width - 4, 2 ), _session.Fraction ), 1 );
	}
}
