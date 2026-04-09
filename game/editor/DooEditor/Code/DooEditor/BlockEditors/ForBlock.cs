namespace Editor.DooEditor;

/// <summary>
/// Main window for editing Doo scripts.
/// </summary>
[Inspector( typeof( Doo.ForBlock ) )]
public class ForBlock : InspectorWidget
{
	SerializedObject Target { get; }

	SerializedProperty _nameProperty;
	SerializedProperty _startProperty;
	SerializedProperty _endProperty;
	SerializedProperty _jumpProperty;

	public ForBlock( SerializedObject obj ) : base( obj )
	{
		Target = obj;
		Layout = Layout.Column();
		Layout.Spacing = 4;

		_nameProperty = Target.GetProperty( nameof( Doo.ForBlock.VariableName ) );
		_startProperty = Target.GetProperty( nameof( Doo.ForBlock.StartValue ) );
		_endProperty = Target.GetProperty( nameof( Doo.ForBlock.EndValue ) );
		_jumpProperty = Target.GetProperty( nameof( Doo.ForBlock.JumpValue ) );

		BuildUI();
	}

	void BuildUI()
	{
		Layout.Clear( true );

		Layout.Add( new InformationBox( "Will run everything inside this block multiple times, depending on the evaluation." ) );

		var cs = new ControlSheet();
		cs.AddControl<DooVariableControlWidget>( _nameProperty );
		cs.AddRow( _startProperty );
		cs.AddRow( _endProperty );
		cs.AddRow( _jumpProperty );

		Layout.Add( cs );
	}
}
