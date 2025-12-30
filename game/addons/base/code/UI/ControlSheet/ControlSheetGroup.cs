namespace Sandbox.UI;

/// <summary>
/// A group for ControlSheet, consists of a title and a body containing properties.
/// </summary>
public class ControlSheetGroup : Panel
{
	public ControlSheetGroupHeader Header { get; set; }
	public Panel ToggleContainer { get; set; }
	public Panel Body { get; set; }
	public bool Closed { get; internal set; }


	InspectorVisibilityAttribute[] _visibility;

	public ControlSheetGroup()
	{
		AddClass( "controlgroup" );

		Header = AddChild<ControlSheetGroupHeader>( "header" );
		Body = AddChild<Panel>( "body" );
	}

	/// <summary>
	/// Set the control that is going to toggle this group open and closed.
	/// </summary>
	public void SetToggle( SerializedProperty toggleGroup )
	{
		Header.ToggleProperty = toggleGroup;
	}

	public override void Tick()
	{
		base.Tick();

		if ( Header.ToggleProperty != null )
		{
			Body.SetClass( "hidden", Header.ToggleProperty.As.Bool == false );
		}

		if ( _visibility?.Length > 0 )
		{
			SetClass( "hidden", _visibility.All( x => x.TestCondition( Header.ToggleProperty?.Parent ) ) );
		}
	}

	/// <summary>
	/// Hide this group if these attributes say so.
	/// </summary>
	public void SetVisibility( InspectorVisibilityAttribute[] inspectorVisibilityAttributes )
	{
		_visibility = null;

		if ( inspectorVisibilityAttributes?.Length == 0 ) return;

		_visibility = inspectorVisibilityAttributes;
	}
}
