namespace Editor;

[CustomEditor( typeof( Vector2 ) )]
[CustomEditor( typeof( Vector3 ) )]
[CustomEditor( typeof( Vector4 ) )]
public class VectorControlWidget : ControlWidget
{
	SerializedObject obj;

	SerializedProperty Property;

	FloatControlWidget FirstControl;

	public override bool SupportsMultiEdit => true;

	public VectorControlWidget( SerializedProperty property ) : base( property )
	{
		Property = property;

		property.TryGetAsObject( out obj );

		if ( obj is null )
		{
			Log.Warning( $"Error when trying to get {property} as object" );
			return;
		}

		Layout = Layout.Row();
		Layout.Spacing = 2;

		FirstControl = TryAddField( "x", Theme.Red, "X" );
		TryAddField( "y", Theme.Green, "Y" );
		TryAddField( "z", Theme.Blue, "Z" );
		TryAddField( "w", Theme.Yellow, "W" );
	}

	private FloatControlWidget TryAddField( string propertyName, Color color, string text )
	{
		var prop = obj.GetProperty( propertyName );
		if ( prop is null ) return null;

		var control = Layout.Add( new FloatControlWidget( prop ) { HighlightColor = color, Label = text } );

		control.MinimumWidth = Theme.RowHeight;
		control.HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		control.MakeRanged( Property );

		return control;
	}

	public override void StartEditing()
	{
		FirstControl?.StartEditing();
	}

	protected override void OnPaint()
	{
		// nothing
	}
}
