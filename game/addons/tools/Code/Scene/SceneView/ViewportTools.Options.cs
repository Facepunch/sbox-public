namespace Editor;

partial class ViewportTools
{
	private void BuildToolbarRight( Layout layout )
	{
		{
			var group = AddGroup();

			group.Layout.Add( new ViewportButton( "wifi", OpenNetworkSettings ) { ToolTip = "Network settings" } );

			layout.Add( group );
		}

		AddSeparator( layout );

		{
			var group = AddGroup();

			if ( sceneViewWidget.CurrentView != SceneViewWidget.ViewMode.Game )
				group.Layout.Add( new ViewportButton( "tune", OpenViewSettings ) { ToolTip = "View Settings" } );

			group.Layout.Add( new ViewportButton( "grid_view", OpenSceneViewModeMenu ) { ToolTip = "Layout" } );
			group.Layout.Add( new ViewportButton( "crop_free", ToggleFullscreen ) { ToolTip = "Toggle Fullscreen" } );

			layout.Add( group );
		}
	}

	void OpenViewSettings()
	{
		var viewport = sceneViewWidget.LastSelectedViewportWidget;
		if ( !viewport.IsValid() )
			return;

		var so = viewport.State.GetSerialized();

		var menu = new ContextMenu( this );

		{
			var widget = new Widget( menu );
			widget.OnPaintOverride = () =>
			{
				Paint.SetBrushAndPen( Theme.WidgetBackground.WithAlpha( 0.5f ) );
				Paint.DrawRect( widget.LocalRect.Shrink( 2 ), 2 );
				return true;
			};

			var cs = new ControlSheet();

			cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.View ) ) );
			cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.WireframeMode ) ) );
			cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.EnablePostProcessing ) ) );

			if ( viewport.SceneView.Session.Scene is PrefabScene )
			{
				cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.EnablePrefabLighting ) ) );
			}

			cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.ShowSkyIn2D ) ) );

			cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.ShowGrid ) ) );
			cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.GridOpacity ) ) );
			if ( viewport.State.View == SceneViewportWidget.ViewMode.Perspective )
			{
				cs.AddRow( so.GetProperty( nameof( SceneViewportWidget.ViewportState.GridAxis ) ) );
			}

			widget.Layout = cs;
			widget.MaximumWidth = 400;

			menu.AddWidget( widget );
		}

		menu.AddSeparator();

		foreach ( var entry in EditorTypeLibrary.GetEnumDescription( typeof( SceneCameraDebugMode ) ) )
		{
			var val = (SceneCameraDebugMode)entry.ObjectValue;
			var o = menu.AddOption( entry.Title, entry.Icon, () => viewport.State.RenderMode = val );
			o.Checkable = true;
			o.Checked = viewport.State.RenderMode == val;
		}

		menu.OpenAtCursor();
	}

	private static readonly Pixmap LayoutOne = Pixmap.FromFile( "toolimages:qcontrols/split_single.png" );
	private static readonly Pixmap LayoutTwoH = Pixmap.FromFile( "toolimages:qcontrols/split_two_horizontal.png" );
	private static readonly Pixmap LayoutTwoV = Pixmap.FromFile( "toolimages:qcontrols/split_two_vertical.png" );
	private static readonly Pixmap LayoutThreeLeft = Pixmap.FromFile( "toolimages:qcontrols/split_two_right_one_left.png" );
	private static readonly Pixmap LayoutThreeRight = Pixmap.FromFile( "toolimages:qcontrols/split_two_left_one_right.png" );
	private static readonly Pixmap LayoutThreeTop = Pixmap.FromFile( "toolimages:qcontrols/split_two_bottom_one_top.png" );
	private static readonly Pixmap LayoutThreeBottom = Pixmap.FromFile( "toolimages:qcontrols/split_two_top_one_bottom.png" );
	private static readonly Pixmap LayoutFour = Pixmap.FromFile( "toolimages:qcontrols/split_four_way.png" );

	void OpenSceneViewModeMenu()
	{
		var menu = new ContextMenu( null );

		foreach ( var entry in EditorTypeLibrary.GetEnumDescription( typeof( SceneViewWidget.ViewportLayoutMode ) ) )
		{
			var layout = (SceneViewWidget.ViewportLayoutMode)entry.ObjectValue;
			var icon = layout switch
			{
				SceneViewWidget.ViewportLayoutMode.One => LayoutOne,
				SceneViewWidget.ViewportLayoutMode.TwoHorizontal => LayoutTwoH,
				SceneViewWidget.ViewportLayoutMode.TwoVertical => LayoutTwoV,
				SceneViewWidget.ViewportLayoutMode.ThreeLeft => LayoutThreeLeft,
				SceneViewWidget.ViewportLayoutMode.ThreeRight => LayoutThreeRight,
				SceneViewWidget.ViewportLayoutMode.ThreeTop => LayoutThreeTop,
				SceneViewWidget.ViewportLayoutMode.ThreeBottom => LayoutThreeBottom,
				SceneViewWidget.ViewportLayoutMode.Four => LayoutFour,
				_ => null
			};

			var o = menu.AddOption( entry.Title, null, () => sceneViewWidget.ViewportLayout = layout );
			o.SetIcon( icon );
			o.Checkable = true;
			o.Checked = sceneViewWidget.ViewportLayout == layout;
			o.Enabled = !sceneViewWidget.Session.IsPlaying; // Not great, but this stuff needs straightening out overall
		}

		menu.OpenAtCursor();
	}
}
