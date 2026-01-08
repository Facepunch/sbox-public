using System.Collections.Immutable;
using System.Text;

namespace Sandbox.UI
{
	public partial class Styles
	{
		public override bool Set( string property, string value )
		{
			property = StyleParser.GetPropertyFromAlias( property );

			switch ( property )
			{
				case "transition":
				case "transition-delay":
				case "transition-duration":
				case "transition-property":
				case "transition-timing-function":
					Transitions = TransitionDesc.ParseProperty( property, value, Transitions );
					return true;

				case "display":
					return SetDisplay( value );

				case "pointer-events":
					return SetPointerEvents( value );

				case "position":
					return SetPosition( value );

				case "flex-direction":
					return SetFlexDirction( value );

				case "justify-content":
					return SetJustifyContent( value );

				case "flex-wrap":
					return SetFlexWrap( value );

				case "flex":
					return SetFlex( value );

				case "gap":
					return SetGap( value );

				case "padding":
					return SetPadding( value );

				case "margin":
					return SetMargin( value );

				case "border-radius":
					return SetBorderRadius( value );

				case "border":
					return SetBorder( value, w => BorderWidth = w, c => BorderColor = c );

				case "border-left":
					return SetBorder( value, w => BorderLeftWidth = w, c => BorderLeftColor = c );

				case "border-right":
					return SetBorder( value, w => BorderRightWidth = w, c => BorderRightColor = c );

				case "border-top":
					return SetBorder( value, w => BorderTopWidth = w, c => BorderTopColor = c );

				case "border-bottom":
					return SetBorder( value, w => BorderBottomWidth = w, c => BorderBottomColor = c );

				case "border-image":
					return SetBorderImage( value );

				case "border-color":
					Color? borderColor = Color.Parse( value );
					BorderColor = borderColor;
					return borderColor.HasValue;

				case "border-width":
					Length? borderWidth = Length.Parse( value );
					BorderWidth = borderWidth;
					return borderWidth.HasValue;

				case "backdrop-filter":
					return SetBackdropFilter( value );

				case "filter":
					return SetFilter( value );

				case "font-weight":
					return SetFontWeight( value );

				case "box-shadow":
					return SetShadow( value, ref BoxShadow );

				case "text-shadow":
					return SetShadow( value, ref TextShadow );

				case "filter-drop-shadow":
					return SetShadow( value, ref FilterDropShadow );

				case "align-content":
					AlignContent = GetAlign( value );
					return AlignContent.HasValue;

				case "align-self":
					AlignSelf = GetAlign( value );
					return AlignSelf.HasValue;

				case "align-items":
					AlignItems = GetAlign( value );
					return AlignItems.HasValue;

				case "text-align":
					return SetTextAlign( value );

				case "text-overflow":
					return SetTextOverflow( value );

				case "text-filter":
					return SetTextFilter( value );

				case "word-break":
					return SetWordBreak( value );

				case "text-decoration":
					return SetTextDecoration( value );

				case "text-decoration-line":
					return SetTextDecorationLine( value );

				case "text-decoration-skip-ink":
					return SetTextDecorationSkipInk( value );

				case "text-decoration-style":
					return SetTextDecorationStyle( value );

				case "text-stroke":
					return SetTextStroke( value );

				case "text-transform":
					return SetTextTransform( value );

				case "font-style":
					return SetFontStyle( value );

				case "white-space":
					return SetWhiteSpace( value );

				case "transform":
					return SetTransform( value );

				case "transform-origin":
					return SetTransformOrigin( value );

				case "perspective-origin":
					return SetPerspectiveOrigin( value );

				case "background":
					return SetBackground( value );

				case "background-image":
					return SetImage( value, SetBackgroundImageFromTexture, SetBackgroundSize, SetBackgroundRepeat, SetBackgroundAngle );

				case "background-size":
					return SetBackgroundSize( value );

				case "background-position":
					return SetBackgroundPosition( value );

				case "background-repeat":
					return SetBackgroundRepeat( value );

				case "image-rendering":
					return SetImageRendering( value );

				case "font-color":
					return SetFontColor( value );

				case "caret-color":
					return SetCaretColor( value );

				case "animation-iteration-count":
					if ( value == "infinite" )
					{
						AnimationIterationCount = float.PositiveInfinity;
						return true;
					}
					break;

				case "animation":
					return SetAnimation( value );

				case "mask":
					return SetMask( value );

				case "mask-image":
					return SetImage( value, SetMaskImageFromTexture, SetMaskSize, SetMaskRepeat, SetMaskAngle );

				case "mask-mode":
					return SetMaskMode( value );

				case "mask-size":
					return SetMaskSize( value );

				case "mask-repeat":
					return SetMaskRepeat( value );

				case "mask-position":
					return SetMaskPosition( value );

				case "mask-scope":
					return SetMaskScope( value );

				case "font-smooth":
					return SetFontSmooth( value );

				case "object-fit":
					return SetObjectFit( value );
			}

			return base.Set( property, value );
		}

		enum EBorderImageParseType
		{
			ParseSlice,
			ParseWidth
		};

		bool SetFontColor( string value )
		{
			Color? fontColor = Color.Parse( value );
			if ( fontColor.HasValue )
			{
				FontColor = fontColor;
				return true;
			}

			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();

			if ( GetTokenValueUnderParenthesis( p, "linear-gradient", out string gradient ) )
			{
				SetTextGradientLinear( gradient );
				return true;
			}

			if ( GetTokenValueUnderParenthesis( p, "radial-gradient", out string radialGradint ) )
			{
				SetTextGradientRadial( radialGradint );
				return true;
			}

			return false;
		}

		bool SetCaretColor( string value )
		{
			Color? caretColor = Color.Parse( value );
			if ( caretColor.HasValue )
			{
				CaretColor = caretColor;
				return true;
			}

			return false;
		}

		bool SetBorderTexture( Lazy<Texture> t )
		{
			_borderImageSource = t;
			return true;
		}

