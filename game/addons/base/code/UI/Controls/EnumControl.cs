namespace Sandbox.UI;

/// <summary>
/// A control for editing enum properties. Can either display a dropdown or a button group depending on the number of options.
/// </summary>
[CustomEditor( typeof( Enum ) )]
public partial class EnumControl : BaseControl
{
	public EnumControl()
	{

	}

	public override void Rebuild()
	{
		if ( Property == null ) return;
		if ( !Property.PropertyType.IsEnum ) return;

		var options = TypeLibrary.GetEnumDescription( Property.PropertyType );
		if ( options == null )
		{
			Log.Warning( $"Couldn't get enum description for {Property.PropertyType}" );
			return;
		}

		bool useButtonGroup = options.Count() <= 4;

		// TODO - add ButtonGroupAttribute to override this?

		if ( useButtonGroup )
		{
			CreateButtonGroup( options );
		}
		else
		{
			CreateDropdown( options );
		}
	}

	void CreateDropdown( EnumDescription options )
	{
		var dd = AddChild( new DropDown() );

		foreach ( var o in options )
		{
			dd.Options.Add( new Option( o.Title, o.Icon, o.ObjectValue ) );
		}

		dd.ValueChanged = ( val ) => Property.SetValue( val );
	}

	void CreateButtonGroup( EnumDescription options )
	{
		var group = AddChild( new ButtonGroup() );
		group.Value = Property.GetValue<object>();

		foreach ( var o in options )
		{
			var button = group.AddChild( new Button() );
			button.Text = o.Title;
			button.Icon = o.Icon;
			button.Value = o.ObjectValue;
		}

		group.ValueChanged = ( val ) => Property.SetValue( group.Value );
	}
}
