
namespace Sandbox.UI;

public class ControlSheetGroupHeader : Panel
{
	public string Title
	{
		get;
		set
		{
			field = value;
			textLabel.Text = field;
		}
	}

	public string Icon
	{
		get;
		set
		{
			field = value;
			iconLabel.Text = field;
		}
	}

	public SerializedProperty ToggleProperty
	{
		get;
		set
		{
			field = value;

			SetClass( "has-toggle", field != null );
		}
	}

	Label iconLabel;
	Label textLabel;

	public ControlSheetGroupHeader()
	{
		iconLabel = AddChild<Label>( "icon" );
		textLabel = AddChild<Label>( "title" );
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "checked", ToggleProperty?.As.Bool == true );
		SetClass( "hidden", string.IsNullOrWhiteSpace( Title ) && ToggleProperty == null );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		ToggleProperty?.As.Bool = !(ToggleProperty?.As.Bool ?? false);
	}
}
