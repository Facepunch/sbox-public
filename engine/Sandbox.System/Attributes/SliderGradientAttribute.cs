namespace Sandbox;

/// <summary>
/// Draws a numeric slider's track as a color gradient instead of highlighting the filled portion.
/// Use with <see cref="RangeAttribute"/>, for example [SliderGradient( "#0ff", "#0aa", "#ff0" )].
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field )]
public sealed class SliderGradientAttribute : Attribute
{
	/// <summary>
	/// Colors along the track, using the same strings as <see cref="Color.Parse(string)"/>.
	/// </summary>
	public string[] Colors { get; }

	/// <summary>
	/// Optional positions from zero to one, with one position per color. All colors are evenly spaced
	/// when omitted or when the position count differs from the color count. Otherwise, non-finite
	/// positions individually fall back to even spacing. Positions are clamped and sorted;
	/// endpoint colors extend to the track edges.
	/// </summary>
	public float[] Positions { get; set; }

	/// <summary>
	/// Creates a gradient with colors spaced evenly from the minimum to the maximum slider value.
	/// </summary>
	public SliderGradientAttribute( params string[] colors )
	{
		Colors = colors;
	}

	/// <summary>
	/// Builds the track gradient using the placement and fallback rules of <see cref="Positions"/>.
	/// An empty color list leaves the slider's usual track unchanged.
	/// </summary>
	public Gradient CreateGradient()
	{
		var gradient = new Gradient();
		if ( Colors is null || Colors.Length == 0 ) return gradient;

		for ( int i = 0; i < Colors.Length; i++ )
		{
			var position = Colors.Length == 1 ? 0.0f : i / (float)(Colors.Length - 1);

			if ( Positions?.Length == Colors.Length && float.IsFinite( Positions[i] ) )
			{
				position = Positions[i];
			}

			gradient.AddColor( position, (Color)Colors[i] );
		}

		return gradient;
	}
}
