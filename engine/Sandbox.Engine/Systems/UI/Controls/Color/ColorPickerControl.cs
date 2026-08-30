using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

/// <summary>
/// A control for picking a color using sliders and whatever
/// </summary>
[StyleSheet.Inline( "colorpickercontrol", Styles )]
public partial class ColorPickerControl : BaseControl
{
	const string Styles = """
		ColorPickerControl
		{
			flex-direction: column;
			flex-shrink: 0;
			gap: 0.5rem;
			margin: 1rem;
		}
		""";

	readonly ColorSaturationValueControl _svControl;
	readonly ColorHueControl _hueControl;
	readonly ColorAlphaControl _alphaControl;

	public override bool SupportsMultiEdit => true;

	public ColorPickerControl()
	{
		_svControl = AddChild<ColorSaturationValueControl>( "sv" );
		_hueControl = AddChild<ColorHueControl>( "hue" );
		_alphaControl = AddChild<ColorAlphaControl>( "alpha" );
	}

	public override void Rebuild()
	{
		_svControl.Property = Property;
		_hueControl.Property = Property;
		_alphaControl.Property = Property;
	}

	public override void Tick()
	{
		base.Tick();
	}
}
