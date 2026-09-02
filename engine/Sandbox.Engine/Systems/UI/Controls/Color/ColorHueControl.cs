using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

/// <summary>
/// A control for editing Color properties. Displays a text entry that can be edited, and a color swatch which pops up a mixer.
/// </summary>
[StyleSheet.Inline( "colorhuecontrol", Styles )]
public partial class ColorHueControl : BaseControl
{
	const string Styles = """
		ColorHueControl
		{
			gap: 0.5rem;
			flex-grow: 1;
			pointer-events: all;
			background: linear-gradient( to right, red, yellow, lime, cyan, blue, magenta, red );
			border-radius: 4px;
			padding: 2px;
			height: 12px;
			position: relative;
			cursor: pointer;
			margin: 10px 0;
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
