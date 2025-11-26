namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. Displays a text entry that can be edited, and a color swatch which pops up a mixer.
/// </summary>
public partial class ColorSaturationValueControl : BaseControl
{
	readonly Panel _handle;

	float _hue = 0;

	public ColorSaturationValueControl()
	{
		_handle = AddChild<Panel>( "handle" );

		AddChild<Panel>( "gradient" );
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
			_hue = color.ToHsv().Hue;
		}

		_handle.Style.Left = Length.Percent( hsv.Saturation * 100f );
		_handle.Style.Top = Length.Percent( (1 - hsv.Value) * 100f );
		_handle.Style.BackgroundColor = color;

		Style.BackgroundColor = new ColorHsv( _hue, 1f, 1f );
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
		float y = Math.Clamp( localPosition.y, 0, bounds.Height );

		// Calculate saturation and value from position
		float saturation = x / bounds.Width;
		float value = 1f - (y / bounds.Height);

		// Create new color with updated saturation and value
		var newColor = new ColorHsv( _hue, saturation, value ).ToColor();

		// Set the property to the new color
		Property.SetValue( newColor );

		UpdateFromColor();
	}
}