		bool SetBorderImage( string value )
		{
			var p = new Parse( value );

			p = p.SkipWhitespaceAndNewlines();

			if ( !SetImage( p.Text, SetBorderTexture ) )
				throw new Exception( "Expected image as first border-image parameter." );

			p.Pointer += p.ReadUntilOrEnd( ")" ).Length + 1;

			List<Length> borderSliceList = new List<Length>();
			List<Length> borderWidthList = new List<Length>();

			EBorderImageParseType parseType = EBorderImageParseType.ParseSlice;

			while ( !p.IsEnd )
			{
				if ( p.Is( "stretch", 0, true ) )
				{
					p.Pointer += "stretch".Length;
					BorderImageRepeat = UI.BorderImageRepeat.Stretch;
				}
				else if ( p.Is( "round", 0, true ) )
				{
					p.Pointer += "round".Length;
					BorderImageRepeat = UI.BorderImageRepeat.Round;
				}
				else if ( p.Is( "fill", 0, true ) )
				{
					p.Pointer += "fill".Length;
					BorderImageFill = UI.BorderImageFill.Filled;
				}
				else if ( p.Is( "/", 0, true ) )
				{
					p.Pointer++;

					//Needs to have at least one element before we do it
					if ( borderSliceList.Count == 0 )
						throw new Exception( "border-image needs at least one value before splitting ('/')" );

					//We don't support anything else
					if ( parseType == EBorderImageParseType.ParseWidth )
						throw new Exception( "border-image only supports up to slice and width params for splitting('/')" );

					parseType++;

				}
				else if ( p.TryReadLength( out Length lengthValue ) )
				{
					switch ( parseType )
					{
						case EBorderImageParseType.ParseSlice:
							borderSliceList.Add( lengthValue );
							break;
						case EBorderImageParseType.ParseWidth:
							borderWidthList.Add( lengthValue );
							break;
					}
				}


				if ( p.IsEnd )
					break;

				p.Pointer++;
				p.SkipWhitespaceAndNewlines();
			}

			//Parse our border slice pixel sizes
			switch ( borderSliceList.Count )
			{
				// 33.3% of texture size
				case 0:
					BorderImageWidthLeft = BorderImageWidthRight = BorderImageWidthTop = BorderImageWidthBottom = BorderImageSource.Width / 3.0f;
					break;

				//Uniform
				case 1:
					BorderImageWidthLeft = BorderImageWidthRight = BorderImageWidthTop = BorderImageWidthBottom = borderSliceList[0];
					break;

				// Top-Bottom and Left-Right
				case 2:
					BorderImageWidthTop = BorderImageWidthBottom = borderSliceList[0];
					BorderImageWidthLeft = BorderImageWidthRight = borderSliceList[1];
					break;

				// Top, Left-Right and Bottom
				case 3:
					BorderImageWidthTop = borderSliceList[0];
					BorderImageWidthLeft = BorderImageWidthRight = borderSliceList[1];
					BorderImageWidthBottom = borderSliceList[2];
					break;

				// Top, Right, Bottom, Left
				case 4:
					BorderImageWidthTop = borderSliceList[0];
					BorderImageWidthRight = borderSliceList[1];
					BorderImageWidthBottom = borderSliceList[2];
					BorderImageWidthLeft = borderSliceList[3];
					break;
			}

			//Parse our border width pixel sizes, we re use BorderWidth so we don't need to pass another uniform to the shader
			switch ( borderWidthList.Count )
			{
				//Just copy whwatever is on slice if nothing is set
				case 0:
					BorderLeftWidth = BorderImageWidthLeft;
					BorderRightWidth = BorderImageWidthRight;
					BorderTopWidth = BorderImageWidthTop;
					BorderBottomWidth = BorderImageWidthBottom;
					break;

				//Uniform
				case 1:
					BorderLeftWidth = BorderRightWidth = BorderTopWidth = BorderBottomWidth = borderWidthList[0];
					break;

				// Top-Bottom and Left-Right
				case 2:
					BorderTopWidth = BorderBottomWidth = borderWidthList[0];
					BorderLeftWidth = BorderRightWidth = borderWidthList[1];
					break;

				// Top, Left-Right and Bottom
				case 3:
					BorderTopWidth = borderWidthList[0];
					BorderLeftWidth = BorderRightWidth = borderWidthList[1];
					BorderBottomWidth = borderWidthList[2];
					break;

				// Top, Right, Bottom, Left
				case 4:
					BorderTopWidth = borderWidthList[0];
					BorderRightWidth = borderWidthList[1];
					BorderBottomWidth = borderWidthList[2];
					BorderLeftWidth = borderWidthList[3];
					break;
			}

			return true;
		}

		bool SetBorderRadius( string value )
		{
			var p = new Parse( value );

			p = p.SkipWhitespaceAndNewlines();

			if ( p.IsEnd )
				return false;

			if ( !p.TryReadLength( out var a ) )
				return false;

			if ( p.IsEnd || !p.TryReadLength( out var b ) )
			{
				BorderTopLeftRadius = a;
				BorderTopRightRadius = a;
				BorderBottomRightRadius = a;
				BorderBottomLeftRadius = a;
				return true;
			}

			if ( p.IsEnd || !p.TryReadLength( out var c ) )
			{
				BorderTopLeftRadius = a;
				BorderTopRightRadius = b;
				BorderBottomRightRadius = a;
				BorderBottomLeftRadius = b;
				return true;
			}

			if ( p.IsEnd || !p.TryReadLength( out var d ) )
			{
				BorderTopLeftRadius = a;
				BorderTopRightRadius = b;
				BorderBottomRightRadius = c;
				BorderBottomLeftRadius = b;
				return true;
			}

			BorderTopLeftRadius = a;
			BorderTopRightRadius = b;
			BorderBottomRightRadius = c;
			BorderBottomLeftRadius = d;
			return true;
		}

		bool SetMask( string value )
		{
			/*
			 * mask: <mask-reference> || <position> [ / <bg-size> ]? ||<repeat-style> || <geometry-box> || [ <geometry-box> | no-clip ] || <compositing-operator> || <masking-mode>
			 * https://developer.mozilla.org/en-US/docs/Web/CSS/mask#formal_syntax
			 * 
			 * mask: url(mask.png);
			 * mask: url(mask.png) luminance;
			 * mask: url(mask.png) 100px 200px;
			 * mask: url(mask.png) 100px 200px/50px 100px;
			 * mask: url(mask.png) repeat-x;
			 * mask: url(mask.png) 50% 50% / contain no-repeat border-box luminance;
			 */

			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();

			//
			// <mask-reference>
			//
			if ( !SetImage( p.Text, SetMaskImageFromTexture, SetMaskSize, SetMaskRepeat, SetMaskAngle ) )
				throw new Exception( "Expected image as first border-image parameter." );

			p.Pointer += p.ReadUntilOrEnd( ")" ).Length + 1;
			p = p.SkipWhitespaceAndNewlines();

			//
			// <position> [ / <bg-size> ]?
			//
			p.TryReadPositionAndSize( out var positionX, out var positionY, out var sizeX, out var sizeY );
			MaskPositionX = positionX;
			MaskPositionY = positionY;
			if ( sizeX.Unit != LengthUnit.Auto ) MaskSizeX = sizeX;
			if ( sizeY.Unit != LengthUnit.Auto ) MaskSizeY = sizeY;

			//
			// <repeat-style>
			//
			if ( p.TryReadRepeat( out var repeat ) )
				SetMaskRepeat( repeat );

			//
			// <masking-mode>
			//
			if ( p.TryReadMaskMode( out var maskMode ) )
				SetMaskMode( maskMode );

			return true;
		}

