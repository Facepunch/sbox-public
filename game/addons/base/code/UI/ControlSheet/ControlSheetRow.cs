namespace Sandbox.UI;

/// <summary>
/// Used by ControlSheet to display a single row of a property. This is created from a SerializedProperty
/// and contains a label and a control for editing the property. Controls are created using BaseControl.CreateFor.
/// </summary>
public class ControlSheetRow : Panel
{
	[Parameter]
	public SerializedProperty Property { get; set; }

	Panel _left;
	Label _title;
	Panel _right;

	public ControlSheetRow()
	{
		_left = AddChild<Panel>( "left" );
		_title = _left.AddChild<Label>( "title" );

		_right = AddChild<Panel>( "right" );
	}

	internal void Initialize( SerializedProperty prop )
	{
		Property = prop;
	}

	protected override void OnParametersSet()
	{
		if ( Property is null )
			return;

		_title.Text = Property?.DisplayName;

		_right.DeleteChildren();

		var c = BaseControl.CreateFor( Property );
		if ( c is null ) return;
		_right.AddChild( c );
	}
}
