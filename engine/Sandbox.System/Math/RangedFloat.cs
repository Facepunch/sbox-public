using System.ComponentModel;
using Sandbox;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;


/// <summary>
/// A float between two values, which can be randomized or fixed.
/// </summary>
[JsonConverter( typeof( Sandbox.Internal.JsonConvert.RangedFloatConverter ) )]
[StructLayout( LayoutKind.Sequential )]
public partial struct RangedFloat
{
	[Obsolete( $"Use {nameof( RangedFloat )}.{nameof( Min )}" )]
	[EditorBrowsable( EditorBrowsableState.Never )]
	public float x;

	[Obsolete( $"Use {nameof( RangedFloat )}.{nameof( Max )}" )]
	[EditorBrowsable( EditorBrowsableState.Never )]
	public float y;

	/// <summary>
	/// The minimum value of the float range.
	/// </summary>
	public float Min
	{
#pragma warning disable CS0618 // Type or member is obsolete
		get => x;
		set => x = value;
#pragma warning restore CS0618 // Type or member is obsolete
	}

	/// <summary>
	/// The maximum value of the float range. For <see cref="RangeType.Fixed"/>,
	/// this will be the same as <see cref="Min"/>.
	/// </summary>
	public float Max
	{
#pragma warning disable CS0618 // Type or member is obsolete
		get => Range == RangeType.Between ? y : x;
		set
		{
			y = value;
			Range = RangeType.Between;
		}
#pragma warning restore CS0618 // Type or member is obsolete
	}

	/// <summary>
	/// The fixed value. Setting this will convert us to a fixed value
	/// </summary>
	public float FixedValue
	{
#pragma warning disable CS0618 // Type or member is obsolete
		get => Min;
		set
		{
			x = y = value;
			Range = RangeType.Fixed;
		}
#pragma warning restore CS0618 // Type or member is obsolete
	}

	/// <summary>
	/// The range value. Setting this will convert us to a range value
	/// </summary>
	public Vector2 RangeValue
	{
		get => new Vector2( Min, Max );
		set
		{
			Min = value.x;
			Max = value.y;
		}
	}

	/// <summary>
	/// Range type of <see cref="RangedFloat"/>.
	/// </summary>
	public enum RangeType
	{
		/// <summary>
		/// Single value, both minimum and maximum value.
		/// </summary>
		[Icon( "fiber_manual_record" )]
		Fixed,

		/// <summary>
		/// Random value between given minimum and maximum.
		/// </summary>
		[Icon( "join_full" )]
		Between
	}

	/// <summary>
	/// Range type of this float.
	/// </summary>
	public RangeType Range { get; set; }

	/// <summary>
	/// Initialize the float as a fixed value.
	/// </summary>
	public RangedFloat( float fixedValue )
	{
		FixedValue = fixedValue;
		Range = RangeType.Fixed;
	}

	/// <summary>
	/// Initialize the float as a random value between given min and max.
	/// </summary>
	/// <param name="min">The minimum possible value for this float.</param>
	/// <param name="max">The maximum possible value for this float.</param>
	public RangedFloat( float min, float max )
	{
		Min = min;
		Max = max;
		Range = RangeType.Between;
	}

	/// <summary>
	/// Returns the final value of this ranged float, randomizing between min and max values.
	/// </summary>
	public float GetValue()
	{
		return Range == RangeType.Between ? SandboxSystem.Random.Float( Min, Max ) : Min;
	}

	[GeneratedRegex( """^[\[\]\s"]*(?<min>-?\d+(?:\.\d+)?)(?:[\s,;]+(?<max>-?\d+(?:\.\d+)?))?(?:[\s,;]+(?<format>\d+))?[\[\]\s"]*$""" )]
	private static partial Regex Pattern();

	private static float? ParseOptionalFloat( Group group )
	{
		if ( !group.Success ) return default;
		return float.TryParse( group.Value, CultureInfo.InvariantCulture, out var value ) ? value : 0f;
	}

	private static int? ParseOptionalInt( Group group )
	{
		if ( !group.Success ) return null;
		return int.TryParse( group.Value, out var value ) ? value : 0;
	}

	/// <summary>
	/// Parse a ranged float from a string. Format is <c>"min[ max]"</c>.
	/// </summary>
	public static RangedFloat Parse( string str )
	{
		var match = Pattern().Match( str );

		if ( !match.Success )
		{
			return default;
		}

		var min = ParseOptionalFloat( match.Groups["min"] );
		var max = ParseOptionalFloat( match.Groups["max"] );

		// Support legacy format

		if ( ParseOptionalInt( match.Groups["format"] ) is { } format )
		{
			return (RangeType)format switch
			{
				RangeType.Fixed => new RangedFloat( min ?? 0 ),
				RangeType.Between => new RangedFloat( min ?? 0, max ?? min ?? 0 ),
				_ => default
			};
		}

		return max is not null ? new RangedFloat( min ?? 0f, max.Value ) : new RangedFloat( min ?? 0f );
	}

	/// <summary>
	/// Returns a string representation of this range, that can be passed to <see cref="Parse"/> to re-create this range.
	/// Format is <c>"min[ max]"</c>.
	/// </summary>
	public override string ToString()
	{
		return Range switch
		{
			RangeType.Fixed => Min.ToString( "R", CultureInfo.InvariantCulture ),
			RangeType.Between => FormattableString.Invariant( $"{Min:R} {Max:R}" ),
			_ => "0"
		};
	}

	public static implicit operator RangedFloat( float input ) => new( input );

	public static implicit operator RangedFloat( (float Min, float Max) range ) => new( range.Min, range.Max );

	public void Deconstruct( out float min, out float max )
	{
		min = Min;
		max = Max;
	}
}
