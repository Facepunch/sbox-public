using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>
/// A report and settings view. The shared session, not this window, owns the compile.
/// </summary>
internal sealed class SceneCompilerWindow : Dialog
{
	readonly SceneCompileSession _session = SceneCompileSession.Current;

	readonly Label _title;
	readonly Label _status;
	readonly Bar _bar;
	readonly SegmentedControl _tabs;
	readonly Dictionary<string, Widget> _pages = new();
	readonly ListView _report;
	readonly TextEdit _log;
	readonly Button _compile;

	readonly List<Group> _groups = new();
	readonly List<Entry> _lines = new();

	SceneCompileReport _sources;
	string _error;
	string[] _summary;

	IReadOnlyList<string> _displayedLines;
	int _displayedLineCount;
	bool _wasRunning;

	static SceneCompilerWindow _current;

	public override void OnDestroyed()
	{
		_session.Changed -= OnSessionChanged;
		if ( _current == this )
			_current = null;

		base.OnDestroyed();
	}

	/// <summary>
	/// Bring up the compiler for the active scene, reusing the window if it's already open.
	/// </summary>
	[Event( "scene.compile.show-report" )]
	internal static void Open( string page = "Report" )
	{
		SceneCompileSession.Current.Refresh();

		if ( _current is { IsValid: true } )
		{
			_current.SelectPage( page );
			_current.Show();
			return;
		}

		_current = new SceneCompilerWindow( page );
	}

	SceneCompilerWindow( string page ) : base( EditorWindow )
	{
		Window.Title = "Scene Compile Report";
		Window.SetWindowIcon( "hardware" );
		Window.Size = new Vector2( 700, 620 );
		Window.StateCookie = "SceneCompiler";

		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;

		var header = Layout.AddRow();
		header.Spacing = 12;

		_title = header.Add( new Label( "" ) );
		_title.SetStyles( "font-weight: bold;" );

		header.AddStretchCell();

		_tabs = header.Add( new SegmentedControl() );
		_tabs.MinimumWidth = 300;
		_tabs.OnSelectedChanged = ShowPage;

		var report = new ListView( this );
		report.ItemSize = new Vector2( 0, 22 );
		report.ItemPaint = PaintEntry;
		report.ItemClicked = OnEntryClicked;
		report.ItemContextMenu = OnEntryContextMenu;
		report.Margin = 4;
		_report = report;

		_log = new TextEdit( this );
		_log.ReadOnly = true;
		_log.HorizontalScrollbarMode = ScrollbarMode.Off;
		_log.SetStyles( "font-family: Consolas, monospace; padding: 8px;" );

		AddPage( "Report", "list", _report );
		AddPage( "Log", "notes", _log );
		AddPage( "Settings", "settings", new SceneCompileSettingsWidget( this ) );

		ShowPage( "Report" );

		_status = Layout.Add( new Label( "" ) { WordWrap = true } );
		_bar = Layout.Add( new Bar() );
		_bar.FixedHeight = 8;

		var footer = Layout.AddRow();
		footer.Spacing = 8;
		footer.AddStretchCell();

		_compile = footer.Add( new Button.Primary( "Compile", "hardware" ) { Clicked = OnCompile } );

		_session.Changed += OnSessionChanged;
		_wasRunning = _session.Running;
		BuildReport();
		RefreshView();
		SelectPage( page );

		Show();
	}

	/// <summary>
	/// Add one of the window's pages. They all fill the same space with only one of them up, so the
	/// window doesn't turn into three panes fighting over the height.
	/// </summary>
	void AddPage( string title, string icon, Widget page )
	{
		_pages[title] = page;

		_tabs.AddOption( title, icon );

		Layout.Add( page, 1 );
	}

	void ShowPage( string title )
	{
		foreach ( var (name, page) in _pages )
		{
			page.Visible = name == title;
		}
	}

	/// <summary>
	/// Bring a page up, moving the tabs with it.
	/// </summary>
	void SelectPage( string title )
	{
		_tabs.Selected = title;

		ShowPage( title );
	}

	void OnSessionChanged()
	{
		if ( !IsValid )
			return;

		var finished = _wasRunning && !_session.Running;
		var started = !_wasRunning && _session.Running;
		_wasRunning = _session.Running;

		if ( finished )
		{
			BuildReport();
			SelectPage( _session.Status == "Failed" ? "Log" : "Report" );
		}
		else if ( started )
		{
			SelectPage( "Log" );
		}

		RefreshView();
	}

