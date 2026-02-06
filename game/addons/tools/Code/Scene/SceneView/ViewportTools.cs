namespace Editor;

public partial class ViewportTools : Widget
{
	private Widget toolbarWidget;

	private SceneViewWidget sceneViewWidget;

	private float Margin => 6;
	private float Spacing => 6;

	public ViewportTools( SceneViewWidget sceneViewWidget )
	{
		Layout = Layout.Column();

		this.sceneViewWidget = sceneViewWidget;

		Rebuild();
	}

	void AddSeparator( Layout layout )
	{
		layout.AddSpacingCell( Spacing );
		layout.Add( new Separator() );
		layout.AddSpacingCell( Spacing );
	}

	public void Rebuild()
	{
		Layout.Clear( true );

		//
		// Toolbar
		//
		toolbarWidget = Layout.Add( new Widget() );
		toolbarWidget.Name = "ViewportToolbar";
		toolbarWidget.FixedHeight = Theme.ControlHeight + Margin * 2;

		toolbarWidget.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( toolbarWidget.LocalRect );
			return true;
		};

		var toolbar = toolbarWidget.Layout = Layout.Row();
		toolbar.Margin = new Sandbox.UI.Margin( 2, Margin, Margin, Margin );
		toolbar.Spacing = Spacing;

		var left = toolbar.AddRow( 1 );
		left.Spacing = Spacing;
		left.Alignment = TextFlag.LeftCenter;

		var center = toolbar.AddRow( 1 );
		center.Spacing = Spacing;
		center.Alignment = TextFlag.Center;

		var right = toolbar.AddRow( 1 );
		right.Spacing = Spacing;
		right.Alignment = TextFlag.RightCenter;

		// These only get built for game view mode, clear them.
		FrameTimeLabel = null;
		FrameRateLabel = null;
		ResolutionComboBox = null;

		if ( sceneViewWidget.CurrentView == SceneViewWidget.ViewMode.Game )
		{
			BuildToolbarGame( left );
		}
		else
		{
			BuildToolbarLeft( left );
			AddSeparator( left );
			BuildToolbarScene( left );
			BuildToolExtensionToolbar( left );
		}

		toolbar.AddStretchCell();

		var centerGroup = center.Add( AddGroup() );
		BuildPlayToolbar( centerGroup.Layout );

		BuildToolbarRight( right );

		Layout.AddStretchCell();
	}

	[Shortcut( "editor.toggle-fullscreen", "F3", ShortcutType.Window )]
	internal static void ToggleFullscreen()
	{
		EditorWindow.SetFullscreen( SceneViewWidget.Current );
	}

	[EditorEvent.Frame]
	public void OnViewportToolsFrame()
	{
		UpdateDimensions();
		UpdateChildren();
		UpdateToolExtensionToolbar();
	}

	int lastGeometryHash = -1;

	private void UpdateDimensions()
	{
		if ( !Parent.IsValid() )
			return;

		// this wasn't always being triggered properly when relying on widget events from the parent (causing HUGE jank)
		int geometryHash = HashCode.Combine( Parent.ScreenPosition, Parent.Size );
		if ( lastGeometryHash != geometryHash )
		{
			Position = Vector2.Zero;
			Size = Parent.Size;
		}

		lastGeometryHash = geometryHash;
	}

	private void UpdateChildren()
	{
		foreach ( var child in toolbarWidget.Children )
		{
			if ( child is EditorToolButton button )
			{
				button.UpdateState();
			}
		}
	}

	private EditorToolButton AddToggleButton( Layout layout, string tooltip, Func<string> getIcon, Func<bool> getVal, Action<bool> setVal )
	{
		var __getVal = () => { try { return getVal(); } catch ( System.Exception ) { return false; } };
		var __setVal = ( bool b ) => { try { setVal( b ); } catch ( System.Exception ) { } };

		var b = new EditorToolButton();
		b.GetIcon = getIcon;
		b.ToolTip = tooltip;
		b.Action = () => __setVal( !__getVal() );
		b.IsActive = () => __getVal();

		layout.Add( b );
		return b;
	}

	private EditorToolButton AddButton( Layout layout, string tooltip, string getIcon, Action onClick )
	{
		var b = new EditorToolButton();
		b.GetIcon = () => getIcon;
		b.ToolTip = tooltip;
		b.Action = onClick;

		layout.Add( b );
		return b;
	}

	private Widget AddGroup()
	{
		var w = new Widget();
		w.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( w.LocalRect, Theme.ControlRadius );

			return true;
		};

		w.FixedHeight = Theme.RowHeight;
		w.Layout = Layout.Row();
		w.Layout.Spacing = Spacing;
		w.Layout.Margin = new( 2, 0 );
		return w;
	}
}

file class Separator : Widget
{
	public Separator() : base( null )
	{
		FixedHeight = Theme.ControlHeight;
		FixedWidth = 1;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Color.White.WithAlpha( 0.25f ) );
		Paint.DrawRect( LocalRect );
	}
}
