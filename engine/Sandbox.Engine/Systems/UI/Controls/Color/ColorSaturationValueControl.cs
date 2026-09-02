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
[StyleSheet.Inline( "colorsaturationvaluecontrol", Styles )]
public partial class ColorSaturationValueControl : BaseControl
{
	const string Styles = """
		ColorSaturationValueControl
		{
			width: 240px;
			height: 240px;
			background-color: red;
			position: relative;
			border-radius: 4px;
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
				width: 16px;
				height: 16px;
				border-radius: 100px;
				border: 2px solid #444;
				position: absolute;
				background-color: white;
				box-shadow: 2px 2px 16px #000a;
				transform: translateX( -50% ) translateY( -50% );
				pointer-events: none;
				z-index: 100;
				z-index: 100;
			}

			.gradient
			{
				position: absolute;
				width: 100%;
				height: 100%;
				border-radius: 4px;
				background: linear-gradient( to right, white, rgba( 255, 255, 255, 0 ) );

				&:after
				{
					content: "";
					position: absolute;
					width: 100%;
					height: 100%;
					border-radius: 4px;
					background: linear-gradient( to top, black, rgba( 0, 0, 0, 0 ) );
				}
			}
		}
		""";

	readonly Panel _handle;

	float _hue = 0;

	public override bool SupportsMultiEdit => true;

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
