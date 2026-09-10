using System.Globalization;

namespace Sandbox.UI
{
	/// <summary>
	/// A variable unit based length. ie, could be a percentage or a pixel length. This is commonly used to express the size of things in UI space, usually coming from style sheets.
	/// </summary>
	public struct Length : IEquatable<Length>
	{
		/// <summary>
		/// The meaning of the value is dependent on <see cref="Unit"/>.
		/// </summary>
		public float Value;

		/// <summary>
		/// How to determine the final length. Commonly used with Pixel or Percentage.
		/// </summary>
		public LengthUnit Unit;

		/// <summary>
		/// The current root panel size. This is required for vh, vw, vmin and vmax. This is set during PreLayout, Layout and PostLayout.
		/// </summary>
		internal static Vector2 RootSize;

		/// <summary>
		/// The current root panel font size. This is required for rem. This is set during PreLayout, Layout and PostLayout.
		/// </summary>
		internal static Length RootFontSize;

		/// <summary>
		/// The current panel font size. This is required for em. This is set during PreLayout.
		/// </summary>
		internal static Length CurrentFontSize;

		/// <summary>
		/// The current root scale factor. This is required for dpi scaling. This is set during PreLayout, Layout and PostLayout.
		/// </summary>
		internal static float RootScale = 1.0f;

		/// <summary>
		/// If the length unit is Expression, this will represent the calc() expression parsed
		/// </summary>
		private string _expression;

		/// <summary>
		/// Convert to a pixel value. Use the dimension to work out percentage values.
		/// </summary>
		public readonly float GetPixels( float dimension )
		{
			if ( Unit == LengthUnit.Percentage )
				return dimension * (Value / 100.0f);

			if ( Unit == LengthUnit.ViewWidth )
				return RootSize.x * (Value / 100.0f);

			if ( Unit == LengthUnit.ViewHeight )
				return RootSize.y * (Value / 100.0f);

			if ( Unit == LengthUnit.ViewMin )
				return Math.Min( RootSize.x, RootSize.y ) * (Value / 100.0f);

			if ( Unit == LengthUnit.ViewMax )
				return Math.Max( RootSize.x, RootSize.y ) * (Value / 100.0f);

			if ( Unit == LengthUnit.Expression )
				return UI.Calc.Evaluate( _expression, dimension );

			if ( Unit == LengthUnit.RootEm )
				return RootFontSize.Value * Value;

			if ( Unit == LengthUnit.Em )
				return CurrentFontSize.Value * Value;

			return Value;
		}

		/// <summary>
		/// Used in situations where the scale couldn't be applied during style computation
		/// </summary>
		internal readonly float GetScaledPixels( float dimension )
		{
			if ( Unit == LengthUnit.Pixels )
				return Value * RootScale;

			if ( Unit == LengthUnit.RootEm )
				return GetPixels( dimension ) * RootScale;

			return GetPixels( dimension );
		}

		/// <summary>
		/// Get the pixel size but also evaluate content size to support use Start, End, Center
		/// </summary>
		public readonly float GetPixels( float dimension, float contentSize )
		{
			if ( Unit == LengthUnit.Start ) return 0.0f;
			if ( Unit == LengthUnit.End ) return dimension - contentSize;
			if ( Unit == LengthUnit.Center ) return (dimension - contentSize) * 0.5f;

			return GetPixels( dimension );
		}

		public static implicit operator Length( float value )
		{
			return new Length { Value = value, Unit = LengthUnit.Pixels };
		}

		public readonly override bool Equals( object obj ) => base.Equals( obj );
		public readonly override int GetHashCode() => HashCode.Combine( Value, Unit );

		/// <summary>
		/// Lerp from one length to another.
		/// </summary>
		/// <param name="a">Length at delta 0</param>
		/// <param name="b">Length at delta 1</param>
		/// <param name="delta">The interpolation stage</param>
		/// <param name="dimension">The width or height of the parent to use when working out percentage lengths</param>
		/// <returns>The interpolated Length</returns>
		internal static Length? Lerp( Length a, Length b, float delta, float dimension )
		{
			var x = a.GetPixels( dimension );
			var y = b.GetPixels( dimension );

			var diff = (y - x);

			return x + diff * delta;
		}

