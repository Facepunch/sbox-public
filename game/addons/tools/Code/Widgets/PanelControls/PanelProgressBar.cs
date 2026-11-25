using Sandbox.UI;
using Sandbox.UI.Construct;
using System;

namespace Editor;

/// <summary>
/// Shows progress in an editor panel, with a moving indicator while the total is unknown.
/// </summary>
internal sealed class PanelProgressBar : Panel
{
	readonly Panel fill;

	/// <summary>
	/// Creates an empty progress bar.
	/// </summary>
	public PanelProgressBar()
	{
		AddClass( "progress" );
		Style.FlexShrink = 0;
		fill = Add.Panel( "progress-fill" );
		fill.Style.Height = Length.Percent( 100 );
		Fraction = 0;
	}

	/// <summary>
	/// The completed fraction from zero to one. Null animates the indicator.
	/// </summary>
	internal float? Fraction
	{
		get;
		set
		{
			field = value;
			fill.Style.Width = Length.Percent( value.HasValue ? 100 * Math.Clamp( value.Value, 0, 1 ) : 25 );
			if ( value.HasValue )
				fill.Style.MarginLeft = 0;
		}
	}

	/// <inheritdoc/>
	public override void Tick()
	{
		base.Tick();
		if ( Fraction is null )
			fill.Style.MarginLeft = Length.Percent( 37.5f * (1 + MathF.Sin( (float)RealTime.Now * 3 )) );
	}
}
