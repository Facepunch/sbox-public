namespace Editor.Inspectors;

[Inspector( typeof( Scene ) )]
public class SceneInspector : InspectorWidget
{
	public SceneInspector( SerializedObject so ) : base( so )
	{
		var cs = new ControlSheet();
		cs.Margin = 8;

		cs.AddRow( SerializedObject.GetProperty( nameof( Scene.TimeScale ) ) );
		cs.AddRow( SerializedObject.GetProperty( nameof( Scene.WantsSystemScene ) ) );

		Layout = Layout.Column();
		Layout.Add( cs );
		Layout.AddStretchCell();
	}
}