		bool SetFlex( string value )
		{
			/*
			 * flex: none | [ <'flex-grow'> <'flex-shrink'>? || <'flex-basis'> ]
			 * https://drafts.csswg.org/css-flexbox/#flex-property
			 */

			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();

			int floatCount = 0;

			while ( !p.IsEnd )
			{
				var word = p.ReadWord( " ", true ).ToLower();
				p.Pointer -= word.Length;

				if ( word == "none" )
				{
					// "none" expands to 0 0 auto
					FlexShrink ??= 0;
					FlexGrow ??= 0;
					FlexBasis = Length.Auto;

					return true;
				}
				else if ( word == "auto" )
				{
					// "auto" expands to 1 1 auto
					FlexShrink ??= 1;
					FlexGrow ??= 1;
					FlexBasis = Length.Auto;

					return true;
				}
				else if ( word == "initial" )
				{
					// "initial" expands to 0 1 auto
					FlexShrink ??= 0;
					FlexGrow ??= 1;
					FlexBasis = Length.Auto;

					return true;
				}
				else
				{
					var maybeLength = p;
					var maybeFloat = p.ReadUntilWhitespaceOrNewlineOrEnd();

					// TryReadFloat eats lengths, TryReadLength eats floats
					// settle it with this
					if ( float.TryParse( maybeFloat, out float val ) )
					{
						if ( floatCount == 0 )
						{
							FlexGrow = val;

							// "flex: 1" expands to <number [1]> 1 0
							if ( val == 1 )
							{
								FlexShrink = 1;
								FlexBasis = 0;
							}
						}
						else
						{
							FlexShrink = val;
						}

						floatCount++;
					}
					else if ( maybeLength.TryReadLength( out var len ) )
					{
						FlexGrow ??= 0;
						FlexShrink ??= 1;
						FlexBasis = len;
						return true;
					}
					else
					{
						Log.Error( $"Couldn't parse flex {value} - expected a float or length" );
						return false;
					}
				}

				p.SkipWhitespaceAndNewlines();
			}

			return true;
		}


		bool SetFilterBorderWrap( string value )
		{
			var p = new Parse( value );

			p = p.SkipWhitespaceAndNewlines();

			while ( !p.IsEnd )
			{
				if ( p.TryReadLength( out var lengthValue ) )
					FilterBorderWidth = lengthValue;
				else if ( p.TryReadColor( out var colorValue ) )
					FilterBorderColor = colorValue;
				else
					return false;

				p = p.SkipWhitespaceAndNewlines();
			}

			return true;
		}

		bool SetBorder( string value, Action<Length?> setWidth, Action<Color?> setColor )
		{
			var p = new Parse( value );

			p = p.SkipWhitespaceAndNewlines();

			while ( !p.IsEnd )
			{
				if ( p.TryReadLineStyle( out var lineStyle ) )
				{
					if ( lineStyle == "none" )
					{
						setWidth( Length.Pixels( 0 ) );
						return true;
					}
				}
				else if ( p.TryReadLength( out var lengthValue ) )
				{
					setWidth( lengthValue );
				}
				else if ( p.TryReadColor( out var colorValue ) )
				{
					setColor( colorValue );
				}
				else
				{
					return false;
				}

				p = p.SkipWhitespaceAndNewlines();
			}

			return true;
		}

