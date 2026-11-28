namespace Editor.Inspectors;

/// <summary>
/// Edits general scene properties in the Inspector.
/// </summary>
[Inspector( typeof( Scene ) )]
public class SceneInspector : InspectorWidget
{
	/// <summary>
	/// Builds the property sheet for the inspected scene.
	/// </summary>
	public SceneInspector( SerializedObject so ) : base( so )
	{
		var cs = new ControlSheet();
		cs.Margin = 8;

		cs.AddRow( SerializedObject.GetProperty( nameof( Scene.TimeScale ) ) );
		cs.AddRow( SerializedObject.GetProperty( nameof( Scene.WantsSystemScene ) ) );
		cs.AddRow( SerializedObject.GetProperty( nameof( Scene.PhysicsMode ) ) );

		Layout = Layout.Column();
		Layout.Add( cs );

		Layout.AddStretchCell();
	}
}
