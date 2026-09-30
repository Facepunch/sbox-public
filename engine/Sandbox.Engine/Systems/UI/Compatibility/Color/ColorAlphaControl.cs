using System.ComponentModel;

namespace Sandbox.UI;

/// <summary>
/// Legacy control for editing the alpha of a Color property. Use ColorPickerControl for new UI.
/// </summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[Obsolete( "Use ColorPickerControl instead." )]
[StyleSheet.Inline( "coloralphacontrol", Styles )]
public partial class ColorAlphaControl : BaseControl
{
	const string Styles = """
		ColorAlphaControl
		{
			gap: 0.5rem;
			flex-grow: 1;
			pointer-events: all;
			background: linear-gradient( to right, black, white );
			border-radius: 4px;
			padding: 2px;
			height: 12px;
			position: relative;
			cursor: pointer;
			border: 1px solid #333;

			&:hover
			{
				border: 1px solid #08f;
			}

			&:active
			{
				border: 1px solid #fff;
			}

			.handle
			{
				top: -5px;
				bottom: -5px;
				aspect-ratio: 1;
				border-radius: 100px;
				border: 2px solid #444;
				position: absolute;
				background-color: white;
				box-shadow: 2px 2px 16px #000a;
				transform: translateX( -50% );
				pointer-events: none;
			}
		}
		""";

	readonly Panel _handle;

	public override bool SupportsMultiEdit => true;

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
		if ( Property is null ) return;

		var color = Property.GetValue<Color>();
		_handle.Style.Left = Length.Percent( color.a * 100f );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		UpdateFromPosition( e.LocalPosition );

		// This press belongs to us - without this a scrolling parent drags the page around
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !PseudoClass.HasFlag( PseudoClass.Active ) )
			return;

		UpdateFromPosition( e.LocalPosition );

		e.StopPropagation();
	}

	private void UpdateFromPosition( Vector2 localPosition )
	{
		if ( Property is null ) return;

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