		bool SetPadding( string value )
		{
			var p = new Parse( value );

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return false;

			if ( p.TryReadLength( out var a ) )
			{
				Padding = a;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( p.TryReadLength( out var b ) )
			{
				PaddingLeft = b;
				PaddingRight = b;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( p.TryReadLength( out var c ) )
			{
				PaddingBottom = c;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( p.TryReadLength( out var d ) )
			{
				PaddingTop = a;
				PaddingRight = b;
				PaddingBottom = c;
				PaddingLeft = d;
			}

			return true;
		}

		bool SetMargin( string value )
		{
			var p = new Parse( value );

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return false;

			if ( p.TryReadLength( out var a ) )
			{
				MarginLeft = a;
				MarginTop = a;
				MarginRight = a;
				MarginBottom = a;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( p.TryReadLength( out var b ) )
			{
				MarginLeft = b;
				MarginRight = b;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( p.TryReadLength( out var c ) )
			{
				MarginBottom = c;
			}

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( p.TryReadLength( out var d ) )
			{
				MarginTop = a;
				MarginRight = b;
				MarginBottom = c;
				MarginLeft = d;
			}

			return true;
		}

		bool SetFontWeight( string value )
		{
			if ( int.TryParse( value, out var i ) )
			{
				FontWeight = i;
				return true;
			}

			switch ( value )
			{
				case "hairline":
				case "thin":
					FontWeight = 100;
					return true;
				case "ultralight":
				case "extralight":
					FontWeight = 200;
					return true;
				case "light":
					FontWeight = 300;
					return true;
				case "regular":
				case "normal":
					FontWeight = 400;
					return true;
				case "medium":
					FontWeight = 500;
					return true;
				case "demibold":
				case "semibold":
					FontWeight = 600;
					return true;
				case "bold":
					FontWeight = 700;
					return true;
				case "ultabold":
				case "extrabold":
					FontWeight = 800;
					return true;
				case "heavy":
				case "black":
					FontWeight = 900;
					return true;
				case "extrablack":
				case "ultrablack":
					FontWeight = 950;
					return true;

				// TODO: These should change depending on the parents value.
				case "bolder":
					// Parent 100->300 = 400 weight
					// Parent 400->500 = 700 weight
					// Parent 600+ = 900 Weight
					FontWeight = 900;
					return true;

				case "lighter":
					// Parent 100->500 = 100 weight
					// Parent 600->700 = 400 weight
					// Parent 800+ = 700 weight
					FontWeight = 200;
					return true;
			}


			return false;
		}


		bool SetShadow( string value, ref ShadowList shadowList )
		{
			var p = new Parse( value );

			shadowList.Clear();

			if ( p.Is( "none", 0, true ) )
			{
				shadowList.IsNone = true;
				return true;
			}

			while ( !p.IsEnd )
			{
				var shadow = new Shadow();

				if ( !p.TryReadLength( out var x ) )
					return false;

				if ( !p.TryReadLength( out var y ) )
					return false;

				shadow.OffsetX = x.Value;
				shadow.OffsetY = y.Value;

				if ( p.TryReadLength( out var blur ) )
				{
					shadow.Blur = blur.Value;

					if ( p.TryReadLength( out var spread ) )
					{
						shadow.Spread = spread.Value;
					}
				}

				if ( p.TryReadColor( out var color ) )
				{
					shadow.Color = color;
				}

				p.SkipWhitespaceAndNewlines();

				if ( p.TryReadShadowInset( out var inset ) )
				{
					shadow.Inset = inset;
				}

				shadowList.Add( shadow );

				p.SkipWhitespaceAndNewlines();

				if ( p.IsEnd || p.Current != ',' )
					return true;

				p.Pointer++;
				p.SkipWhitespaceAndNewlines();
			}

			return true;
		}

		bool SetTextStroke( string value )
		{
			var p = new Parse( value );

			if ( !p.TryReadLength( out var width ) )
				return false;

			if ( !p.TryReadColor( out var color ) )
				return false;

			TextStrokeWidth = width;
			TextStrokeColor = color;

			return true;
		}

		bool SetDisplay( string value )
		{
			switch ( value )
			{
				case "none":
					Display = DisplayMode.None;
					return true;
				case "flex":
					Display = DisplayMode.Flex;
					return true;
				case "contents":
					Display = DisplayMode.Contents;
					return true;
				default:
					Log.Warning( $"Unhandled display property: {value}" );
					return false;
			}
		}

		bool SetPointerEvents( string value )
		{
			switch ( value )
			{
				case "auto":
					PointerEvents = null;
					return true;
				case "none":
					PointerEvents = UI.PointerEvents.None;
					return true;
				case "all":
					PointerEvents = UI.PointerEvents.All;
					return true;
				default:
					Log.Warning( $"Unhandled pointer-events value: {value} (expected auto, none, all)" );
					return false;
			}
		}

		bool SetPosition( string value )
		{
			switch ( value )
			{
				case "static":
					Position = PositionMode.Static;
					return true;
				case "absolute":
					Position = PositionMode.Absolute;
					return true;
				case "relative":
					Position = PositionMode.Relative;
					return true;
				default:
					Log.Warning( $"Unhandled position property: {value}" );
					return false;
			}
		}


		bool SetFlexDirction( string value )
		{
			switch ( value )
			{
				case "column":
					FlexDirection = UI.FlexDirection.Column;
					return true;
				case "column-reverse":
					FlexDirection = UI.FlexDirection.ColumnReverse;
					return true;
				case "row":
					FlexDirection = UI.FlexDirection.Row;
					return true;
				case "row-reverse":
					FlexDirection = UI.FlexDirection.RowReverse;
					return true;
				default:
					Log.Warning( $"Unhandled flex-direction property: {value}" );
					return false;
			}
		}

		bool SetFlexWrap( string value )
		{
			switch ( value )
			{
				case "nowrap":
					FlexWrap = Wrap.NoWrap;
					return true;
				case "wrap":
					FlexWrap = Wrap.Wrap;
					return true;
				case "wrap-reverse":
					FlexWrap = Wrap.WrapReverse;
					return true;
				default:
					Log.Warning( $"Unhandled flex-wrap property: {value}" );
					return false;
			}
		}

		bool SetGap( string value )
		{
			// gap =
			//  < 'row-gap' > < 'column-gap' >?

			var p = new Parse( value );

			if ( !p.TryReadLength( out var gap ) )
				return false;

			RowGap = gap;
			ColumnGap = gap;

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return true;

			if ( !p.TryReadLength( out var colGap ) )
				return false;

			ColumnGap = colGap;

			return true;
		}

		bool SetJustifyContent( string value )
		{
			switch ( value )
			{
				case "flex-start":
					JustifyContent = UI.Justify.FlexStart;
					return true;
				case "center":
					JustifyContent = UI.Justify.Center;
					return true;
				case "flex-end":
					JustifyContent = UI.Justify.FlexEnd;
					return true;
				case "space-between":
					JustifyContent = UI.Justify.SpaceBetween;
					return true;
				case "space-around":
					JustifyContent = UI.Justify.SpaceAround;
					return true;
				case "space-evenly":
					JustifyContent = UI.Justify.SpaceEvenly;
					return true;
				default:
					Log.Warning( $"Unhandled justify-content property: {value}" );
					return false;
			}
		}

		private Align? GetAlign( string value )
		{
			switch ( value )
			{
				case "auto": return Align.Auto;
				case "flex-end": return Align.FlexEnd;
				case "flex-start": return Align.FlexStart;
				case "center": return Align.Center;
				case "stretch": return Align.Stretch;
				case "space-between": return Align.SpaceBetween;
				case "space-around": return Align.SpaceAround;
				case "space-evenly": return Align.SpaceEvenly;
				case "baseline": return Align.Baseline;
				default:
					Log.Warning( $"Unhandled align property: {value}" );
					return null;
			}
		}

		bool SetTextAlign( string value )
		{
			switch ( value )
			{
				case "center":
					TextAlign = UI.TextAlign.Center;
					return true;
				case "left":
					TextAlign = UI.TextAlign.Left;
					return true;
				case "right":
					TextAlign = UI.TextAlign.Right;
					return true;
				default:
					Log.Warning( $"Unhandled text-align property: {value}" );
					return false;
			}
		}

		bool SetTextOverflow( string value )
		{
			switch ( value )
			{
				case "ellipsis":
					TextOverflow = UI.TextOverflow.Ellipsis;
					return true;
				case "clip":
					TextOverflow = UI.TextOverflow.Clip;
					return true;
				default:
					Log.Warning( $"Unhandled text-overflow property: {value}" );
					return false;
			}
		}

		bool SetTextFilter( string value )
		{
			switch ( value )
			{
				case "linear":
				case "bilinear":
					TextFilter = Rendering.FilterMode.Bilinear;
					return true;
				case "point":
					TextFilter = Rendering.FilterMode.Point;
					return true;
				case "trilinear":
					TextFilter = Rendering.FilterMode.Trilinear;
					return true;
				case "anisotropic":
					TextFilter = Rendering.FilterMode.Anisotropic;
					return true;
				default:
					Log.Warning( $"Unhandled text-filter property: {value}" );
					return false;
			}
		}

		bool SetWordBreak( string value )
		{
			switch ( value )
			{
				case "normal":
					WordBreak = UI.WordBreak.Normal;
					return true;
				case "break-all":
					WordBreak = UI.WordBreak.BreakAll;
					return true;
				default:
					Log.Warning( $"Unhandled word-break property: {value}" );
					return false;
			}
		}

		UI.TextDecoration GetTextDecorationFromValue( string value )
		{
			var td = UI.TextDecoration.None;

			if ( value.Contains( "underline" ) ) td |= UI.TextDecoration.Underline;
			if ( value.Contains( "line-through" ) ) td |= UI.TextDecoration.LineThrough;
			if ( value.Contains( "overline" ) ) td |= UI.TextDecoration.Overline;

			return td;
		}

		bool SetTextDecoration( string value )
		{
			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return false;

			var td = UI.TextDecoration.None;

			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.TryReadLength( out var decorationThickness ) )
				{
					TextDecorationThickness = decorationThickness;
					continue;
				}

				if ( p.TryReadColor( out var decorationColor ) )
				{
					TextDecorationColor = decorationColor;
					continue;
				}

				var subValue = p.ReadWord( null, true );

				var textDecoration = GetTextDecorationFromValue( subValue );
				if ( textDecoration != UI.TextDecoration.None )
				{
					td |= textDecoration;
					continue;
				}

				if ( !SetTextDecorationStyle( subValue ) )
				{
					return false;
				}
			}

			if ( td != UI.TextDecoration.None )
			{
				TextDecorationLine = td;
			}

			return true;
		}

		bool SetTextDecorationLine( string value )
		{
			TextDecorationLine = GetTextDecorationFromValue( value );
			return true;
		}

		bool SetTextDecorationSkipInk( string value )
		{
			switch ( value )
			{
				case "auto":
				case "all":
					TextDecorationSkipInk = UI.TextSkipInk.All;
					return true;
				case "none":
					TextDecorationSkipInk = UI.TextSkipInk.None;
					return true;
				default:
					Log.Warning( $"Unhandled text-decoration-skip-ink property: {value}" );
					return false;
			}
		}

		bool SetTextDecorationStyle( string value )
		{
			switch ( value )
			{
				case "solid":
					TextDecorationStyle = UI.TextDecorationStyle.Solid;
					return true;
				case "double":
					TextDecorationStyle = UI.TextDecorationStyle.Double;
					return true;
				case "dotted":
					TextDecorationStyle = UI.TextDecorationStyle.Dotted;
					return true;
				case "dashed":
					TextDecorationStyle = UI.TextDecorationStyle.Dashed;
					return true;
				case "wavy":
					TextDecorationStyle = UI.TextDecorationStyle.Wavy;
					return true;
				default:
					Log.Warning( $"Unhandled text-decoration-style property: {value}" );
					return false;
			}
		}

		bool SetFontStyle( string value )
		{
			var fs = UI.FontStyle.None;

			if ( value.Contains( "italic" ) ) fs |= UI.FontStyle.Italic;
			if ( value.Contains( "oblique" ) ) fs |= UI.FontStyle.Oblique;

			FontStyle = fs;
			return true;
		}

		bool SetWhiteSpace( string value )
		{
			switch ( value )
			{
				case "normal":
					WhiteSpace = UI.WhiteSpace.Normal;
					break;
				case "nowrap":
					WhiteSpace = UI.WhiteSpace.NoWrap;
					break;
				case "pre-line":
					WhiteSpace = UI.WhiteSpace.PreLine;
					break;
				case "pre":
					WhiteSpace = UI.WhiteSpace.Pre;
					break;
				default:
					Log.Warning( $"Unhandled white-space property: {value}" );
					return false;
			}
			return true;
		}

		bool SetTextTransform( string value )
		{
			switch ( value )
			{
				case "capitalize":
					TextTransform = UI.TextTransform.Capitalize;
					return true;
				case "uppercase":
					TextTransform = UI.TextTransform.Uppercase;
					return true;
				case "lowercase":
					TextTransform = UI.TextTransform.Lowercase;
					return true;
				case "none":
					TextTransform = UI.TextTransform.None;
					return true;
				default:
					Log.Warning( $"Unhandled text-transform property: {value}" );
					return false;
			}
		}

		bool SetTransformOrigin( string value )
		{
			var p = new Parse( value );

			if ( !p.TryReadLength( out var x ) )
				return false;

			TransformOriginX = x;

			if ( !p.TryReadLength( out var y ) )
			{
				TransformOriginY = x;
				return true;
			}

			TransformOriginY = y;
			return true;
		}

		bool SetTransform( string value )
		{
			if ( string.IsNullOrEmpty( value ) || value == "none" )
			{
				Transform = null;
				return true;
			}

			var t = new PanelTransform();
			t.Parse( value );

			Transform = t;

			return true;
		}

		bool SetPerspectiveOrigin( string value )
		{
			var p = new Parse( value );

			if ( !p.TryReadLength( out var x ) )
				return false;

			PerspectiveOriginX = x;

			if ( !p.TryReadLength( out var y ) )
			{
				PerspectiveOriginY = x;
				return true;
			}

			// Do we want to move away from standard and let us specify a Z too?

			PerspectiveOriginY = y;
			return true;
		}

		bool GetTokenValueUnderParenthesis( Parse p, string tokenName, out string result )
		{
			if ( p.Is( tokenName, 0, true ) )
			{
				p.Pointer += tokenName.Length;
				p = p.SkipWhitespaceAndNewlines();

				if ( p.Current != '(' ) throw new System.Exception( "Expected ( after " + tokenName );

				p.Pointer++;

				int stack = 1;
				var wordStart = p;

				while ( !p.IsEnd && stack > 0 )
				{
					p.Pointer++;
					if ( p.Current == '(' ) stack++;
					if ( p.Current == ')' ) stack--;
				}

				if ( p.IsEnd ) throw new System.Exception( "Expected ) after " + tokenName );

				result = wordStart.Read( p.Pointer - wordStart.Pointer );
				return true;
			}
			result = "";
			return false;
		}

		bool SetBackground( string value )
		{
			/*
			 * We support a version of the "background" syntax that consists only of
			 * the final background layer; we also omit:
			 * - background-attachment
			 * - background-clip
			 * - background-origin
			 * 
			 * so our syntax can be defined as:
			 * background: <bg-image> || <bg-position> [ / <bg-size> ]? || <repeat-style> || <'background-color'>
			 * https://drafts.csswg.org/css-backgrounds/#the-background
			 */

			var p = new Parse( value );

			var bgBuilder = new StringBuilder();
			var lengthList = new List<Length>();
			var keywords = new List<string>();

			// Values (like linear-gradient(...), #ff00ff, etc.) need special handling - we read those
			// until we reach an end bracket, rather than a space
			int depth = 0;

			while ( !p.IsEnd )
			{
				p.SkipWhitespaceAndNewlines();

				var part = p.ReadWord( " ", true );

				if ( part.Contains( "#" ) )
					depth++;

				depth += part.Count( x => x == '(' );

				if ( depth > 0 )
				{
					depth -= part.Count( x => x == ')' );
					bgBuilder.Append( part + " " );
				}
				else
				{
					// Ignore separators
					if ( part == "/" ) continue;

					var length = Length.Parse( part );
					if ( length != null ) lengthList.Add( length!.Value );
					else if ( part == "repeat-x" || part == "repeat-y" || part == "repeat" || part == "space" || part == "round" || part == "no-repeat" )
					{
						//
						// <repeat-style>
						//
						SetBackgroundRepeat( part );
					}
					else
					{
						Log.Warning( $"Unrecognised part {part} in background" );
					}
				}

				p.SkipWhitespaceAndNewlines();
			}

			//
			// <bg-image> / <background-color>
			//
			string bgSource = bgBuilder.ToString().Trim();

			if ( bgSource.StartsWith( "#" ) || bgSource.StartsWith( "rgb(" ) || bgSource.StartsWith( "hsv(" ) )
				BackgroundColor = Color.Parse( bgSource ) ?? default;
			else
				SetImage( bgSource, SetBackgroundImageFromTexture, SetBackgroundSize, SetBackgroundRepeat, SetBackgroundAngle );

			//
			// <bg-position> [ / <bg-size> ]?
			//
			if ( lengthList.Count > 0 )
			{
				// Position X and Y
				BackgroundPositionX = lengthList[0];
				BackgroundPositionY = lengthList[0];

				switch ( lengthList.Count )
				{
					case 2:
					case 3:
						// Size
						BackgroundSizeX = lengthList[1];
						BackgroundSizeY = lengthList[1];

						if ( lengthList.Count == 3 )
						{
							// Position Y
							BackgroundPositionY = lengthList[1];

							// Size
							BackgroundSizeX = lengthList[2];
							BackgroundSizeY = lengthList[2];
						}
						break;
					case 4:
						// Position Y, Size X, Size Y
						BackgroundPositionY = lengthList[1];
						BackgroundSizeX = lengthList[2];
						BackgroundSizeY = lengthList[3];
						break;
				}
			}

			return true;
		}

		bool SetAnimation( string value )
		{
			/* <single-animation> = 
			 *		<time [0s,∞]> || <easing-function> || <time> || <single-animation-iteration-count> || <single-animation-direction> 
			 *		|| <single-animation-fill-mode> || <single-animation-play-state> || [ none | <keyframes-name> ] 
			*/

			var p = new Parse( value );

			//
			// animation: none;
			//
			if ( p.Is( "none", 0, true ) )
			{
				AnimationName = "none";
				return true;
			}

			int timeCount = 0;
			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();

				// The first value in each <single-animation> that can be parsed as a <time> is assigned to the animation-duration
				// The second value in each <single-animation> that can be parsed as a <time> is assigned to animation-delay
				if ( p.TryReadTime( out var time ) )
				{
					if ( timeCount == 0 )
						AnimationDuration = time / 1000.0f; // ms to s
					else
						AnimationDelay = time / 1000.0f; // ms to s

					timeCount++;
					continue;
				}

				// When parsing, keywords that are valid for properties other than animation-name whose values were not found earlier
				// in the shorthand must be accepted for those properties rather than for animation-name.
				var word = p.ReadWord( null, true ).ToLower();

				if ( Utility.Easing.TryGetFunction( word, out _ ) )
				{
					AnimationTimingFunction = word;
				}
				else if ( int.TryParse( word, out int iterationCount ) || word == "infinite" )
				{
					if ( word == "infinite" )
						AnimationIterationCount = float.PositiveInfinity;
					else
						AnimationIterationCount = iterationCount;
				}
				else if ( word == "normal" || word == "reverse" || word == "alternate" || word == "alternate-reverse" )
				{
					AnimationDirection = word;
				}
				else if ( word == "none" || word == "forwards" || word == "backwards" || word == "both" )
				{
					AnimationFillMode = word;
				}
				else if ( word == "running" || word == "paused" )
				{
					AnimationPlayState = word;
				}
				else
				{
					AnimationName = word;
				}
			}

			return true;
		}

		/// <param name="value"></param>
		/// <param name="setImage">Optional</param>
		/// <param name="setSize">Optional</param>
		/// <param name="setRepeat">Optional</param>
		/// <param name="setAngle">Optional</param>
		bool SetImage( string value, Func<Lazy<Texture>, bool> setImage = null, Func<string, bool> setSize = null, Func<string, bool> setRepeat = null, Func<float, bool> setAngle = null )
		{
			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();

			// TODO - support for multiple?

			if ( p.Is( "none", 0, true ) )
			{
				setImage( new Lazy<Texture>( Texture.Invalid ) );
				return true;
			}

			if ( GetTokenValueUnderParenthesis( p, "url", out string url ) )
			{
				url = url.Trim( ' ', '"', '\'' );
				setImage( new Lazy<Texture>( () =>
				{
					return Texture.Load( url ) ?? Texture.Invalid;
				} ) );
				return true;
			}

			if ( GetTokenValueUnderParenthesis( p, "linear-gradient", out string gradient ) )
			{
				Texture gradientTexture = GenerateLinearGradientTexture( gradient, out var angle );
				setAngle?.Invoke( angle );
				setImage?.Invoke( new Lazy<Texture>( gradientTexture ) );
				setSize?.Invoke( "100%" );
				setRepeat?.Invoke( "clamp" );
				return true;
			}

			if ( GetTokenValueUnderParenthesis( p, "radial-gradient", out string radialGradient ) )
			{
				setImage?.Invoke( new Lazy<Texture>( GenerateRadialGradientTexture( radialGradient ) ) );
				setSize?.Invoke( "100%" );
				setRepeat?.Invoke( "clamp" );
				return true;
			}

			if ( GetTokenValueUnderParenthesis( p, "conic-gradient", out string conicGradient ) )
			{
				setImage?.Invoke( new Lazy<Texture>( GenerateConicGradientTexture( conicGradient ) ) );
				setSize?.Invoke( "100%" );
				setRepeat?.Invoke( "clamp" );
				return true;
			}

			if ( GetTokenValueUnderParenthesis( p, "material", out string materialUrl ) )
			{
				return true;
			}

			Log.Warning( $"Unknown Image Type \"{value}\"\n" );

			return false;
		}

		bool SetImageRendering( string value )
		{
			switch ( value )
			{
				case "auto":
				case "anisotropic":
					ImageRendering = UI.ImageRendering.Anisotropic;
					return true;
				case "bilinear":
					ImageRendering = UI.ImageRendering.Bilinear;
					return true;
				case "trilinear":
					ImageRendering = UI.ImageRendering.Trilinear;
					return true;
				case "point":
				case "pixelated":
				case "nearest-neighbor":
					ImageRendering = UI.ImageRendering.Point;
					return true;
			}

			Log.Warning( $"Unknown Image Rendering \"{value}\"\n" );

			return false;
		}

		bool SetBackdropFilter( string value )
		{
			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();

			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) return true;

				var name = p.ReadWord( "(" );
				var innervalue = p.ReadInnerBrackets();

				if ( name == "blur" )
				{
					BackdropFilterBlur = Length.Parse( innervalue );
					continue;
				}

				if ( name == "invert" )
				{
					BackdropFilterInvert = Length.Parse( innervalue );
					continue;
				}

				if ( name == "contrast" )
				{
					BackdropFilterContrast = Length.Parse( innervalue );
					continue;
				}

				if ( name == "brightness" )
				{
					BackdropFilterBrightness = Length.Parse( innervalue );
					continue;
				}

				if ( name == "grayscale" )
				{
					BackdropFilterSaturate = Length.Parse( innervalue );

					if ( BackdropFilterSaturate.HasValue )
					{
						var val = BackdropFilterSaturate.Value.GetPixels( 1 );
						BackdropFilterSaturate = 1 - val;
					}

					continue;
				}

				if ( name == "saturate" )
				{
					BackdropFilterSaturate = Length.Parse( innervalue );
					continue;
				}

				if ( name == "sepia" )
				{
					BackdropFilterSepia = Length.Parse( innervalue );
					continue;
				}

				if ( name == "hue-rotate" )
				{
					BackdropFilterHueRotate = Length.Parse( innervalue );
					continue;
				}

				return false;
			}

			return true;
		}

