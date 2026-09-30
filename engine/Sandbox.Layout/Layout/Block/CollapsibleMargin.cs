namespace Sandbox.Layout;

/// <summary>
/// A set of adjoining vertical margins being collapsed together (CSS 2.1 §8.3.1): the largest positive
/// and the most negative margin. The collapsed result is their sum.
/// </summary>
internal struct CollapsibleMargin
{
	public float Positive;
	public float Negative;

	public static CollapsibleMargin FromMargin( float margin )
	{
		return margin >= 0 ? new CollapsibleMargin { Positive = margin } : new CollapsibleMargin { Negative = margin };
	}

	public readonly CollapsibleMargin CollapseWith( float margin )
	{
		CollapsibleMargin result = this;
		if ( margin >= 0 )
		{
			result.Positive = MathF.Max( result.Positive, margin );
		}
		else
		{
			result.Negative = MathF.Min( result.Negative, margin );
		}

		return result;
	}

	public readonly CollapsibleMargin CollapseWith( CollapsibleMargin other )
	{
		return new CollapsibleMargin
		{
			Positive = MathF.Max( Positive, other.Positive ),
			Negative = MathF.Min( Negative, other.Negative )
		};
	}

	public readonly float Resolve() => Positive + Negative;
}
