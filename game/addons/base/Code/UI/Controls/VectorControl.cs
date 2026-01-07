namespace Sandbox.UI;

/// <summary>
/// Like TextEntry, except just for numbers
/// </summary>
[CustomEditor( typeof( Vector2 ) )]
[CustomEditor( typeof( Vector3 ) )]
[CustomEditor( typeof( Vector4 ) )]
public partial class VectorControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	NumberEntry _x;
	NumberEntry _y;
	NumberEntry _z;
	NumberEntry _w;

	public VectorControl()
	{
		_x = AddChild<NumberEntry>( "x" );
		_y = AddChild<NumberEntry>( "y" );
		_z = AddChild<NumberEntry>( "z" );
		_w = AddChild<NumberEntry>( "w" );
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;

		// get the vector3 as a so
		if ( Property.TryGetAsObject( out var so ) )
		{
			_x.Property = so.GetProperty( "x" );
			_y.Property = so.GetProperty( "y" );

			_z.Property = so.GetProperty( "z" );
			_z.Style.Display = _z.Property is not null ? DisplayMode.Flex : DisplayMode.None;

			_w.Property = so.GetProperty( "w" );
			_w.Style.Display = _w.Property is not null ? DisplayMode.Flex : DisplayMode.None;
		}
	}
}