		bool SetFilter( string value )
		{
			var p = new Parse( value );
			p = p.SkipWhitespaceAndNewlines();

			while ( !p.IsEnd )
			{
				p = p.SkipWhitespaceAndNewlines();
				if ( p.IsEnd ) return true;

				var name = p.ReadWord( "(" );
				var innervalue = p.ReadInnerBrackets();

				switch ( name )
				{
					case "blur":
						FilterBlur = Length.Parse( innervalue );
						break;
					case "saturate":
						FilterSaturate = Length.Parse( innervalue );
						break;
					case "greyscale":
					case "grayscale":
						FilterSaturate = Length.Parse( innervalue );

						if ( FilterSaturate.HasValue )
						{
							var val = FilterSaturate.Value.GetPixels( 1 );
							FilterSaturate = 1 - val;
						}
						break;
					case "sepia":
						FilterSepia = Length.Parse( innervalue );
						break;
					case "brightness":
						FilterBrightness = Length.Parse( innervalue );
						break;
					case "contrast":
						FilterContrast = Length.Parse( innervalue );
						break;
					case "hue-rotate":
						FilterHueRotate = Length.Parse( innervalue );
						break;
					case "invert":
						FilterInvert = Length.Parse( innervalue );
						break;
					case "tint":
						FilterTint = Color.Parse( innervalue );
						break;
					case "drop-shadow":
						var shadowList = new ShadowList();
						SetShadow( innervalue, ref shadowList );
						FilterDropShadow = shadowList;
						break;
					case "border-wrap":
						SetFilterBorderWrap( innervalue );
						break;
					default:
						Log.Warning( $"Unknown filter property {innervalue}" );
						return false;
				}

			}

			return true;
		}