	void RefreshView()
	{
		_status.Text = _session.Status switch
		{
			"" => _session.Error ?? "Ready to compile",
			"Done" => "Compiled",
			"Failed" => "Compile failed. Open the Log tab for details.",
			_ => _session.Status
		};
		_bar.Visible = _session.Running;
		_bar.Fraction = _session.Fraction;

		if ( !ReferenceEquals( _displayedLines, _session.Lines ) )
		{
			_displayedLines = _session.Lines;
			_log.Clear();
			_displayedLineCount = 0;
		}

		while ( _displayedLineCount < _displayedLines.Count )
			_log.AppendPlainText( _displayedLines[_displayedLineCount++] );

		_log.ScrollToBottom();

		if ( _sources != _session.Report || _summary != _session.Summary || _error != _session.Error )
			BuildReport();

		UpdateControls();
	}

	[EditorEvent.Frame]
	void UpdateControls()
	{
		if ( !IsValid )
			return;

		if ( _session.Running )
			_bar.Update();

		_title.Text = _session.Name;
		var running = _session.Running;
		_compile.Text = running ? _session.Cancelling ? "Cancelling" : "Cancel" : "Compile";
		_compile.Icon = running ? "close" : "hardware";
		_compile.Tint = running ? Theme.ButtonBackground : Theme.Primary;
		_compile.Enabled = running ? !_session.Cancelling : _session.CanCompile;
	}

	async void OnCompile()
	{
		if ( _session.Running )
		{
			_session.RequestCancel();
			return;
		}

		if ( !_session.CanCompile )
			return;

		await _session.StartAsync();
	}

	/// <summary>
	/// Everything the compile is going to do, and everything it's going to leave alone grouped by
	/// why, with the objects listed under each so you can go and look at them.
	/// </summary>
	void BuildReport()
	{
		if ( !IsValid )
			return;

		_sources = _session.Report;
		_summary = _session.Summary;
		_error = _session.Error;
		_lines.Clear();
		_groups.Clear();

		if ( _sources is null )
		{
			_lines.Add( new Entry { Text = _error ?? "No scene data to compile.", Icon = "error" } );

			Flatten();
			return;
		}

		if ( _error is not null )
			_lines.Add( new Entry { Text = _error, Icon = "error" } );

		if ( _session.Statistics is { } statistics )
		{
			_lines.Add( new Entry
			{
				Text = $"Completed {statistics.CompletedAt.LocalDateTime:g} in {statistics.Duration.TotalSeconds:n2} s",
				Icon = "schedule"
			} );
			_lines.Add( new Entry
			{
				Text = $"Generated model geometry: {statistics.VertexCount:n0} vertices, {statistics.TriangleCount:n0} triangles",
				Icon = "view_in_ar"
			} );
			_lines.Add( new Entry { Text = $"Aggregate fragments: {statistics.FragmentCount:n0}", Icon = "grid_view" } );
		}

		if ( _summary is not null )
		{
			foreach ( var line in _summary )
			{
				_lines.Add( new Entry { Text = line, Icon = "done" } );
			}
		}

		_lines.Add( new Entry
		{
			Text = $"{_sources.MeshCount:n0} {(_sources.MeshCount == 1 ? "mesh" : "meshes")}, {_sources.PropCount:n0} {(_sources.PropCount == 1 ? "prop" : "props")} selected for aggregation",
			Icon = "category"
		} );

		var groups = new Dictionary<(string Label, SceneCompileSkipReason Reason), Group>();
		var reasons = EditorTypeLibrary.GetEnumDescription( typeof( SceneCompileSkipReason ) );

		foreach ( var skip in _sources.Skipped )
		{
			var key = (skip.Label, skip.Reason);

			if ( !groups.TryGetValue( key, out var group ) )
			{
				group = new Group
				{
					Title = $"{skip.Label} excluded from aggregates - {reasons.GetEntry( skip.Reason ).Title}",
					Reason = skip.Reason
				};
				groups[key] = group;
				_groups.Add( group );
			}

			group.Objects.Add( skip.Component );
		}

		_groups.Sort( ( a, b ) => b.Objects.Count - a.Objects.Count );

		if ( _session.Statistics is { } completed )
		{
			var timings = new Group { Title = "compile stages" };
			foreach ( var stage in completed.Stages )
				timings.Entries.Add( new Entry { Text = $"{stage.Name}: {stage.Duration.TotalSeconds:n2} s", Icon = "schedule", Indent = 20.0f } );
			_groups.Insert( 0, timings );
		}

		Flatten();
	}

	/// <summary>
	/// Feed the list what's on show right now. Folding a reason open or shut is just this again -
	/// the list paints its own rows, so nothing is created or destroyed to make it happen.
	/// </summary>
	void Flatten()
	{
		var items = new List<object>( _lines );

		foreach ( var group in _groups )
		{
			items.Add( group.Header );

			if ( !group.Open )
				continue;

			items.AddRange( group.Entries );
			foreach ( var component in group.Objects )
			{
				items.Add( new Entry { Text = component.IsValid() ? component.GameObject.Name : "(Deleted object)", Icon = "my_location", Indent = 20.0f, Target = component } );
			}
		}

		_report.SetItems( items );
	}

