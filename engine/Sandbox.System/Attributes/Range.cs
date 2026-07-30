
namespace Sandbox
{
	/// <summary>
	/// Mark this property as a ranged float/int. In inspector we'll be able to create a slider
	/// instead of a text entry.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field )]
	public class RangeAttribute : System.Attribute
	{
		/// <summary>
		/// The minimum value of the range.
		/// </summary>
		public float Min { get; }

		/// <summary>
		/// The maximum value of the range.
		/// </summary>
		public float Max { get; }

		/// <summary>
		/// Whether or not a slider should be shown for this range.
		/// </summary>
		public bool Slider { get; } = true;

		/// <summary>
		/// Whether or not the value should be clamped to the range.
		/// If false, the user can manually enter values outside the range if they wish.
		/// </summary>
		public bool Clamped { get; } = false;

		[Obsolete( "Use [Step] attribute instead" )]
		public float Step { get; } = 0f;

		public RangeAttribute( float min, float max )
		{
			Min = min;
			Max = max;
		}

		public RangeAttribute( float min, float max, bool clamped = true, bool slider = true )
		{
			Min = min;
			Max = max;
			Clamped = clamped;
			Slider = slider;
		}

		[Obsolete( "Use [Step] attribute for setting step value." )]
		public RangeAttribute( float min, float max, float step, bool clamped = true, bool slider = true )
		{
			Min = min;
			Max = max;
			Step = step;
			Clamped = clamped;
			Slider = slider;
		}
	}

	/// <summary>
	/// Mark this property as a stepped value, where the value can only be set to multiples of the step value.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field )]
	public class StepAttribute : System.Attribute
	{
		public float Step { get; }
		public StepAttribute( float step )
		{
			Step = step;
		}
	}
}

/// <summary>
/// For use with Curves, allows you to define a custom range for the time
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public class TimeRangeAttribute : System.Attribute
{
	public float Min { get; }
	public float Max { get; }
	public bool CanModify { get; }
	public TimeRangeAttribute( float start, float end, bool canModify = true )
	{
		Min = start;
		Max = end;
		CanModify = canModify;
	}
}

/// <summary>
/// For use with Curves, allows you to define a custom range for the value
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public class ValueRangeAttribute : System.Attribute
{
	public float Min { get; }
	public float Max { get; }
	public bool CanModify { get; }
	public ValueRangeAttribute( float start, float end, bool canModify = true )
	{
		Min = start;
		Max = end;
		CanModify = canModify;
	}
}
