namespace Editor;

[CustomEditor( typeof( ParticleVector3 ) )]
public class ParticleVector3ControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	SerializedObject Target;

	public ParticleVector3ControlWidget( SerializedProperty property ) : base( property )
	{
		SetSizeMode( SizeMode.Ignore, SizeMode.Default );

		if ( !property.TryGetAsObject( out Target ) )
			return;

		Layout = Layout.Row();
		Layout.Spacing = 3;

		var x = Layout.Add( new ParticleFloatControlWidget( Target.GetProperty( "X" ), "X", Theme.Red ) );
		var y = Layout.Add( new ParticleFloatControlWidget( Target.GetProperty( "Y" ), "Y", Theme.Green ) );
		var z = Layout.Add( new ParticleFloatControlWidget( Target.GetProperty( "Z" ), "Z", Theme.Blue ) );

		Layout.AddStretchCell();
	}

	protected override void OnPaint()
	{

	}

}