		/// <summary>
		/// Lerp from one length to another.
		/// </summary>
		/// <param name="a">Length at delta 0</param>
		/// <param name="b">Length at delta 1</param>
		/// <param name="delta">The interpolation stage</param>
		/// <param name="dimension">The width or height of the parent to use when working out percentage lengths</param>
		/// <param name="contentSize">Evaluate content size to support use Start, End, Center</param>
		/// <returns>The interpolated Length</returns>
		internal static Length? Lerp( Length a, Length b, float delta, float dimension, float contentSize )
		{
			var x = a.GetPixels( dimension, contentSize );
			var y = b.GetPixels( dimension, contentSize );

			var diff = (y - x);

			return x + diff * delta;
		}

		/// <summary>
		/// Lerp from one length to another.
		/// </summary>
		/// <param name="a">Length at delta 0</param>
		/// <param name="b">Length at delta 1</param>
		/// <param name="delta">The interpolation stage</param>
		/// <returns>The interpolated Length</returns>
		internal static Length? Lerp( Length a, Length b, float delta )
		{
			if ( a.Unit != b.Unit )
				return b;

			return new Length { Unit = a.Unit, Value = a.Value.LerpTo( b.Value, delta, false ) };
		}

		/// <summary>
		/// Create a length in pixels
		/// </summary>
		/// <param name="pixels">The amount of pixels for this length</param>
		/// <returns>A new length</returns>
		public static Length? Pixels( float pixels ) => new Length { Value = pixels, Unit = LengthUnit.Pixels };

		/// <summary>
		/// Create a length in percents
		/// </summary>
		/// <param name="percent">The amount of percent for this (0-100)</param>
		/// <returns>A new length</returns>
		public static Length? Percent( float percent ) => new Length { Value = percent, Unit = LengthUnit.Percentage };

		/// <summary>
		/// Create a length based on the view height
		/// </summary>
		/// <param name="percentage">The amount of percent for this (0-100)</param>
		/// <returns>A new length</returns>
		public static Length? ViewHeight( float percentage ) => new Length { Value = percentage, Unit = LengthUnit.ViewHeight };

		/// <summary>
		/// Create a length based on the view width
		/// </summary>
		/// <param name="percentage">The amount of percent for this (0-100)</param>
		/// <returns>A new length</returns>
		public static Length? ViewWidth( float percentage ) => new Length { Value = percentage, Unit = LengthUnit.ViewWidth };

		/// <summary>
		/// Create a length based on the longest edge of the screen size
		/// </summary>
		/// <param name="percentage">The amount of percent for this (0-100)</param>
		/// <returns>A new length</returns>
		public static Length? ViewMax( float percentage ) => new Length { Value = percentage, Unit = LengthUnit.ViewMax };

		/// <summary>
		/// Create a length based on the shortest edge of the screen size
		/// </summary>
		/// <param name="percentage">The amount of percent for this (0-100)</param>
		/// <returns>A new length</returns>
		public static Length? ViewMin( float percentage ) => new Length { Value = percentage, Unit = LengthUnit.ViewMin };

		/// <summary>
		/// Create a length in percents
		/// </summary>
		/// <param name="fraction">The fraction of a percent (0 = 0%, 1 = 100%)</param>
		/// <returns>A new length</returns>
		public static Length? Fraction( float fraction ) => Percent( fraction * 100 );

		/// <summary>
		/// Create a length based on a css calc expression
		/// </summary>
		public static Length? Calc( string expression ) => new Length { Unit = LengthUnit.Expression, _expression = expression };

		/// <summary>
		/// Create a length based on the font size of the root element.
		/// </summary>
		/// <param name="value">Value in rem</param>
		/// <returns>A new length</returns>
		public static Length Rem( float value ) => new Length { Value = value, Unit = LengthUnit.RootEm };

		/// <summary>
		/// Create a length based on the font size of the current element.
		/// </summary>
		/// <param name="value">Value in em</param>
		/// <returns>A new length</returns>
		public static Length Em( float value ) => new Length { Value = value, Unit = LengthUnit.Em };

		/// <summary>
		/// Quickly create a Length with Unit set to LengthUnit.Auto
		/// </summary>
		public static Length Auto => new Length { Unit = LengthUnit.Auto };

		/// <summary>
		/// Quickly create a Length with Unit set to LengthUnit.Contain
		/// </summary>
		public static Length Contain => new Length { Unit = LengthUnit.Contain };