		bool SetMaskImageFromTexture( Lazy<Texture> texture )
		{
			if ( texture == null )
				return true;

			_maskImage = texture;

			return true;
		}

		bool SetMaskMode( string value )
		{
			switch ( value )
			{
				case "match-source":
					MaskMode = UI.MaskMode.MatchSource;
					return true;
				case "alpha":
					MaskMode = UI.MaskMode.Alpha;
					return true;
				case "luminance":
					MaskMode = UI.MaskMode.Luminance;
					return true;
				default:
					Log.Warning( $"Unhandled mask-mode property: {value}" );
					return false;
			}
		}

		bool SetBackgroundImageFromTexture( Lazy<Texture> texture )
		{
			if ( texture == null )
				return true;

			_backgroundImage = texture;
			Dirty();

			return true;
		}

		bool SetBackgroundAngle( float value )
		{
			if ( value < 0 )
				return false;

			BackgroundAngle = value;
			return true;
		}

		bool SetMaskAngle( float value )
		{
			if ( value < 0 )
				return false;

			MaskAngle = value;
			return true;
		}

		bool TryParseAngle( string value, out float outAngle )
		{
			outAngle = 0.0f;

			var angle = GetAngleInDegrees( value );

			if ( !angle.HasValue ) return false;

			var angleDeg = angle.Value;

			// The shader expects radians.
			var angleRad = angleDeg.Value.DegreeToRadian();
			outAngle = angleRad;

			return true;
		}

