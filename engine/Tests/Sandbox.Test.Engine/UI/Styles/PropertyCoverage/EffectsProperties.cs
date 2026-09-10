using Sandbox.UI;

namespace UITests.PropertyCoverage;

/// <summary>
/// Exhaustive property coverage for "effects" CSS properties:
/// filter / backdrop-filter (+ longhands), box-shadow / text-shadow / filter-drop-shadow,
/// mask (+ longhands), object-fit, image-rendering.
/// </summary>
[TestClass]
public class EffectsPropertiesTest
{
	// ─────────────────────────────────────────────────────────────────────────
	// filter (shorthand → FilterXXX longhands)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void FilterBlur()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "blur(4px)" ) );
		Assert.IsTrue( s.FilterBlur.HasValue );
		Assert.AreEqual( 4, s.FilterBlur.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FilterBlur.Value.Unit );
	}

	[TestMethod]
	public void FilterBrightness()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "brightness(0.5)" ) );
		Assert.IsTrue( s.FilterBrightness.HasValue );
		Assert.AreEqual( 0.5f, s.FilterBrightness.Value.Value );
		// unitless number → pixels
		Assert.AreEqual( LengthUnit.Pixels, s.FilterBrightness.Value.Unit );
	}

	[TestMethod]
	public void FilterBrightnessPercent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "brightness(50%)" ) );
		Assert.IsTrue( s.FilterBrightness.HasValue );
		Assert.AreEqual( 50, s.FilterBrightness.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.FilterBrightness.Value.Unit );
	}

	[TestMethod]
	public void FilterSaturate()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "saturate(2)" ) );
		Assert.IsTrue( s.FilterSaturate.HasValue );
		Assert.AreEqual( 2, s.FilterSaturate.Value.Value );
	}

	[TestMethod]
	public void FilterContrast()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "contrast(1.5)" ) );
		Assert.IsTrue( s.FilterContrast.HasValue );
		Assert.AreEqual( 1.5f, s.FilterContrast.Value.Value );
	}

	[TestMethod]
	public void FilterSepia()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "sepia(0.8)" ) );
		Assert.IsTrue( s.FilterSepia.HasValue );
		Assert.AreEqual( 0.8f, s.FilterSepia.Value.Value );
	}

	[TestMethod]
	public void FilterInvert()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "invert(1)" ) );
		Assert.IsTrue( s.FilterInvert.HasValue );
		Assert.AreEqual( 1, s.FilterInvert.Value.Value );
	}

	[TestMethod]
	public void FilterHueRotate()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "hue-rotate(90deg)" ) );
		Assert.IsTrue( s.FilterHueRotate.HasValue );
		// 90deg parses to 90 pixels (deg → Length.Pixels, see Length.Parse)
		Assert.AreEqual( 90, s.FilterHueRotate.Value.Value );
	}

	[TestMethod]
	public void FilterGrayscale()
	{
		// grayscale(x) is stored in FilterSaturate as (1 - x)
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "grayscale(1)" ) );
		Assert.IsTrue( s.FilterSaturate.HasValue );
		Assert.AreEqual( 0, s.FilterSaturate.Value.Value );

		s = new Styles();
		Assert.IsTrue( s.Set( "filter", "grayscale(0)" ) );
		Assert.IsTrue( s.FilterSaturate.HasValue );
		Assert.AreEqual( 1, s.FilterSaturate.Value.Value );
	}

	[TestMethod]
	public void FilterTint()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "tint(red)" ) );
		Assert.IsTrue( s.FilterTint.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.FilterTint.Value );
	}

	[TestMethod]
	public void FilterDropShadow()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "drop-shadow(2px 2px red)" ) );
		Assert.AreEqual( 1, s.FilterDropShadow.Count );
		Assert.AreEqual( 2, s.FilterDropShadow[0].OffsetX );
		Assert.AreEqual( 2, s.FilterDropShadow[0].OffsetY );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.FilterDropShadow[0].Color );
	}

	[TestMethod]
	public void FilterDropShadowWithBlur()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "drop-shadow(2px 4px 6px black)" ) );
		Assert.AreEqual( 1, s.FilterDropShadow.Count );
		Assert.AreEqual( 2, s.FilterDropShadow[0].OffsetX );
		Assert.AreEqual( 4, s.FilterDropShadow[0].OffsetY );
		Assert.AreEqual( 6, s.FilterDropShadow[0].Blur );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), s.FilterDropShadow[0].Color );
	}

	[TestMethod]
	public void FilterCombination()
	{
		// Multiple filter functions applied at once should each map to their longhand.
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter", "blur(2px) brightness(0.5) saturate(3)" ) );

		Assert.IsTrue( s.FilterBlur.HasValue );
		Assert.AreEqual( 2, s.FilterBlur.Value.Value );

		Assert.IsTrue( s.FilterBrightness.HasValue );
		Assert.AreEqual( 0.5f, s.FilterBrightness.Value.Value );

		Assert.IsTrue( s.FilterSaturate.HasValue );
		Assert.AreEqual( 3, s.FilterSaturate.Value.Value );
	}

	[TestMethod]
	public void FilterInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "filter", "bullshit(4px)" ) );
	}

	/// <summary>
	/// 'filter: none' clears any filters.
	/// </summary>
	[TestMethod]
	public void FilterNone()
	{
		var s = new Styles();
		s.Set( "filter", "blur(4px)" );
		Assert.IsTrue( s.Set( "filter", "none" ) );
		Assert.AreEqual( 0, s.FilterBlur.Value.Value ); // reset to no blur (non-null so it overrides a base rule)
	}

	// ─────────────────────────────────────────────────────────────────────────
	// filter longhands (parsed directly via SetGenerated)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void FilterBlurLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-blur", "8px" ) );
		Assert.IsTrue( s.FilterBlur.HasValue );
		Assert.AreEqual( 8, s.FilterBlur.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FilterBlur.Value.Unit );
	}

	[TestMethod]
	public void FilterSaturateLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-saturate", "2" ) );
		Assert.IsTrue( s.FilterSaturate.HasValue );
		Assert.AreEqual( 2, s.FilterSaturate.Value.Value );
	}

	[TestMethod]
	public void FilterSepiaLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-sepia", "0.5" ) );
		Assert.IsTrue( s.FilterSepia.HasValue );
		Assert.AreEqual( 0.5f, s.FilterSepia.Value.Value );
	}

	[TestMethod]
	public void FilterBrightnessLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-brightness", "1.2" ) );
		Assert.IsTrue( s.FilterBrightness.HasValue );
		Assert.AreEqual( 1.2f, s.FilterBrightness.Value.Value );
	}

	[TestMethod]
	public void FilterContrastLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-contrast", "2" ) );
		Assert.IsTrue( s.FilterContrast.HasValue );
		Assert.AreEqual( 2, s.FilterContrast.Value.Value );
	}

	[TestMethod]
	public void FilterHueRotateLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-hue-rotate", "45px" ) );
		Assert.IsTrue( s.FilterHueRotate.HasValue );
		Assert.AreEqual( 45, s.FilterHueRotate.Value.Value );
	}

	[TestMethod]
	public void FilterInvertLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-invert", "1" ) );
		Assert.IsTrue( s.FilterInvert.HasValue );
		Assert.AreEqual( 1, s.FilterInvert.Value.Value );
	}

	[TestMethod]
	public void FilterTintLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-tint", "#00ff00" ) );
		Assert.IsTrue( s.FilterTint.HasValue );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.FilterTint.Value );
	}

	[TestMethod]
	public void FilterBorderWidthLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-border-width", "3px" ) );
		Assert.IsTrue( s.FilterBorderWidth.HasValue );
		Assert.AreEqual( 3, s.FilterBorderWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FilterBorderWidth.Value.Unit );
	}

	[TestMethod]
	public void FilterBorderColorLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-border-color", "blue" ) );
		Assert.IsTrue( s.FilterBorderColor.HasValue );
		Assert.AreEqual( new Color( 0, 0, 1, 1 ), s.FilterBorderColor.Value );
	}

	[TestMethod]
	public void FilterDropShadowLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "filter-drop-shadow", "5px 6px 7px red" ) );
		Assert.AreEqual( 1, s.FilterDropShadow.Count );
		Assert.AreEqual( 5, s.FilterDropShadow[0].OffsetX );
		Assert.AreEqual( 6, s.FilterDropShadow[0].OffsetY );
		Assert.AreEqual( 7, s.FilterDropShadow[0].Blur );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.FilterDropShadow[0].Color );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// backdrop-filter (shorthand → BackdropFilterXXX longhands)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void BackdropFilterBlur()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "blur(10px)" ) );
		Assert.IsTrue( s.BackdropFilterBlur.HasValue );
		Assert.AreEqual( 10, s.BackdropFilterBlur.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackdropFilterBlur.Value.Unit );
	}

	[TestMethod]
	public void BackdropFilterBrightness()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "brightness(0.5)" ) );
		Assert.IsTrue( s.BackdropFilterBrightness.HasValue );
		Assert.AreEqual( 0.5f, s.BackdropFilterBrightness.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterContrast()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "contrast(2)" ) );
		Assert.IsTrue( s.BackdropFilterContrast.HasValue );
		Assert.AreEqual( 2, s.BackdropFilterContrast.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterSaturate()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "saturate(3)" ) );
		Assert.IsTrue( s.BackdropFilterSaturate.HasValue );
		Assert.AreEqual( 3, s.BackdropFilterSaturate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterSepia()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "sepia(0.4)" ) );
		Assert.IsTrue( s.BackdropFilterSepia.HasValue );
		Assert.AreEqual( 0.4f, s.BackdropFilterSepia.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterInvert()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "invert(1)" ) );
		Assert.IsTrue( s.BackdropFilterInvert.HasValue );
		Assert.AreEqual( 1, s.BackdropFilterInvert.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterHueRotate()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "hue-rotate(90deg)" ) );
		Assert.IsTrue( s.BackdropFilterHueRotate.HasValue );
		Assert.AreEqual( 90, s.BackdropFilterHueRotate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterGrayscale()
	{
		// grayscale(x) is stored in BackdropFilterSaturate as (1 - x)
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "grayscale(1)" ) );
		Assert.IsTrue( s.BackdropFilterSaturate.HasValue );
		Assert.AreEqual( 0, s.BackdropFilterSaturate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterCombination()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter", "blur(4px) saturate(2)" ) );

		Assert.IsTrue( s.BackdropFilterBlur.HasValue );
		Assert.AreEqual( 4, s.BackdropFilterBlur.Value.Value );

		Assert.IsTrue( s.BackdropFilterSaturate.HasValue );
		Assert.AreEqual( 2, s.BackdropFilterSaturate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "backdrop-filter", "bullshit(4px)" ) );
	}

	// backdrop-filter longhands

	[TestMethod]
	public void BackdropFilterBlurLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-blur", "12px" ) );
		Assert.IsTrue( s.BackdropFilterBlur.HasValue );
		Assert.AreEqual( 12, s.BackdropFilterBlur.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterBrightnessLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-brightness", "0.75" ) );
		Assert.IsTrue( s.BackdropFilterBrightness.HasValue );
		Assert.AreEqual( 0.75f, s.BackdropFilterBrightness.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterContrastLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-contrast", "1.5" ) );
		Assert.IsTrue( s.BackdropFilterContrast.HasValue );
		Assert.AreEqual( 1.5f, s.BackdropFilterContrast.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterSaturateLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-saturate", "2" ) );
		Assert.IsTrue( s.BackdropFilterSaturate.HasValue );
		Assert.AreEqual( 2, s.BackdropFilterSaturate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterSepiaLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-sepia", "0.3" ) );
		Assert.IsTrue( s.BackdropFilterSepia.HasValue );
		Assert.AreEqual( 0.3f, s.BackdropFilterSepia.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterInvertLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-invert", "1" ) );
		Assert.IsTrue( s.BackdropFilterInvert.HasValue );
		Assert.AreEqual( 1, s.BackdropFilterInvert.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterHueRotateLonghand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "backdrop-filter-hue-rotate", "30px" ) );
		Assert.IsTrue( s.BackdropFilterHueRotate.HasValue );
		Assert.AreEqual( 30, s.BackdropFilterHueRotate.Value.Value );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// box-shadow (ShadowList)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void BoxShadowOffsetOnly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "10px 20px red" ) );
		Assert.AreEqual( 1, s.BoxShadow.Count );
		Assert.AreEqual( 10, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( 0, s.BoxShadow[0].Blur );
		Assert.AreEqual( 0, s.BoxShadow[0].Spread );
		Assert.IsFalse( s.BoxShadow[0].Inset );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );
	}

	[TestMethod]
	public void BoxShadowWithBlur()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "10px 20px 30px red" ) );
		Assert.AreEqual( 1, s.BoxShadow.Count );
		Assert.AreEqual( 10, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, s.BoxShadow[0].Blur );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );
	}

	[TestMethod]
	public void BoxShadowWithSpread()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "10px 20px 30px 40px red" ) );
		Assert.AreEqual( 1, s.BoxShadow.Count );
		Assert.AreEqual( 10, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, s.BoxShadow[0].Blur );
		Assert.AreEqual( 40, s.BoxShadow[0].Spread );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );
	}

	[TestMethod]
	public void BoxShadowTrailingInset()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "10px 20px 30px 40px red inset" ) );
		Assert.AreEqual( 1, s.BoxShadow.Count );
		Assert.IsTrue( s.BoxShadow[0].Inset );
		Assert.AreEqual( 10, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, s.BoxShadow[0].Blur );
		Assert.AreEqual( 40, s.BoxShadow[0].Spread );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );
	}

	[TestMethod]
	public void BoxShadowLeadingInset()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "inset 10px 20px 30px 40px red" ) );
		Assert.AreEqual( 1, s.BoxShadow.Count );
		Assert.IsTrue( s.BoxShadow[0].Inset );
		Assert.AreEqual( 10, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, s.BoxShadow[0].Blur );
		Assert.AreEqual( 40, s.BoxShadow[0].Spread );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );
	}

	[TestMethod]
	public void BoxShadowLeadingColor()
	{
		// Color is allowed to appear before the lengths.
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "red 1px 2px" ) );
		Assert.AreEqual( 1, s.BoxShadow.Count );
		Assert.AreEqual( 1, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 2, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );
	}

	[TestMethod]
	public void BoxShadowMultiple()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "10px 20px red, 5px 10px black" ) );
		Assert.AreEqual( 2, s.BoxShadow.Count );

		Assert.AreEqual( 10, s.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, s.BoxShadow[0].OffsetY );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BoxShadow[0].Color );

		Assert.AreEqual( 5, s.BoxShadow[1].OffsetX );
		Assert.AreEqual( 10, s.BoxShadow[1].OffsetY );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), s.BoxShadow[1].Color );
	}

	[TestMethod]
	public void BoxShadowNone()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "box-shadow", "none" ) );
		Assert.AreEqual( 0, s.BoxShadow.Count );
		Assert.IsTrue( s.BoxShadow.IsNone );
	}

	[TestMethod]
	public void BoxShadowInvalid()
	{
		// Only a single length (missing the Y offset) is invalid.
		var s = new Styles();
		Assert.IsFalse( s.Set( "box-shadow", "10px" ) );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// text-shadow (ShadowList)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void TextShadowOffsetOnly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-shadow", "2px 3px black" ) );
		Assert.AreEqual( 1, s.TextShadow.Count );
		Assert.AreEqual( 2, s.TextShadow[0].OffsetX );
		Assert.AreEqual( 3, s.TextShadow[0].OffsetY );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), s.TextShadow[0].Color );
	}

	[TestMethod]
	public void TextShadowWithBlur()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-shadow", "2px 3px 4px red" ) );
		Assert.AreEqual( 1, s.TextShadow.Count );
		Assert.AreEqual( 2, s.TextShadow[0].OffsetX );
		Assert.AreEqual( 3, s.TextShadow[0].OffsetY );
		Assert.AreEqual( 4, s.TextShadow[0].Blur );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.TextShadow[0].Color );
	}

	[TestMethod]
	public void TextShadowMultiple()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-shadow", "1px 1px black, 2px 2px red" ) );
		Assert.AreEqual( 2, s.TextShadow.Count );
		Assert.AreEqual( 1, s.TextShadow[0].OffsetX );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), s.TextShadow[0].Color );
		Assert.AreEqual( 2, s.TextShadow[1].OffsetX );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.TextShadow[1].Color );
	}

	[TestMethod]
	public void TextShadowNone()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-shadow", "none" ) );
		Assert.AreEqual( 0, s.TextShadow.Count );
		Assert.IsTrue( s.TextShadow.IsNone );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// object-fit (ObjectFit enum, Enum.TryParse ignoreCase)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void ObjectFitFill()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "object-fit", "fill" ) );
		Assert.AreEqual( ObjectFit.Fill, s.ObjectFit );
	}

	[TestMethod]
	public void ObjectFitContain()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "object-fit", "contain" ) );
		Assert.AreEqual( ObjectFit.Contain, s.ObjectFit );
	}

	[TestMethod]
	public void ObjectFitCover()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "object-fit", "cover" ) );
		Assert.AreEqual( ObjectFit.Cover, s.ObjectFit );
	}

	[TestMethod]
	public void ObjectFitNone()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "object-fit", "none" ) );
		Assert.AreEqual( ObjectFit.None, s.ObjectFit );
	}

	[TestMethod]
	public void ObjectFitInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "object-fit", "bullshit" ) );
		Assert.IsNull( s.ObjectFit );
	}

	/// <summary>
	/// object-fit: scale-down maps to contain (we have no never-upscale mode).
	/// </summary>
	[TestMethod]
	public void ObjectFitScaleDown()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "object-fit", "scale-down" ) );
		Assert.AreEqual( ObjectFit.Contain, s.ObjectFit );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// image-rendering (ImageRendering enum)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void ImageRenderingAuto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "auto" ) );
		Assert.AreEqual( ImageRendering.Anisotropic, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingAnisotropic()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "anisotropic" ) );
		Assert.AreEqual( ImageRendering.Anisotropic, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingBilinear()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "bilinear" ) );
		Assert.AreEqual( ImageRendering.Bilinear, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingTrilinear()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "trilinear" ) );
		Assert.AreEqual( ImageRendering.Trilinear, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingPoint()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "point" ) );
		Assert.AreEqual( ImageRendering.Point, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingPixelated()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "pixelated" ) );
		Assert.AreEqual( ImageRendering.Point, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingNearestNeighbor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "nearest-neighbor" ) );
		Assert.AreEqual( ImageRendering.Point, s.ImageRendering );
	}

	[TestMethod]
	public void ImageRenderingInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "image-rendering", "bullshit" ) );
		Assert.IsNull( s.ImageRendering );
	}

	/// <summary>
	/// 'crisp-edges' maps to point/nearest sampling.
	/// </summary>
	[TestMethod]
	public void ImageRenderingCrispEdges()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "image-rendering", "crisp-edges" ) );
		Assert.AreEqual( ImageRendering.Point, s.ImageRendering );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask shorthand (SetMask)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskImageUrl()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask", "url(mask.png)" ) );
	}

	[TestMethod]
	public void MaskWithMode()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask", "url(mask.png) luminance" ) );
		Assert.AreEqual( MaskMode.Luminance, s.MaskMode );
	}

	[TestMethod]
	public void MaskWithRepeat()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask", "url(mask.png) no-repeat" ) );
		Assert.AreEqual( BackgroundRepeat.NoRepeat, s.MaskRepeat );
	}

	[TestMethod]
	public void MaskWithPosition()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask", "url(mask.png) 100px 200px" ) );
		Assert.IsTrue( s.MaskPositionX.HasValue );
		Assert.IsTrue( s.MaskPositionY.HasValue );
		Assert.AreEqual( 100, s.MaskPositionX.Value.Value );
		Assert.AreEqual( 200, s.MaskPositionY.Value.Value );
	}

	/// <summary>
	/// 'mask: url(...) 10px 20px/30px 40px' - position 10/20 and size 30/40 (space-less '/').
	/// </summary>
	[TestMethod]
	public void MaskWithPositionAndSize()
	{
		var s = new Styles();
		s.Set( "mask", "url(mask.png) 10px 20px/30px 40px" );
		Assert.IsTrue( s.MaskPositionX.HasValue );
		Assert.AreEqual( 10, s.MaskPositionX.Value.Value );
		Assert.IsTrue( s.MaskSizeX.HasValue, "mask size after / not parsed" );
		Assert.IsTrue( s.MaskSizeY.HasValue );
		Assert.AreEqual( 30, s.MaskSizeX.Value.Value );
		Assert.AreEqual( 40, s.MaskSizeY.Value.Value );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-image (SetImage)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskImageLonghandUrl()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-image", "url(mask.png)" ) );
	}

	// Note: mask-image: none is intentionally NOT tested - the "none" path eagerly evaluates
	// Texture.Invalid, which needs a render device the headless test host lacks and crashes the process.

	[TestMethod]
	public void MaskImageInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "mask-image", "bullshit" ) );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-mode (MaskMode enum)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskModeMatchSource()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-mode", "match-source" ) );
		Assert.AreEqual( MaskMode.MatchSource, s.MaskMode );
	}

	[TestMethod]
	public void MaskModeAlpha()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-mode", "alpha" ) );
		Assert.AreEqual( MaskMode.Alpha, s.MaskMode );
	}

	[TestMethod]
	public void MaskModeLuminance()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-mode", "luminance" ) );
		Assert.AreEqual( MaskMode.Luminance, s.MaskMode );
	}

	[TestMethod]
	public void MaskModeInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "mask-mode", "bullshit" ) );
		Assert.IsNull( s.MaskMode );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-repeat (BackgroundRepeat enum)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskRepeatNoRepeat()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-repeat", "no-repeat" ) );
		Assert.AreEqual( BackgroundRepeat.NoRepeat, s.MaskRepeat );
	}

	[TestMethod]
	public void MaskRepeatRepeat()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-repeat", "repeat" ) );
		Assert.AreEqual( BackgroundRepeat.Repeat, s.MaskRepeat );
	}

	[TestMethod]
	public void MaskRepeatRepeatX()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-repeat", "repeat-x" ) );
		Assert.AreEqual( BackgroundRepeat.RepeatX, s.MaskRepeat );
	}

	[TestMethod]
	public void MaskRepeatRepeatY()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-repeat", "repeat-y" ) );
		Assert.AreEqual( BackgroundRepeat.RepeatY, s.MaskRepeat );
	}

	[TestMethod]
	public void MaskRepeatRound()
	{
		// 'round' and 'clamp' both map to Clamp.
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-repeat", "round" ) );
		Assert.AreEqual( BackgroundRepeat.Clamp, s.MaskRepeat );
	}

	[TestMethod]
	public void MaskRepeatInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "mask-repeat", "bullshit" ) );
		Assert.IsNull( s.MaskRepeat );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-size / mask-size-x / mask-size-y
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskSizeSingle()
	{
		// One value applies to both X and Y.
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-size", "50px" ) );
		Assert.IsTrue( s.MaskSizeX.HasValue );
		Assert.IsTrue( s.MaskSizeY.HasValue );
		Assert.AreEqual( 50, s.MaskSizeX.Value.Value );
		Assert.AreEqual( 50, s.MaskSizeY.Value.Value );
	}

	[TestMethod]
	public void MaskSizeTwoValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-size", "50px 75px" ) );
		Assert.AreEqual( 50, s.MaskSizeX.Value.Value );
		Assert.AreEqual( 75, s.MaskSizeY.Value.Value );
	}

	[TestMethod]
	public void MaskSizeX()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-size-x", "60px" ) );
		Assert.IsTrue( s.MaskSizeX.HasValue );
		Assert.AreEqual( 60, s.MaskSizeX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MaskSizeX.Value.Unit );
	}

	[TestMethod]
	public void MaskSizeY()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-size-y", "70px" ) );
		Assert.IsTrue( s.MaskSizeY.HasValue );
		Assert.AreEqual( 70, s.MaskSizeY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MaskSizeY.Value.Unit );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-position / mask-position-x / mask-position-y
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskPositionSingle()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-position", "25px" ) );
		Assert.IsTrue( s.MaskPositionX.HasValue );
		Assert.IsTrue( s.MaskPositionY.HasValue );
		Assert.AreEqual( 25, s.MaskPositionX.Value.Value );
		Assert.AreEqual( 25, s.MaskPositionY.Value.Value );
	}

	[TestMethod]
	public void MaskPositionTwoValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-position", "10px 20px" ) );
		Assert.AreEqual( 10, s.MaskPositionX.Value.Value );
		Assert.AreEqual( 20, s.MaskPositionY.Value.Value );
	}

	[TestMethod]
	public void MaskPositionX()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-position-x", "15px" ) );
		Assert.IsTrue( s.MaskPositionX.HasValue );
		Assert.AreEqual( 15, s.MaskPositionX.Value.Value );
	}

	[TestMethod]
	public void MaskPositionY()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-position-y", "35px" ) );
		Assert.IsTrue( s.MaskPositionY.HasValue );
		Assert.AreEqual( 35, s.MaskPositionY.Value.Value );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-angle (Length?)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskAngle()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-angle", "45px" ) );
		Assert.IsTrue( s.MaskAngle.HasValue );
		Assert.AreEqual( 45, s.MaskAngle.Value.Value );
	}

	// ─────────────────────────────────────────────────────────────────────────
	// mask-scope (MaskScope enum)
	// ─────────────────────────────────────────────────────────────────────────

	[TestMethod]
	public void MaskScopeDefault()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-scope", "default" ) );
		Assert.AreEqual( MaskScope.Default, s.MaskScope );
	}

	[TestMethod]
	public void MaskScopeFilter()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mask-scope", "filter" ) );
		Assert.AreEqual( MaskScope.Filter, s.MaskScope );
	}

	[TestMethod]
	public void MaskScopeInvalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "mask-scope", "bullshit" ) );
		Assert.IsNull( s.MaskScope );
	}
}
