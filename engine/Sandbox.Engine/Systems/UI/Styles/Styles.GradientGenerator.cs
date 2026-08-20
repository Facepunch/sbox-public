using System;
using System.Collections.Generic;

namespace Sandbox.UI
{
	public partial class Styles
	{
		public struct GradientColorOffset
		{
			public Color color;
			public float? offset;

			/// <summary>Offset is a pixel length along the gradient line, not a fraction of it.</summary>
			public bool offsetIsPixels;

			public override int GetHashCode()
			{
				return HashCode.Combine( color, offset, offsetIsPixels );
			}
		}
		public struct GradientGenerator
		{
			public GradientColorOffset from;
			public GradientColorOffset to;
		}

		/// <summary>
		/// Parse a linear-gradient's inner token into stops for shader evaluation.
		/// False when it can't be represented (parse failure, or more stops than the
		/// shader supports) - the caller falls back to baking a texture.
		/// </summary>
		private bool TryParseLinearGradientInfo( string token, out GradientInfo info )
		{
			info = default;

			var p = new Parse( token );

			var restoreP = p;
			float angle = 0; // radians, like TryParseAngle - 0 is "to bottom" in our convention
			var corner = GradientInfo.Corners.None;
			var angleStr = p.ReadSentence();

			if ( TryParseCorner( angleStr, out corner ) || TryParseAngle( angleStr, out angle ) )
			{
				p.Pointer++; // comma
			}
			else
			{
				angle = 0;
				p = restoreP;
			}

			if ( !TryParseStops( p.ReadRemaining(), out var stops ) )
				return false;

			info = new GradientInfo
			{
				Angle = angle,
				Corner = corner,
				GradientType = GradientInfo.GradientTypes.Linear,
				ColorOffsets = stops,
			};

			return true;
		}

		/// <summary>
		/// "to" and two side keywords - a corner. The web angles the gradient line so it runs corner
		/// to corner, which needs the box's aspect, so only the corner itself is parsed here.
		/// </summary>
		private static bool TryParseCorner( string text, out GradientInfo.Corners corner )
		{
			corner = GradientInfo.Corners.None;

			if ( string.IsNullOrWhiteSpace( text ) )
				return false;

			var p = new Parse( text );
			p = p.SkipWhitespaceAndNewlines();

			if ( !p.Is( "to ", 0, true ) )
				return false;

			p.Pointer += 3;

			bool top = false, bottom = false, left = false, right = false;

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				var word = p.ReadWord( null, true );
				if ( string.IsNullOrEmpty( word ) )
					break;

				switch ( word.ToLowerInvariant() )
				{
					case "top": top = true; break;
					case "bottom": bottom = true; break;
					case "left": left = true; break;
					case "right": right = true; break;
					default: return false;
				}
			}

			if ( top && left ) corner = GradientInfo.Corners.TopLeft;
			else if ( top && right ) corner = GradientInfo.Corners.TopRight;
			else if ( bottom && left ) corner = GradientInfo.Corners.BottomLeft;
			else if ( bottom && right ) corner = GradientInfo.Corners.BottomRight;

			return corner != GradientInfo.Corners.None;
		}

		/// <summary>
		/// The colour stops of any gradient, ready for the shader. False when nothing parsed
		/// at all. Anything past the shader's stop limit is dropped.
		/// </summary>
		private bool TryParseStops( string token, out System.Collections.Immutable.ImmutableArray<GradientColorOffset> result )
		{
			result = default;

			var segments = ParseGradient( token );
			if ( segments.Count == 0 )
				return false;

			// Segments share their endpoints, so the stops are the first segment's
			// start followed by every segment's end.
			var count = Math.Min( segments.Count + 1, GradientInfo.MaxStops );

			var stops = System.Collections.Immutable.ImmutableArray.CreateBuilder<GradientColorOffset>( count );
			stops.Add( segments[0].from );

			foreach ( var segment in segments )
			{
				if ( stops.Count >= count )
					break;

				stops.Add( segment.to );
			}

			result = stops.ToImmutable();
			return true;
		}