		bool SetBackgroundSize( string value )
		{
			var p = new Parse( value );
			if ( p.TryReadLength( out var lenx ) )
			{
				BackgroundSizeX = lenx;
				BackgroundSizeY = lenx;

				if ( p.TryReadLength( out var leny ) )
				{
					BackgroundSizeY = leny;
				}
			}

			return true;
		}

		bool SetMaskPosition( string value )
		{
			var p = new Parse( value );
			if ( p.TryReadLength( out var lenx ) )
			{
				MaskPositionX = lenx;
				MaskPositionY = lenx;

				if ( p.TryReadLength( out var leny ) )
				{
					MaskPositionY = leny;
				}
			}

			return true;
		}

		bool SetMaskSize( string value )
		{
			var p = new Parse( value );
			if ( p.TryReadLength( out var lenx ) )
			{
				MaskSizeX = lenx;
				MaskSizeY = lenx;

				if ( p.TryReadLength( out var leny ) )
				{
					MaskSizeY = leny;
				}
			}

			return true;
		}

		bool SetBackgroundPosition( string value )
		{
			var p = new Parse( value );
			if ( p.TryReadLength( out var lenx ) )
			{
				BackgroundPositionX = lenx;
				BackgroundPositionY = lenx;

				if ( p.TryReadLength( out var leny ) )
				{
					BackgroundPositionY = leny;
				}
			}

			return true;
		}

		bool SetMaskScope( string value )
		{
			switch ( value )
			{
				case "default":
					MaskScope = Sandbox.UI.MaskScope.Default;
					return true;

				case "filter":
					MaskScope = Sandbox.UI.MaskScope.Filter;
					return true;
			}

			return false;
		}

		bool SetMaskRepeat( string value )
		{
			switch ( value )
			{
				case "no-repeat":
					MaskRepeat = Sandbox.UI.BackgroundRepeat.NoRepeat;
					return true;

				case "repeat-x":
					MaskRepeat = Sandbox.UI.BackgroundRepeat.RepeatX;
					return true;

				case "repeat-y":
					MaskRepeat = Sandbox.UI.BackgroundRepeat.RepeatY;
					return true;

				case "repeat":
					MaskRepeat = Sandbox.UI.BackgroundRepeat.Repeat;
					return true;

				case "round":
				case "clamp":
					MaskRepeat = Sandbox.UI.BackgroundRepeat.Clamp;
					return true;
			}

			return false;
		}