		/// <summary>
		/// Quickly create a Length with Unit set to LengthUnit.Cover
		/// </summary>
		public static Length Cover => new Length { Unit = LengthUnit.Cover };

		public static Length Undefined => new Length { Unit = LengthUnit.Undefined };

		/// <summary>
		/// Parse a length. This is used by the stylesheet parsing system.
		/// </summary>
		/// <param name="value">A length represented by a string</param>
		/// <example>Length.Parse( "100px" )</example>
		/// <example>Length.Parse( "56%" )</example>
		/// <returns></returns>
		public static Length? Parse( string value )
		{
			if ( string.IsNullOrWhiteSpace( value ) ) return null;
			value = value.Trim();

			if ( value == "center" ) return new Length { Unit = LengthUnit.Center };
			if ( value == "left" || value == "top" ) return new Length { Unit = LengthUnit.Start };
			if ( value == "right" || value == "bottom" ) return new Length { Unit = LengthUnit.End };
			if ( value == "cover" ) return Cover;
			if ( value == "contain" ) return Contain;
			if ( value == "auto" ) return Auto;

			// Store this as an expression, defers the actual calculation until we use it (these need the
			// reference size to resolve percentages).
			if ( value.StartsWith( "calc(" ) || value.StartsWith( "min(" ) || value.StartsWith( "max(" ) || value.StartsWith( "clamp(" ) )
				return Calc( value );

			// For keyframes
			if ( value == "from" ) return Length.Percent( 0 );
			if ( value == "to" ) return Length.Percent( 100 );

			// Split into a numeric prefix and a unit suffix, then parse without ever throwing - a bad
			// value returns null so it can't take the rest of the rule down with it.
			int num = 0;
			while ( num < value.Length && (char.IsDigit( value[num] ) || value[num] == '.' || value[num] == '-' || value[num] == '+') )
				num++;

			if ( !float.TryParse( value.Substring( 0, num ), NumberStyles.Float, CultureInfo.InvariantCulture, out float fnum ) )
			{
				// Special float literals like infinity / -infinity / nan (used by calc) have no usable
				// digit prefix - fall back to a whole-string float parse for those.
				if ( float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out float whole ) )
					return Length.Pixels( whole );

				return null;
			}

			// Unit is the leading run of letters (or %) after the number; trailing junk (eg "!important") is ignored.
			var rest = value.Substring( num ).TrimStart();
			int u = 0;
			while ( u < rest.Length && (char.IsLetter( rest[u] ) || rest[u] == '%') )
				u++;
			var unit = rest.Substring( 0, u ).ToLowerInvariant();

			switch ( unit )
			{
				case "":
				case "px":
					return Length.Pixels( fnum );
				case "%":
					return Length.Percent( fnum );
				case "deg":
					// We have no angle unit - preserve the legacy behaviour of treating deg as pixels.
					return Length.Pixels( fnum );
				case "vh":
					return Length.ViewHeight( fnum );
				case "vw":
					return Length.ViewWidth( fnum );
				case "vmin":
					return Length.ViewMin( fnum );
				case "vmax":
					return Length.ViewMax( fnum );
				case "rem":
					return Length.Rem( fnum );
				case "em":
					return Length.Em( fnum );

				// Dynamic / small / large viewport units. We don't track a dynamic viewport, so treat
				// them as the equivalent static vh/vw.
				case "dvh":
				case "svh":
				case "lvh":
					return Length.ViewHeight( fnum );
				case "dvw":
				case "svw":
				case "lvw":
					return Length.ViewWidth( fnum );
			}

			return null;
		}

		public readonly bool Equals( Length other )
		{
			return (Value, Unit) == (other.Value, other.Unit);
		}

		public static bool operator ==( Length lhs, Length rhs )
		{
			return lhs.Equals( rhs );
		}

		public static bool operator !=( Length lhs, Length rhs )
		{
			return !lhs.Equals( rhs );
		}
		public static bool operator ==( Length? lhs, Length? rhs )
		{
			if ( !lhs.HasValue && !rhs.HasValue ) return true;
			if ( !lhs.HasValue ) return false;
			if ( !rhs.HasValue ) return false;

			return lhs.Value.Equals( rhs.Value );
		}

		public static bool operator !=( Length? lhs, Length? rhs )
		{
			if ( !lhs.HasValue && !rhs.HasValue ) return false;
			if ( !lhs.HasValue ) return true;
			if ( !rhs.HasValue ) return true;
			return !lhs.Value.Equals( rhs.Value );
		}