	void OnEntryClicked( object item )
	{
		if ( item is not Entry entry )
			return;

		if ( entry.Group is { } group )
		{
			group.Open = !group.Open;

			Flatten();
			return;
		}

		if ( entry.Target is not null )
		{
			Reveal( entry.Target );
		}
	}

	void OnEntryContextMenu( object item )
	{
		if ( item is not Entry { Group: { } group } )
			return;

		var menu = new ContextMenu( this );
		switch ( group.Reason )
		{
			case SceneCompileSkipReason.NotStatic:
				menu.AddOption( "Make All Static", "push_pin", () => MakeStatic( group ) ).Enabled =
					!_session.Running && !Game.IsPlaying
					&& group.Objects.Any( x => x.IsValid() && x.Scene == _session.Scene && !x.GameObject.IsStatic );
				break;
		}

		if ( menu.HasOptions || menu.HasMenus )
			menu.OpenAtCursor();
		else
			menu.Destroy();
	}

	void MakeStatic( Group group )
	{
		if ( _session.Running || Game.IsPlaying
			|| SceneEditorSession.Active is not { IsPrefabSession: false, IsMounted: false } editor
			|| editor.Scene != _session.Scene )
			return;

		var objects = group.Objects
			.Where( x => x.IsValid() && x.Scene == editor.Scene )
			.Select( x => x.GameObject )
			.Where( x => !x.IsStatic )
			.Distinct()
			.ToArray();

		if ( objects.Length == 0 )
			return;

		using var scene = editor.Scene.Push();
		using ( editor.UndoScope( "Make Objects Static" ).WithGameObjectChanges( objects, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in objects )
				go.IsStatic = true;
		}

		_session.Refresh();
	}

	static void PaintEntry( VirtualWidget item )
	{
		if ( item.Object is not Entry entry )
			return;

		var clickable = entry.Group is not null || entry.Target is not null;
		var hovered = item.Hovered && clickable;

		if ( hovered )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground.Lighten( 0.5f ) );
			Paint.DrawRect( item.Rect, 2.0f );
		}

		var rect = item.Rect.Shrink( 4 + entry.Indent, 0, 4, 0 );
		var color = hovered ? Theme.Blue : Theme.TextControl;

		Paint.SetDefaultFont();

		var icon = entry.Group is { } group ? (group.Open ? "expand_more" : "chevron_right") : entry.Icon;

		if ( !string.IsNullOrEmpty( icon ) )
		{
			Paint.SetPen( color.WithAlpha( 0.6f ) );
			rect.Left += Paint.DrawIcon( rect, icon, 14, TextFlag.LeftCenter ).Width + 6;
		}

		Paint.SetPen( color );
		Paint.DrawText( rect, entry.Text, TextFlag.LeftCenter );
	}

	/// <summary>
	/// Select an object we skipped and look at it, so a reason in the report leads straight to the
	/// thing that caused it.
	/// </summary>
	static void Reveal( Component component )
	{
		if ( !component.IsValid() )
			return;

		var go = component.GameObject;
		var session = SceneEditorSession.Resolve( go );

		if ( session is null )
			return;

		using ( session.Scene.Push() )
		{
			session.Selection.Set( go );
			session.FrameTo( go.GetBounds() );
		}
	}

	/// <summary>
	/// A reason things were skipped, and everything it happened to.
	/// </summary>
	sealed class Group
	{
		public string Title { get; init; }
		public SceneCompileSkipReason Reason { get; init; }
		public List<Component> Objects { get; } = new();
		public List<Entry> Entries { get; } = new();
		public bool Open { get; set; }

		Entry _header;

		/// <summary>
		/// The row that folds this group open and shut. Held onto rather than remade, so the list
		/// keeps the item it already has laid out when the group opens.
		/// </summary>
		public Entry Header => _header ??= new Entry { Text = $"{Objects.Count + Entries.Count} {Title}", Group = this };
	}

	/// <summary>
	/// A line of the report.
	/// </summary>
	sealed class Entry
	{
		public string Text { get; init; }
		public string Icon { get; init; }
		public float Indent { get; init; }
		public Group Group { get; init; }
		public Component Target { get; init; }
	}
	/// <summary>
	/// How far through the current phase we are, drawn as a bar because a compile has no idea how
	/// long it's going to take.
	/// </summary>
	sealed class Bar : Widget
	{
		public float Fraction { get; set; }

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.ControlBackground, 1.0f );
			Paint.SetBrush( Theme.WidgetBackground.Darken( 0.1f ) );
			Paint.DrawRect( LocalRect, 2.0f );

			if ( Fraction == 0.0f )
				return;

			var filled = SceneCompileProgress.Fill( LocalRect.Shrink( 1 ), Fraction );

			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary );
			Paint.DrawRect( filled, 2.0f );
		}
	}
}