		bool SetBackgroundRepeat( string value )
		{
			switch ( value )
			{
				case "no-repeat":
					BackgroundRepeat = Sandbox.UI.BackgroundRepeat.NoRepeat;
					return true;

				case "repeat-x":
					BackgroundRepeat = Sandbox.UI.BackgroundRepeat.RepeatX;
					return true;

				case "repeat-y":
					BackgroundRepeat = Sandbox.UI.BackgroundRepeat.RepeatY;
					return true;

				case "repeat":
					BackgroundRepeat = Sandbox.UI.BackgroundRepeat.Repeat;
					return true;

				case "round":
				case "clamp":
					BackgroundRepeat = Sandbox.UI.BackgroundRepeat.Clamp;
					return true;
			}

			return false;
		}

		bool SetTextGradientLinear( string gradient )
		{
			TextGradient = new();
			TextGradient.GradientType = GradientInfo.GradientTypes.Linear;

			var p = new Parse( gradient );
			p.SkipWhitespaceAndNewlines();

			var restoreP = p;

			if ( !p.TryReadColor( out var _ ) )
			{
				p = restoreP;

				var angle = p.ReadUntilOrEnd( ",", true );
				p.SkipWhitespaceAndNewlines( "," );

				if ( !string.IsNullOrEmpty( angle ) )
				{
					SetTextGradientAngle( angle );
				}
			}
			else
			{
				p = restoreP;
			}

			var colors = p.ReadRemaining();
			var gradientData = ParseGradient( colors );

			TextGradient.ColorOffsets = ImmutableArray.Create<GradientColorOffset>();
			foreach ( var gen in gradientData )
			{
				TextGradient.ColorOffsets = TextGradient.ColorOffsets.Add( gen.from );
				TextGradient.ColorOffsets = TextGradient.ColorOffsets.Add( gen.to );
			}

			return true;
		}

		bool SetTextGradientRadial( string gradient )
		{
			TextGradient = new();
			TextGradient.OffsetX = Length.Percent( 50 ).Value;
			TextGradient.OffsetY = Length.Percent( 50 ).Value;
			TextGradient.GradientType = GradientInfo.GradientTypes.Radial;
			TextGradient.SizeMode = GradientInfo.RadialSizeMode.FarthestSide;

			var p = new Parse( gradient );
			p.SkipWhitespaceAndNewlines();

			var restoreP = p;

			if ( !p.TryReadColor( out var _ ) )
			{
				p = restoreP;

				var sizemode = p.ReadUntilOrEnd( ", ", true );
				p.SkipWhitespaceAndNewlines();
				var position = p.ReadUntilOrEnd( ",", true );
				p.SkipWhitespaceAndNewlines( "," );

				if ( !string.IsNullOrEmpty( sizemode ) )
				{
					SetTextGradientSizeMode( sizemode );
				}

				if ( !string.IsNullOrEmpty( position ) )
				{
					SetTextGradientPosition( position );
				}
			}
			else
			{
				p = restoreP;
			}

			var colors = p.ReadRemaining();
			var gradientData = ParseGradient( colors );

			TextGradient.ColorOffsets = ImmutableArray.Create<GradientColorOffset>();
			foreach ( var gen in gradientData )
			{
				TextGradient.ColorOffsets = TextGradient.ColorOffsets.Add( gen.from );
				TextGradient.ColorOffsets = TextGradient.ColorOffsets.Add( gen.to );
			}

			return true;
		}

		bool SetTextGradientPosition( string value )
		{
			var p = new Parse( value );

			if ( p.Is( "at " ) )
				p.Pointer += 3;

			if ( p.TryReadLength( out var lenx ) )
			{
				TextGradient.OffsetX = lenx;
				TextGradient.OffsetY = lenx;

				if ( p.TryReadLength( out var leny ) )
				{
					TextGradient.OffsetY = leny;
				}
				return true;
			}

			return false;
		}

		bool SetTextGradientSizeMode( string value )
		{
			switch ( value )
			{
				case "circle":
					TextGradient.SizeMode = GradientInfo.RadialSizeMode.Circle;
					return true;
				case "closest-corner":
					TextGradient.SizeMode = GradientInfo.RadialSizeMode.ClosestCorner;
					return true;
				case "closest-side":
					TextGradient.SizeMode = GradientInfo.RadialSizeMode.ClosestSide;
					return true;
				case "farthest-corner":
					TextGradient.SizeMode = GradientInfo.RadialSizeMode.FarthestCorner;
					return true;
				case "farthest-side":
					TextGradient.SizeMode = GradientInfo.RadialSizeMode.FarthestSide;
					return true;
			}

			return false;
		}

		bool SetTextGradientAngle( string value )
		{
			var angle = GetAngleInDegrees( value );

			if ( !angle.HasValue ) return false;

			var angler = angle.Value;
			TextGradient.Angle = angler.Value;

			return true;
		}

		bool SetFontSmooth( string value )
		{
			value = value.Trim();

			if ( Enum.TryParse<FontSmooth>( value, true, out var fontSmooth ) )
			{
				FontSmooth = fontSmooth;
				return true;
			}

			return false;
		}

		bool SetObjectFit( string value )
		{
			value = value.Trim();

			if ( Enum.TryParse<ObjectFit>( value, true, out var objectFit ) )
			{
				ObjectFit = objectFit;
				return true;
			}

			return false;
		}

		Length? GetAngleInDegrees( string value )
		{
			var p = new Parse( value );

			p.SkipWhitespaceAndNewlines();

			//
			// https://www.w3.org/TR/css-images-3/#linear-gradient-syntax
			// top/bottom are flipped in order to match css spec, our coordinate systems differ
			// from browser implementations
			//
			Dictionary<string, float> directions = new Dictionary<string, float>()
			{
				{ "bottom", 0 },
				{ "right", 90 },
				{ "top", 180 },
				{ "left", 270 }
			};

			Length? result = null;
			if ( p.Is( "to ", 0, true ) )
			{
				p.Pointer += 3;
				p.SkipWhitespaceAndNewlines();

				foreach ( var (name, angle) in directions )
				{
					if ( p.Is( name, 0, true ) )
						return angle;
				}
			}

			var lastP = p;

			// We only want to fetch specific units from here, otherwise we'll fallback to parsing other units
			if ( result == null && p.TryReadLength( out var lenx ) )
			{
				// If it's pixels, lets check the other units
				if ( lenx.Unit != LengthUnit.Pixels )
					return lenx;
			}

			// Still not found
			if ( result == null )
			{
				// Reset our pointer and try and parse with units
				p = lastP;
				if ( p.TryReadFloat( out var num ) )
				{
					var unit = "deg";
					if ( p.IsLetter ) unit = p.ReadUntilWhitespaceOrNewlineOrEnd( "," );

					// CSS angles - +x is assumed to be 0 degrees, whereas we would assume +y is 0 degrees,
					// so we add 90deg here in order to match the CSS spec.
					return StyleHelpers.RotationDegrees( num, unit ) + 90f;
				}
			}

			return null;
		}

	}
}