		/// <summary>
		/// Parse a radial-gradient's inner token into stops for shader evaluation.
		/// False when it can't be represented - the caller falls back to baking a texture.
		/// </summary>
		private bool TryParseRadialGradientInfo( string token, out GradientInfo info )
		{
			info = default;

			var p = new Parse( token );

			// CSS defaults: an ellipse reaching the farthest corner, centred.
			var sizeMode = GradientInfo.RadialSizeMode.FarthestCorner;
			var circle = false;
			var center = new Length[] { Length.Percent( 50 ).Value, Length.Percent( 50 ).Value };

			// "[ <shape> || <size> ] [ at <position> ]" sits before the first comma, if at all.
			var restore = p;
			var prelude = p.ReadSentence();

			if ( TryParseRadialPrelude( prelude, ref sizeMode, ref circle, center ) )
			{
				p.Pointer++; // comma
			}
			else
			{
				p = restore;
			}

			if ( !TryParseStops( p.ReadRemaining(), out var stops ) )
				return false;

			info = new GradientInfo
			{
				GradientType = GradientInfo.GradientTypes.Radial,
				SizeMode = sizeMode,
				Circle = circle,
				OffsetX = center[0],
				OffsetY = center[1],
				ColorOffsets = stops,
			};

			return true;
		}

		/// <summary>
		/// Parse a conic-gradient's inner token into stops for shader evaluation.
		/// False when it can't be represented - the caller falls back to baking a texture.
		/// </summary>
		private bool TryParseConicGradientInfo( string token, out GradientInfo info )
		{
			info = default;

			var p = new Parse( token );

			var angle = 0f;
			var center = new Length[] { Length.Percent( 50 ).Value, Length.Percent( 50 ).Value };

			// "[ from <angle> ] [ at <position> ]" sits before the first comma, if at all.
			var restore = p;
			var prelude = p.ReadSentence();

			if ( TryParseConicPrelude( prelude, ref angle, center ) )
			{
				p.Pointer++; // comma
			}
			else
			{
				p = restore;
			}

			if ( !TryParseStops( p.ReadRemaining(), out var stops ) )
				return false;

			info = new GradientInfo
			{
				Angle = angle,
				GradientType = GradientInfo.GradientTypes.Conic,
				OffsetX = center[0],
				OffsetY = center[1],
				ColorOffsets = stops,
			};

			return true;
		}

		/// <summary>Shape and size keywords in any order, then an optional "at &lt;position&gt;".</summary>
		private bool TryParseRadialPrelude( string text, ref GradientInfo.RadialSizeMode sizeMode, ref bool circle, Length[] center )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return false;

			var p = new Parse( text );
			var recognised = false;

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				if ( p.Is( "at", 0, true ) )
				{
					p.Pointer += 2;

					if ( !TryParsePosition( ref p, center ) )
						return false;

					recognised = true;
					continue;
				}

				var word = p.ReadWord( null, true );
				if ( string.IsNullOrEmpty( word ) )
					return false;

				switch ( word.ToLowerInvariant() )
				{
					case "circle": circle = true; break;
					case "ellipse": circle = false; break;
					case "closest-side": sizeMode = GradientInfo.RadialSizeMode.ClosestSide; break;
					case "closest-corner": sizeMode = GradientInfo.RadialSizeMode.ClosestCorner; break;
					case "farthest-side": sizeMode = GradientInfo.RadialSizeMode.FarthestSide; break;
					case "farthest-corner": sizeMode = GradientInfo.RadialSizeMode.FarthestCorner; break;

					// A colour, or anything we don't know - this wasn't a prelude.
					default: return false;
				}

