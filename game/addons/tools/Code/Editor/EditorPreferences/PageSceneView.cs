namespace Editor.Preferences;

internal class PageSceneView : Widget
{
	public PageSceneView( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		// Scene View
		Layout.Add( new Label.Subtitle( "Scene View" ) );
		var sceneSheet = new ControlSheet();
		sceneSheet.AddProperty( () => EditorPreferences.BackfaceSelection );
		sceneSheet.AddProperty( () => EditorPreferences.BoundsPlacement );
		sceneSheet.AddProperty( () => EditorPreferences.CreateObjectsAtOrigin );
		sceneSheet.AddProperty( () => EditorPreferences.PasteAtCursor );
		Layout.Add( sceneSheet );
		Layout.AddSpacingCell( 12 );

		// Camera
		Layout.Add( new Label( "Camera View" ) );
		Layout.AddSpacingCell( 8 );
		var cameraSheet = new ControlSheet();
		cameraSheet.AddProperty( () => EditorPreferences.CameraFieldOfView );
		cameraSheet.AddProperty( () => EditorPreferences.CameraZNear );
		cameraSheet.AddProperty( () => EditorPreferences.CameraZFar );
		Layout.Add( cameraSheet );
		Layout.AddSpacingCell( 12 );

		Layout.Add( new Label( "Camera Navigation" ) );
		Layout.AddSpacingCell( 8 );
		var cameraStyleSettings = new ControlSheet();
		Layout.Add( cameraStyleSettings );
		cameraStyleSettings.AddProperty( () => EditorPreferences.NavigationStyle );
		cameraStyleSettings.AddProperty( () => EditorPreferences.CameraCursor );
		Layout.AddSpacingCell( 8 );

		var cameraOrbitSettings = new ControlSheet();
		Layout.Add( cameraOrbitSettings );
		cameraOrbitSettings.AddProperty( () => EditorPreferences.OrbitSensitivity );
		cameraOrbitSettings.AddProperty( () => EditorPreferences.OrbitInvertHorizontal );
		cameraOrbitSettings.AddProperty( () => EditorPreferences.OrbitInvertVertical );
		Layout.AddSpacingCell( 8 );

		var cameraPanSettings = new ControlSheet();
		Layout.Add( cameraPanSettings );
		cameraPanSettings.AddProperty( () => EditorPreferences.PanSensitivity );
		cameraPanSettings.AddProperty( () => EditorPreferences.PanInvertHorizontal );
		cameraPanSettings.AddProperty( () => EditorPreferences.PanInvertVertical );
		Layout.AddSpacingCell( 8 );

		var cameraZoomSettings = new ControlSheet();
		Layout.Add( cameraZoomSettings );
		cameraZoomSettings.AddProperty( () => EditorPreferences.ZoomSensitivity );
		cameraZoomSettings.AddProperty( () => EditorPreferences.ZoomInvert );
		Layout.AddSpacingCell( 12 );

		Layout.Add( new Label( "Flying Camera" ) );
		Layout.AddSpacingCell( 8 );
		var cameraMovementSheet = new ControlSheet();
		Layout.Add( cameraMovementSheet );
		cameraMovementSheet.AddProperty( () => EditorPreferences.CameraMovementSmoothing );
		cameraMovementSheet.AddProperty( () => EditorPreferences.ScrollZoomSpeed );

		Layout.AddStretchCell();
	}
}