		/// <summary>
		/// If it's a %, will return 0-1. If not it'll return its value.
		/// </summary>
		internal readonly float GetFraction( float f = 1.0f )
		{
			return GetPixels( 1.0f ) * f;
		}

		public readonly override string ToString()
		{
			if ( Unit == LengthUnit.Expression ) return $"{_expression}";
			if ( Unit == LengthUnit.Pixels ) return $"{Value}px";
			if ( Unit == LengthUnit.Percentage ) return $"{Value}%";
			if ( Unit == LengthUnit.RootEm ) return $"{Value}rem";
			if ( Unit == LengthUnit.Em ) return $"{Value}em";

			return $"{Unit}";
		}

		internal static void Scale( ref Length? scale, float amount, bool skipRounding = false )
		{
			if ( scale == null ) return;

			var s = scale.Value;
			Scale( ref s, amount, skipRounding );
			scale = s;
		}

		internal static void Scale( ref Length scale, float amount, bool skipRounding = false )
		{
			if ( scale == null ) return;

			if ( scale.Unit == LengthUnit.Pixels )
			{
				if ( !skipRounding )
				{
					scale = Pixels( System.MathF.Ceiling( scale.Value * amount ) ).Value;
				}
				else
				{
					scale = Pixels( scale.Value * amount ).Value;
				}
			}

			if ( scale.Unit == LengthUnit.RootEm || scale.Unit == LengthUnit.Em )
			{
				scale = scale with { Value = scale.Value * amount };
			}

		}
	}

	/// <summary>
	/// Possible units for various CSS properties that require length, used by <see cref="Length"/> struct.
	/// </summary>
	public enum LengthUnit : byte
	{
		/// <summary>
		/// The layout engine will calculate and select a width for the specified element.
		/// </summary>
		Auto = 0,

		/// <summary>
		/// The length is in pixels.
		/// </summary>
		Pixels,

		/// <summary>
		/// The length is a percentage (0-100) of the parent's length. (typically)
		/// </summary>
		Percentage,

		/// <summary>
		/// The length is a percentage (0-100) of the viewport's height.
		/// </summary>
		ViewHeight,

		/// <summary>
		/// The length is a percentage (0-100) of the viewport's width.
		/// </summary>
		ViewWidth,

		/// <summary>
		/// The length is a percentage (0-100) of the viewport's smallest side/edge.
		/// </summary>
		ViewMin,

		/// <summary>
		/// The length is a percentage (0-100) of the viewport's largest side/edge.
		/// </summary>
		ViewMax,

		/// <summary>
		/// Start of the parent at the appropriate axis.
		/// </summary>
		Start,

		/// <summary>
		/// For background images, cover the entire element with the image, stretcing and cropping as necessary.
		/// </summary>
		Cover,

		/// <summary>
		/// For background images, contain the image within the element bounds.
		/// </summary>
		Contain,

		/// <summary>
		/// End of the parent at the appropriate axis.
		/// </summary>
		End,

		/// <summary>
		/// In the middle of the parent at the appropriate axis.
		/// </summary>
		Center,

		/// <summary>
		/// Similar to CSS 'unset', basically means we don't have a value; should only really be used under certain
		/// circumstances (e.g. to handle background sizing properly).
		/// </summary>
		Undefined,

		/// <summary>
		/// Represents a calc( ... ) expression
		/// </summary>
		Expression,

		/// <summary>
		/// Font size of the root element.
		/// </summary>
		RootEm,

		/// <summary>
		/// Font size of the current element.
		/// </summary>
		Em
	}

	public static class LengthUnitExtension
	{
		/// <summary>
		/// Determine whether this unit type is dynamic (ie. should be updated regularly) or whether it's constant
		/// </summary>
		public static bool IsDynamic( this LengthUnit unit )
		{
			return unit == LengthUnit.ViewWidth || unit == LengthUnit.ViewHeight /* vw/vh */
				|| unit == LengthUnit.ViewMin || unit == LengthUnit.ViewMax /* vmin/vmax */
				|| unit == LengthUnit.Expression /* calc( ... ) */
				|| unit == LengthUnit.RootEm || unit == LengthUnit.Em; /* em/rem */
		}
	}
}
