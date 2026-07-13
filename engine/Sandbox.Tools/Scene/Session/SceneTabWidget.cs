using System;

namespace Editor;

/// <summary>
/// The editor's central widget. Every open scene and prefab session lives here
/// as a browser style tab - sessions are never dock widgets, so they can't be
/// floated, split or lost by layout changes.
/// </summary>
sealed class SceneTabWidget : Widget
{
	readonly Widget _tabBar;
	readonly ContentHost _content;
	readonly IconButton _newTab;
	readonly List<SceneTab> _tabs = new();

	SceneTab _currentTab;

	public SceneTabWidget( Widget parent ) : base( parent )
	{
		Name = "SceneTabs";
		Layout = Layout.Column();

		_tabBar = Layout.Add( new TabBarWidget( this ) );
		_content = Layout.Add( new ContentHost( this ), 1 );

		_newTab = new IconButton( "add", EditorScene.NewScene, _tabBar )
		{
			ToolTip = "New scene",
			Background = Color.Transparent,
			FixedWidth = 20,
			FixedHeight = 20
		};

		RebuildTabBar();
	}

	public void Open( SceneEditorSession session )
	{
		if ( _tabs.Any( x => x.Session == session ) ) return;

		var tab = new SceneTab( this, session );
		_tabs.Add( tab );

		session.SceneDock.Visible = false;
		session.SceneDock.Parent = _content;

		RebuildTabBar();
		MakeCurrent( tab );
	}

	public void Remove( SceneEditorSession session )
	{
		var tab = _tabs.FirstOrDefault( x => x.Session == session );
		if ( tab is null ) return;

		CloseTab( tab );
	}

	void CloseTab( SceneTab tab )
	{
		var index = _tabs.IndexOf( tab );
		if ( index < 0 ) return;

		_tabs.Remove( tab );
		tab.Destroy();

		if ( !_tabBar.IsValid() )
			return;

		if ( _tabs.Count == 0 )
		{
			_currentTab = null;
			_content.Clear();
			RebuildTabBar();
			return;
		}

		if ( _currentTab == tab )
		{
			_currentTab = null;

			var neighbour = _tabs[Math.Min( index, _tabs.Count - 1 )];
			neighbour.Session.MakeActive();
		}

		RebuildTabBar();
	}

	public void MakeCurrent( SceneEditorSession session )
	{
		if ( _tabs.FirstOrDefault( x => x.Session == session ) is { } tab )
			MakeCurrent( tab );
	}

	void MakeCurrent( SceneTab tab )
	{
		if ( _currentTab == tab ) return;

		_currentTab = tab;
		_content.Show( tab.Session.SceneDock );

		_tabBar.Update();
	}

	public void UpdateTitle( SceneEditorSession session )
	{
		if ( _tabs.FirstOrDefault( x => x.Session == session ) is not { } tab ) return;

		tab.UpdateGeometry();
		tab.Update();
	}

	[Event( "scene.play" )]
	[Event( "scene.stop" )]
	void OnPlayStateChanged()
	{
		foreach ( var tab in _tabs )
			tab.Update();
	}

	internal bool IsCurrent( SceneTab tab ) => _currentTab == tab;

	void RebuildTabBar()
	{
		_tabBar.Layout.Clear( false );

		foreach ( var tab in _tabs )
		{
			_tabBar.Layout.Add( tab );
		}

		_tabBar.Layout.AddSpacingCell( 2 );
		_tabBar.Layout.Add( _newTab );
		_tabBar.Layout.AddStretchCell();
		_tabBar.Layout.AddSpacingCell( 4 );

		_tabBar.Update();
	}

