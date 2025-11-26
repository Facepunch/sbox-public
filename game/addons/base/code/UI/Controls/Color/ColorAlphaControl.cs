namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. Displays a text entry that can be edited, and a color swatch which pops up a mixer.
/// </summary>
public partial class ColorAlphaControl : BaseControl
{
	readonly Panel _handle;

	public ColorAlphaControl()
	{
		_handle = AddChild<Panel>( "handle" );
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;
	}

	public override void Tick()
	{
		base.Tick();

		UpdateFromColor();
	}

	void UpdateFromColor()
	{
		var color = Property.GetValue<Color>();
		_handle.Style.Left = Length.Percent( color.a * 100f );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		UpdateFromPosition( e.LocalPosition );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !PseudoClass.HasFlag( PseudoClass.Active ) )
			return;

		UpdateFromPosition( e.LocalPosition );
	}

	private void UpdateFromPosition( Vector2 localPosition )
	{
		// Get the bounds of the control
		var bounds = Box.Rect;
		if ( bounds.Width <= 0 || bounds.Height <= 0 ) return;

		// Clamp position within bounds
		float x = Math.Clamp( localPosition.x, 0, bounds.Width );

		// Calculate saturation and value from position
		var alpha = (x / bounds.Width);

		var color = Property.GetValue<Color>();

		// Create new color with updated saturation and value
		var newColor = color with { a = alpha };

		// Set the property to the new color
		Property.SetValue( newColor );

		UpdateFromColor();
	}
}
