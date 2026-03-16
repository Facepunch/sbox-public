namespace Editor.Preferences;

internal class PageSceneView : Widget
{
	public PageSceneView( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		Layout.Add( new Label.Subtitle( "Scene View" ) );

		var sheet = new ControlSheet();

		sheet.AddProperty( () => EditorPreferences.CameraFieldOfView );

		sheet.AddProperty( () => EditorPreferences.CameraZNear );
		sheet.AddProperty( () => EditorPreferences.CameraZFar );

		sheet.AddProperty( () => EditorPreferences.CameraSpeed );
		sheet.AddProperty( () => EditorPreferences.CameraMovementSmoothing );

		sheet.AddProperty( () => EditorPreferences.CameraSensitivity );

		sheet.AddProperty( () => EditorPreferences.CreateObjectsAtOrigin );
		sheet.AddProperty( () => EditorPreferences.CameraInvertPan );

		sheet.AddProperty( () => EditorPreferences.HideRotateCursor );
		sheet.AddProperty( () => EditorPreferences.HidePanCursor );
		sheet.AddProperty( () => EditorPreferences.HideOrbitCursor );

		sheet.AddProperty( () => EditorPreferences.InvertOrbitZoom );
		sheet.AddProperty( () => EditorPreferences.OrbitZoomSpeed );

		sheet.AddProperty( () => EditorPreferences.BackfaceSelection );
		sheet.AddProperty( () => EditorPreferences.BoundsPlacement );
		sheet.AddProperty( () => EditorPreferences.PasteAtCursor );

		Layout.Add( sheet );
		Layout.AddStretchCell();
	}
}