				recognised = true;
			}

			return recognised;
		}

		/// <summary>An optional "from &lt;angle&gt;", then an optional "at &lt;position&gt;".</summary>
		private bool TryParseConicPrelude( string text, ref float angle, Length[] center )
		{
			if ( string.IsNullOrWhiteSpace( text ) )
				return false;

			var p = new Parse( text );
			var recognised = false;

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				if ( p.Is( "from", 0, true ) )
				{
					p.Pointer += 4;
					p = p.SkipWhitespaceAndNewlines();

					var word = p.ReadWord( null, true );
					if ( string.IsNullOrEmpty( word ) )
						return false;

					// Straight CSS degrees - a conic's "from" rotates the sweep, it isn't
					// a direction, so it doesn't get the linear mirroring.
					var degrees = GetAngleInDegrees( word );
					if ( !degrees.HasValue )
						return false;

					angle = degrees.Value.Value.DegreeToRadian();

					recognised = true;
					continue;
				}

				if ( p.Is( "at", 0, true ) )
				{
					p.Pointer += 2;

					if ( !TryParsePosition( ref p, center ) )
						return false;

					recognised = true;
					continue;
				}

				return false;
			}

			return recognised;
		}

		/// <summary>
		/// A CSS position - one or two components of keywords or lengths. A single component
		/// leaves the other centred, and keywords may come in either order.
		/// </summary>
		private bool TryParsePosition( ref Parse p, Length[] center )
		{
			var gotX = false;
			var gotY = false;
			var loose = new List<Length>();

			while ( true )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) break;

				if ( p.Is( "left", 0, true ) ) { center[0] = Length.Percent( 0 ).Value; gotX = true; p.Pointer += 4; continue; }
				if ( p.Is( "right", 0, true ) ) { center[0] = Length.Percent( 100 ).Value; gotX = true; p.Pointer += 5; continue; }
				if ( p.Is( "top", 0, true ) ) { center[1] = Length.Percent( 0 ).Value; gotY = true; p.Pointer += 3; continue; }
				if ( p.Is( "bottom", 0, true ) ) { center[1] = Length.Percent( 100 ).Value; gotY = true; p.Pointer += 6; continue; }
				if ( p.Is( "center", 0, true ) ) { loose.Add( Length.Percent( 50 ).Value ); p.Pointer += 6; continue; }

				if ( p.TryReadLength( out var length ) )
				{
					loose.Add( length );
					continue;
				}

				return false;
			}

			// Whatever wasn't pinned by a keyword fills the axes left over, in order.
			foreach ( var length in loose )
			{
				if ( !gotX ) { center[0] = length; gotX = true; continue; }
				if ( !gotY ) { center[1] = length; gotY = true; continue; }

				return false;
			}

			return gotX || gotY;
		}

		private List<GradientGenerator> ParseGradient( string token )
		{
			var gradientGenerators = new List<GradientGenerator>();

			Color? lastColor = null;
			float? lastOffset = null;
			bool lastOffsetIsPixels = false;

			var p = new Parse( token );

			// Parse the color values
			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();

				// Read up to a comma or end of the text within the brackets
				var w = p.ReadSentence();
				var wp = new Parse( w );

				// First parse the color
				var c = Color.Parse( ref wp );
				if ( !c.HasValue )
				{
					Log.Error( $"Cannot read a color from '{w}'" );
					break;
				}

				wp = wp.SkipWhitespaceAndNewlines();

				// Then optionally parse the stop position
				float? offset = null;
				bool offsetIsPixels = false;

				if ( wp.IsDigit && wp.TryReadFloat( out var stop ) )
				{
					if ( wp.Is( '%' ) )
					{
						wp.Pointer++;
						offset = stop / 100;
					}
					else if ( wp.Is( "px", 0, true ) )
					{
						wp.Pointer += 2;
						offset = stop;
						offsetIsPixels = true;
					}
					else
					{
						Log.Error( $"Stop positions take a percentage or a pixel length: '{w}'" );
						break;
					}
				}

				wp = wp.SkipWhitespaceAndNewlines();

				if ( !wp.IsEnd )
				{
					Log.Error( $"Extra text found after color stop: '{w}'" );
					break;
				}

				if ( c.HasValue )
				{
					if ( lastColor.HasValue )
					{
						var gradient = new GradientGenerator();
						gradient.from.color = lastColor.Value;
						gradient.from.offset = lastOffset;
						gradient.from.offsetIsPixels = lastOffsetIsPixels;
						gradient.to.color = c.Value;
						gradient.to.offset = offset;
						gradient.to.offsetIsPixels = offsetIsPixels;

						gradientGenerators.Add( gradient );
					}

					lastColor = c;
					lastOffset = offset;
					lastOffsetIsPixels = offsetIsPixels;
				}

				if ( p.Is( ',' ) )
				{
					p.Pointer++;
				}
				else
				{
					break;
				}
			}

			if ( gradientGenerators.Count == 0 )
			{
				var solidColor = lastColor ?? Color.Black;
				var item = new GradientGenerator();
				item.from = new GradientColorOffset()
				{
					color = solidColor,
					offset = 0
				};
				item.to = new GradientColorOffset()
				{
					color = solidColor,
					offset = 1
				};

				gradientGenerators.Add( item );

				return gradientGenerators;
			}

			// Set the distance properties that were not initialized
			float perSliceDistance = 1.0f / (float)gradientGenerators.Count;

			for ( int i = 0; i < gradientGenerators.Count; i++ )
			{
				var gradientGenerator = gradientGenerators[i];

				if ( !gradientGenerator.from.offset.HasValue )
					gradientGenerator.from.offset = (float)i * perSliceDistance;
				if ( !gradientGenerator.to.offset.HasValue )
					gradientGenerator.to.offset = (float)(i + 1) * perSliceDistance;

				gradientGenerators[i] = gradientGenerator;
			}

			// fill in the gap if we weren't given a final stop point
			var lastGenerator = gradientGenerators[^1];
			if ( lastGenerator.to.offset.Value < 1 )
			{
				gradientGenerators.Add( new GradientGenerator
				{
					from = lastGenerator.to,
					to = new GradientColorOffset
					{
						color = lastGenerator.to.color,
						offset = 1,
					},
				} );
			}

			return gradientGenerators;
		}

		private int CalcOptimalGradientWidth()
		{
			var width = BackgroundSizeX?.GetPixels( 1f ) ?? Width?.GetPixels( 1f ) ?? 0f;
			var height = BackgroundSizeY?.GetPixels( 1f ) ?? Height?.GetPixels( 1f ) ?? 0f;

			var calcWidth = MathF.Max( width, height );
			var gradientWidth = Math.Clamp( (int)calcWidth, 256, 2048 );

			return gradientWidth;
		}

		private Color32 LerpPremultiplied( Color32 from, Color32 to, float t )
		{
			// Interpolate the way the web does: premultiplied components and alpha both
			// lerped linearly, in sRGB space, then un-premultiplied by that same alpha.
			// Premultiplying keeps transparent stops from bleeding their hue into the ramp.
			var colA = from.ToColor();
			var colB = to.ToColor();

			float a = colA.a + t * (colB.a - colA.a);

			float r = colA.r * colA.a + t * (colB.r * colB.a - colA.r * colA.a);
			float g = colA.g * colA.a + t * (colB.g * colB.a - colA.g * colA.a);
			float b = colA.b * colA.a + t * (colB.b * colB.a - colA.b * colA.a);

			if ( a > 0.0001f ) // avoid division by zero
			{
				r /= a;
				g /= a;
				b /= a;
			}

			return new Color( r, g, b, a ).ToColor32();
		}

		/// <summary>
		/// A stop's position as a fraction of the gradient. The baked path only has the texture to
		/// measure a pixel position against, where the shader has the real gradient line.
		/// </summary>
		private static float StopFraction( GradientColorOffset stop, int gradientWidth )
		{
			var offset = stop.offset ?? 0f;

			if ( stop.offsetIsPixels )
				return Math.Clamp( offset / Math.Max( gradientWidth, 1 ), 0f, 1f );

			return offset;
		}

		private byte[] GenerateGradient( string token, int gradientWidth )
		{
			var gradientGenerators = ParseGradient( token );

			byte[] gradientData = new byte[gradientWidth * 4];

			// Actually generate gradient data now
			foreach ( var gradient in gradientGenerators )
			{
				var fromColor = gradient.from.color.ToColor32();
				var toColor = gradient.to.color.ToColor32();

				int fromPixel = (int)(StopFraction( gradient.from, gradientWidth ) * gradientWidth);
				int toPixel = (int)(StopFraction( gradient.to, gradientWidth ) * gradientWidth);

				for ( int i = fromPixel; i < toPixel; i++ )
				{
					float j = (float)(i - fromPixel) / (float)(toPixel - fromPixel);

					var color = LerpPremultiplied( fromColor, toColor, j );

					gradientData[(i * 4) + 0] = color.r;
					gradientData[(i * 4) + 1] = color.g;
					gradientData[(i * 4) + 2] = color.b;
					gradientData[(i * 4) + 3] = color.a;
				}
			}

			return gradientData;
		}

		Texture GenerateConicGradientTexture( string token )
		{
			var p = new Parse( token );
			Vector2 centerOffset;

			// Temporary, this can be changed by client too
			centerOffset = new Vector2( 0.5f, 0.5f );

			var gradientWidth = CalcOptimalGradientWidth();
			byte[] gradientLUT = GenerateGradient( p.ReadRemaining(), gradientWidth );

			gradientWidth = gradientLUT.Length / 4;

			byte[] gradientData = new byte[gradientWidth * gradientWidth * 4];

			// Wrap the 1D linear gradient we have calculated into a cone
			for ( int x = 0; x < gradientWidth; x++ )
			{
				for ( int y = 0; y < gradientWidth; y++ )
				{
					Vector2 pos = new Vector2( (float)x / gradientWidth, (float)y / gradientWidth );
					var distance = (Math.Atan2( pos.y - centerOffset.y, pos.x - centerOffset.y ) + Math.PI) / (Math.PI * 2.0f);

					int s = Math.Clamp( gradientWidth - (int)(distance * gradientWidth), 0, gradientWidth - 1 );
					int outS = ((x * gradientWidth) + y) * 4;

					gradientData[outS + 0] = gradientLUT[(s * 4) + 0];
					gradientData[outS + 1] = gradientLUT[(s * 4) + 1];
					gradientData[outS + 2] = gradientLUT[(s * 4) + 2];
					gradientData[outS + 3] = gradientLUT[(s * 4) + 3];
				}
			}

			var gradientTexture = Texture.Create( gradientWidth, gradientWidth )
			.WithName( "conic-gradient" )
			.WithData( gradientData )
			.Finish();

			return gradientTexture;
		}

		Texture GenerateRadialGradientTexture( string token )
		{
			var p = new Parse( token );
			Vector2 centerOffset;

			// https://developer.mozilla.org/en-US/docs/Web/CSS/radial-gradient()
			//
			p.SkipWhitespaceAndNewlines();
			if ( p.Is( "closest-side", 0, true ) )
			{

			}
			else if ( p.Is( "closest-corner", 0, true ) )
			{

			}
			else if ( p.Is( "farthest-side", 0, true ) )
			{

			}
			else if ( p.Is( "farthest-corner", 0, true ) )
			{

			}

			// Temporary, this can be changed by client too
			centerOffset = new Vector2( 0.5f, 0.5f );

			var gradientWidth = CalcOptimalGradientWidth();
			byte[] gradientLUT = GenerateGradient( p.ReadRemaining(), gradientWidth );

			gradientWidth = gradientLUT.Length / 4;

			byte[] gradientData = new byte[gradientWidth * gradientWidth * 4];

			// Wrap the 1D linear gradient we have calculated into a radial
			for ( int x = 0; x < gradientWidth; x++ )
			{
				for ( int y = 0; y < gradientWidth; y++ )
				{
					Vector2 pos = new Vector2( (float)x / gradientWidth, (float)y / gradientWidth );
					var distance = Vector2.Distance( pos, centerOffset );

					int s = Math.Clamp( (int)(distance * gradientWidth), 0, gradientWidth - 1 );
					int outS = ((x * gradientWidth) + y) * 4;

					gradientData[outS + 0] = gradientLUT[(s * 4) + 0];
					gradientData[outS + 1] = gradientLUT[(s * 4) + 1];
					gradientData[outS + 2] = gradientLUT[(s * 4) + 2];
					gradientData[outS + 3] = gradientLUT[(s * 4) + 3];
				}
			}

			var gradientTexture = Texture.Create( gradientWidth, gradientWidth )
			.WithName( "radial-gradient" )
			.WithData( gradientData )
			.Finish();

			return gradientTexture;
		}

		Texture GenerateLinearGradientTexture( string token, out float angle )
		{
			angle = -1;

			var p = new Parse( token );

			var restoreP = p;
			var angleStr = p.ReadSentence();
			if ( TryParseAngle( angleStr, out angle ) )
			{
				p.Pointer++; // comma
			}
			else
			{
				p = restoreP;
			}

			var gradientWidth = CalcOptimalGradientWidth();
			byte[] gradientData = GenerateGradient( p.ReadRemaining(), gradientWidth );

			var gradientTexture = Texture.Create( 1, gradientData.Length / 4 )
			.WithName( "linear-gradient" )
			.WithData( gradientData )
			.Finish();

			return gradientTexture;
		}
	}
}
