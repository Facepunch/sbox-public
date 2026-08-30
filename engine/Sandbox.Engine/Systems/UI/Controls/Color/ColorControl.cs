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
[CustomEditor( typeof( Color ) )]
[StyleSheet.Inline( "colorcontrol", Styles )]
public partial class ColorControl : BaseControl
{
	const string Styles = """
		ColorControl
		{
			gap: 0.5rem;
			flex-grow: 1;
			pointer-events: all;
			background-color: #000a;
			border-radius: 4px;
			padding: 2px;
			height: 32px;
		}

		ColorControl TextEntry
		{
			flex-grow: 1;
			flex-shrink: 0;
			color: #aaa;
			font-size: 1.2rem;

			&:hover, &:focus
			{
				color: #ddd;

				.icon
				{
					color: #3af;
				}
			}

			&:active
			{
				color: white;
			}
		}

		ColorControl > .colorswatch
		{
			aspect-ratio: 1;
			border-radius: 4px;
			height: 100%;
			flex-shrink: 0;
			cursor: pointer;
			border: 2px solid #000;
		}
		""";

	readonly TextEntry _textEntry;
	readonly Panel _colorSwatch;

	public override bool SupportsMultiEdit => true;

	public ColorControl()
	{
		_colorSwatch = AddChild<Panel>( "colorswatch" );
		_colorSwatch.AddEventListener( "onmousedown", OpenPopup );

		_textEntry = AddChild<TextEntry>( "textentry" );
		_textEntry.OnTextEdited = OnTextEntryChanged;
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;

		_textEntry.Value = Property.GetValue<Color>().Hex;
	}

	public override void Tick()
	{
		base.Tick();

		_colorSwatch.Style.BackgroundColor = Property.GetValue<Color>();
	}

	void OnTextEntryChanged( string value )
	{
		Property.SetValue( value );
	}

	void OpenPopup()
	{
		var popup = new Popup( _colorSwatch, Popup.PositionMode.BelowLeft, 0 );

		var picker = popup.AddChild<ColorPickerControl>();
		picker.Property = Property;
	}
}
