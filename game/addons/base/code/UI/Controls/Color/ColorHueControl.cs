namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. Displays a text entry that can be edited, and a color swatch which pops up a mixer.
/// </summary>
public partial class ColorHueControl : BaseControl
{
	readonly Panel _handle;

	float _hue = 0;

	public ColorHueControl()
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
		var hsv = color.ToHsv();

		if ( hsv.Saturation > 0.05f && hsv.Value > 0.05f )
		{
			_hue = hsv.Hue;
		}

		_handle.Style.Left = Length.Percent( (_hue / 360.0f) * 100f );
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
		_hue = (x / bounds.Width) * 360.0f;
		_hue = _hue.Clamp( 0, 360.0f - 0.001f );

		var color = Property.GetValue<Color>().ToHsv();

		// Create new color with updated saturation and value
		var newColor = color with { Hue = _hue };

		// Set the property to the new color
		Property.SetValue( newColor.ToColor() );

		UpdateFromColor();
	}

}
