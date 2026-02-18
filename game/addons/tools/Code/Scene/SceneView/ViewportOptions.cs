namespace Editor;

public partial class ViewportOptions : Widget
{
	SceneViewportWidget SceneViewportWidget;

	public ViewportOptions( SceneViewportWidget sceneViewportWidget )
	{
		SceneViewportWidget = sceneViewportWidget;
		Layout = Layout.Row();
		Layout.Spacing = 4;

		Rebuild();
	}

	[EditorEvent.Hotload]
	public void Rebuild()
	{
		Layout.Clear( true );
		Layout.Add( new IconButton( "tune", OpenViewSettings ) { ToolTip = "View Settings", Background = Theme.ControlBackground } );
	}

	protected override void OnPaint()
	{

	}

	void OpenViewSettings()
	{
		var viewport = GetAncestor<SceneViewportWidget>();
		var so = viewport.State.GetSerialized();

		var menu = new ContextMenu( this );

		{
			// this whole menu should probably just be a popup

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
			var o = menu.AddOption( entry.Title, entry.Icon, () => { viewport.State.RenderMode = val; Rebuild(); } );
			o.Checkable = true;
			o.Checked = viewport.State.RenderMode == val;
		}

		menu.OpenAtCursor();
	}
}
