namespace Editor.DooEditor;

/// <summary>
/// Main window for editing Doo scripts.
/// </summary>
[Inspector( typeof( Doo.DelayBlock ) )]
public class DelayBlock : InspectorWidget
{
	SerializedObject Target { get; }

	public DelayBlock( SerializedObject obj ) : base( obj )
	{
		Target = obj;
		Layout = Layout.Column();

		BuildUI();
	}

	void BuildUI()
	{
		var prop = Target.GetProperty( nameof( Doo.DelayBlock.Seconds ) ).GetCustomizable();
		prop.SetDisplayName( "Delay Seconds" );
		prop.AddAttribute( new TypeHintAttribute( typeof( float ) ) );

		var cs = new ControlSheet();
		cs.AddRow( prop );

		Layout.Add( cs );
	}
}