	sealed class TabBarWidget : Widget
	{
		public TabBarWidget( Widget parent ) : base( parent )
		{
			Layout = Layout.Row();
			FixedHeight = 26;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TabBarBackground );
			Paint.DrawRect( LocalRect );
		}
	}

	/// <summary>
	/// Hosts the scene views stacked on top of each other, so switching doesn't
	/// relayout anything. The outgoing widget stays on top until the end of the
	/// frame, by which point the incoming viewport has presented.
	/// </summary>
	sealed class ContentHost : Widget
	{
		Widget _current;

		public ContentHost( Widget parent ) : base( parent )
		{
		}

		public void Clear()
		{
			if ( _current.IsValid() )
				_current.Visible = false;

			_current = null;
			Update();
		}

		public void Show( Widget widget )
		{
			if ( _current == widget ) return;

			var outgoing = _current;
			_current = widget;

			widget.Position = 0;
			widget.Size = Size;
			widget.Visible = true;

			if ( !outgoing.IsValid() || !outgoing.Visible )
				return;

			widget.Lower();

			EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () =>
			{
				if ( outgoing.IsValid() && outgoing != _current )
					outgoing.Visible = false;

				if ( _current.IsValid() )
					_current.Raise();
			} ) );
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TabBackground );
			Paint.DrawRect( LocalRect );
		}

		protected override void OnResize()
		{
			foreach ( var child in Children )
			{
				if ( !child.Visible ) continue;

				child.Position = 0;
				child.Size = Size;
			}
		}
	}
}

/// <summary>
/// A single scene/prefab tab in the <see cref="SceneTabWidget"/> tab bar.
/// </summary>
sealed class SceneTab : Widget
{
	public SceneEditorSession Session { get; }

	readonly SceneTabWidget _owner;
	readonly IconButton _close;

	bool IsCurrent => _owner.IsCurrent( this );
	bool IsPlaying => Session.IsPlaying;
	string Icon => IsPlaying ? "play_arrow" : Session.IsPrefabSession ? "home_repair_service" : "grid_4x4";
	string Title => Session.SceneDock?.WindowTitle ?? "Untitled";

	public SceneTab( SceneTabWidget owner, SceneEditorSession session ) : base( owner )
	{
		_owner = owner;
		Session = session;
		Cursor = CursorShape.Finger;

		_close = new IconButton( "close", CloseSession, this )
		{
			ToolTip = "Close",
			Background = Color.Transparent,
			Foreground = Theme.TextLight,
			IconSize = 8,
			FixedWidth = 16,
			FixedHeight = 16
		};
	}

	protected override Vector2 SizeHint()
	{
		Paint.SetDefaultFont( 8, 500 );
		return new Vector2( Paint.MeasureText( Title ).x + 58, 26 );
	}

	protected override void OnPaint()
	{
		var hovered = Paint.HasMouseOver;
		var background = IsCurrent ? Theme.TabBackground : hovered ? Theme.SurfaceBackground : Theme.TabInactiveBackground;

		Paint.ClearPen();
		Paint.SetBrush( background );
		Paint.DrawRect( new Rect( 0, 0, Width, Height + Theme.ControlRadius ), Theme.ControlRadius );

		if ( IsCurrent || IsPlaying )
		{
			Paint.SetBrush( IsPlaying ? Theme.Green : Theme.Primary );
			Paint.DrawRect( new Rect( 0, 0, Width, 2 ) );
		}

		var color = IsCurrent || hovered ? Theme.Text : Theme.TextLight;

		Paint.SetPen( IsPlaying ? Theme.Green : color );
		Paint.DrawIcon( new Rect( 8, 0, 16, Height ), Icon, 13 );

		Paint.SetPen( color );
		Paint.SetDefaultFont( 8, 500 );
		Paint.DrawText( new Rect( 29, 0, Width - 52, Height ), Title, TextFlag.LeftCenter );
	}

	protected override void OnResize()
	{
		_close.Position = new Vector2( Width - 20, (Height - _close.Height) * 0.5f + 1 );
	}

	protected override void OnMouseEnter()
	{
		base.OnMouseEnter();
		Update();
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );
		if ( e.LeftMouseButton ) Session.MakeActive();
	}

	void CloseSession()
	{
		// routes through SceneDock.OnClose so the unsaved changes prompt still applies
		Session.SceneDock?.Close();
	}
}
