using Sandbox.UI;

namespace UITest.PropertyCoverage;

[TestClass]
public class BackgroundProperties
{
	// ---------------------------------------------------------------------
	// background-color
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundColor_Hex()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "#ff0000" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void BackgroundColor_ShortHexWithAlpha()
	{
		// #f0f0 = r=1 g=0 b=1 a=0  (matches StyleSetting.SetBackgroundColor)
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "#f0f0" ) );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.g );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.b );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.a );
	}

	[TestMethod]
	public void BackgroundColor_Rgb()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "rgb( 255, 0, 0 )" ) );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void BackgroundColor_Rgba()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "rgba( 0, 0, 0, 0.5 )" ) );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 0, 0, 0, 0.5f ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void BackgroundColor_Named()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "red" ) );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void BackgroundColor_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-color", "notacolor" ) );
		Assert.IsFalse( s.BackgroundColor.HasValue );
	}

	// ---------------------------------------------------------------------
	// background (shorthand) - color forms
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Background_HexColor()
	{
		// SetBackground routes bgSource starting with '#' to BackgroundColor
		var s = new Styles();
		Assert.IsTrue( s.Set( "background", "#ff0000" ) );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Background_RgbColor()
	{
		// SetBackground routes bgSource starting with 'rgb(' to BackgroundColor
		var s = new Styles();
		Assert.IsTrue( s.Set( "background", "rgb(0,255,0)" ) );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Background_UrlImage()
	{
		// Image forms: assert ok only (texture result is hard to assert)
		var s = new Styles();
		Assert.IsTrue( s.Set( "background", "url(/x.png)" ) );
	}

	[TestMethod]
	public void Background_RepeatToken()
	{
		// background shorthand should pick up a <repeat-style> token
		var s = new Styles();
		Assert.IsTrue( s.Set( "background", "url(/x.png) no-repeat" ) );
		Assert.AreEqual( BackgroundRepeat.NoRepeat, s.BackgroundRepeat );
	}

	[TestMethod]
	public void Background_PositionAndSize()
	{
		// background shorthand: a single length becomes both position X and Y,
		// a second length becomes both size X and Y (see SetBackground length list).
		var s = new Styles();
		Assert.IsTrue( s.Set( "background", "url(/x.png) 10px 20px" ) );

		Assert.IsTrue( s.BackgroundPositionX.HasValue );
		Assert.IsTrue( s.BackgroundPositionY.HasValue );
		Assert.AreEqual( 10, s.BackgroundPositionX.Value.Value );
		Assert.AreEqual( 10, s.BackgroundPositionY.Value.Value );

		Assert.IsTrue( s.BackgroundSizeX.HasValue );
		Assert.IsTrue( s.BackgroundSizeY.HasValue );
		Assert.AreEqual( 20, s.BackgroundSizeX.Value.Value );
		Assert.AreEqual( 20, s.BackgroundSizeY.Value.Value );
	}

	// ---------------------------------------------------------------------
	// background-image
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundImage_Url()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-image", "url(/x.png)" ) );
	}

	[TestMethod]
	public void BackgroundImage_UrlQuoted()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-image", "url( \"/ui/test.png\" )" ) );
	}

	// Note: gradient values (linear/radial/conic) and "none" on background-image are intentionally
	// NOT tested here. Unlike url() (wrapped in a Lazy<Texture>), gradients build a real GPU texture
	// immediately and "none" eagerly evaluates Texture.Invalid - both need a render device that the
	// headless test host doesn't have, so they crash the process.

	[TestMethod]
	public void BackgroundImage_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-image", "garbage" ) );
	}

	// ---------------------------------------------------------------------
	// background-size  /  background-size-x / -y
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundSize_Single()
	{
		// One length sets both X and Y
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-size", "50px" ) );
		Assert.IsTrue( s.BackgroundSizeX.HasValue );
		Assert.IsTrue( s.BackgroundSizeY.HasValue );
		Assert.AreEqual( 50, s.BackgroundSizeX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundSizeX.Value.Unit );
		Assert.AreEqual( 50, s.BackgroundSizeY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundSizeY.Value.Unit );
	}

	[TestMethod]
	public void BackgroundSize_TwoValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-size", "50px 80px" ) );
		Assert.AreEqual( 50, s.BackgroundSizeX.Value.Value );
		Assert.AreEqual( 80, s.BackgroundSizeY.Value.Value );
	}

	[TestMethod]
	public void BackgroundSize_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-size", "100%" ) );
		Assert.IsTrue( s.BackgroundSizeX.HasValue );
		Assert.AreEqual( 100, s.BackgroundSizeX.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.BackgroundSizeX.Value.Unit );
		Assert.AreEqual( 100, s.BackgroundSizeY.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.BackgroundSizeY.Value.Unit );
	}

	[TestMethod]
	public void BackgroundSizeX_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-size-x", "30px" ) );
		Assert.IsTrue( s.BackgroundSizeX.HasValue );
		Assert.AreEqual( 30, s.BackgroundSizeX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundSizeX.Value.Unit );
		Assert.IsFalse( s.BackgroundSizeY.HasValue );
	}

	[TestMethod]
	public void BackgroundSizeY_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-size-y", "30px" ) );
		Assert.IsTrue( s.BackgroundSizeY.HasValue );
		Assert.AreEqual( 30, s.BackgroundSizeY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundSizeY.Value.Unit );
		Assert.IsFalse( s.BackgroundSizeX.HasValue );
	}

	[TestMethod]
	public void BackgroundSizeX_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-size-x", "notalength" ) );
	}

	// ---------------------------------------------------------------------
	// background-position  /  background-position-x / -y
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundPosition_Single()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-position", "12px" ) );
		Assert.IsTrue( s.BackgroundPositionX.HasValue );
		Assert.IsTrue( s.BackgroundPositionY.HasValue );
		Assert.AreEqual( 12, s.BackgroundPositionX.Value.Value );
		Assert.AreEqual( 12, s.BackgroundPositionY.Value.Value );
	}

	[TestMethod]
	public void BackgroundPosition_TwoValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-position", "10px 25px" ) );
		Assert.AreEqual( 10, s.BackgroundPositionX.Value.Value );
		Assert.AreEqual( 25, s.BackgroundPositionY.Value.Value );
	}

	[TestMethod]
	public void BackgroundPosition_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-position", "50%" ) );
		Assert.IsTrue( s.BackgroundPositionX.HasValue );
		Assert.AreEqual( 50, s.BackgroundPositionX.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.BackgroundPositionX.Value.Unit );
	}

	[TestMethod]
	public void BackgroundPositionX_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-position-x", "8px" ) );
		Assert.IsTrue( s.BackgroundPositionX.HasValue );
		Assert.AreEqual( 8, s.BackgroundPositionX.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundPositionX.Value.Unit );
		Assert.IsFalse( s.BackgroundPositionY.HasValue );
	}

	[TestMethod]
	public void BackgroundPositionY_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-position-y", "8px" ) );
		Assert.IsTrue( s.BackgroundPositionY.HasValue );
		Assert.AreEqual( 8, s.BackgroundPositionY.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundPositionY.Value.Unit );
		Assert.IsFalse( s.BackgroundPositionX.HasValue );
	}

	[TestMethod]
	public void BackgroundPositionX_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-position-x", "notalength" ) );
	}

	// ---------------------------------------------------------------------
	// background-repeat
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundRepeat_NoRepeat()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-repeat", "no-repeat" ) );
		Assert.AreEqual( BackgroundRepeat.NoRepeat, s.BackgroundRepeat );
	}

	[TestMethod]
	public void BackgroundRepeat_RepeatX()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-repeat", "repeat-x" ) );
		Assert.AreEqual( BackgroundRepeat.RepeatX, s.BackgroundRepeat );
	}

	[TestMethod]
	public void BackgroundRepeat_RepeatY()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-repeat", "repeat-y" ) );
		Assert.AreEqual( BackgroundRepeat.RepeatY, s.BackgroundRepeat );
	}

	[TestMethod]
	public void BackgroundRepeat_Repeat()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-repeat", "repeat" ) );
		Assert.AreEqual( BackgroundRepeat.Repeat, s.BackgroundRepeat );
	}

	[TestMethod]
	public void BackgroundRepeat_Round()
	{
		// 'round' maps to Clamp in SetBackgroundRepeat
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-repeat", "round" ) );
		Assert.AreEqual( BackgroundRepeat.Clamp, s.BackgroundRepeat );
	}

	[TestMethod]
	public void BackgroundRepeat_Clamp()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-repeat", "clamp" ) );
		Assert.AreEqual( BackgroundRepeat.Clamp, s.BackgroundRepeat );
	}

	[TestMethod]
	public void BackgroundRepeat_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-repeat", "diagonal" ) );
	}

	// ---------------------------------------------------------------------
	// background-angle
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundAngle_Degrees()
	{
		// background-angle longhand goes through Length.Parse (generated setter).
		// Note: Length.Parse treats a "deg" suffix as Pixels (it has no Degrees unit),
		// so the value is preserved (45) but the unit is Pixels.
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-angle", "45deg" ) );
		Assert.IsTrue( s.BackgroundAngle.HasValue );
		Assert.AreEqual( 45, s.BackgroundAngle.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundAngle.Value.Unit );
	}

	[TestMethod]
	public void BackgroundAngle_BareNumber()
	{
		// A bare number is parsed by Length.Parse as Pixels.
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-angle", "90" ) );
		Assert.IsTrue( s.BackgroundAngle.HasValue );
		Assert.AreEqual( 90, s.BackgroundAngle.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BackgroundAngle.Value.Unit );
	}

	[TestMethod]
	public void BackgroundAngle_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-angle", "notanangle" ) );
	}

	// ---------------------------------------------------------------------
	// background-tint
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundTint_Hex()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-tint", "#00ff00" ) );
		Assert.IsTrue( s.BackgroundTint.HasValue );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.BackgroundTint.Value );
	}

	[TestMethod]
	public void BackgroundTint_Rgba()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-tint", "rgba( 255, 255, 255, 0.25 )" ) );
		Assert.IsTrue( s.BackgroundTint.HasValue );
		Assert.AreEqual( new Color( 1, 1, 1, 0.25f ), s.BackgroundTint.Value );
	}

	[TestMethod]
	public void BackgroundTint_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "background-tint", "notacolor" ) );
	}

	// ---------------------------------------------------------------------
	// background-blend-mode
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundBlendMode_Multiply()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-blend-mode", "multiply" ) );
		Assert.AreEqual( "multiply", s.BackgroundBlendMode );
	}

	[TestMethod]
	public void BackgroundBlendMode_Screen()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-blend-mode", "screen" ) );
		Assert.AreEqual( "screen", s.BackgroundBlendMode );
	}

	// ---------------------------------------------------------------------
	// background-playback-state
	// ---------------------------------------------------------------------

	[TestMethod]
	public void BackgroundPlaybackState_Paused()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-playback-state", "paused" ) );
		Assert.IsTrue( s.BackgroundPlaybackPaused.HasValue );
		Assert.IsTrue( s.BackgroundPlaybackPaused.Value );
	}

	[TestMethod]
	public void BackgroundPlaybackState_Running()
	{
		// anything other than 'paused' resolves to false (not paused)
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-playback-state", "running" ) );
		Assert.IsTrue( s.BackgroundPlaybackPaused.HasValue );
		Assert.IsFalse( s.BackgroundPlaybackPaused.Value );
	}

	// ---------------------------------------------------------------------
	// background shorthand colour forms - any Color.Parse-able token sets BackgroundColor.
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Background_NamedColor()
	{
		var s = new Styles();
		s.Set( "background", "red" );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Background_Rebeccapurple()
	{
		var s = new Styles();
		s.Set( "background", "rebeccapurple" );
		Assert.IsTrue( s.BackgroundColor.HasValue );
	}

	[TestMethod]
	public void Background_Hsl()
	{
		var s = new Styles();
		s.Set( "background", "hsl(0 100% 50%)" );
		Assert.IsTrue( s.BackgroundColor.HasValue );
	}

	[TestMethod]
	public void Background_Rgba()
	{
		var s = new Styles();
		s.Set( "background", "rgba(0,0,0,0.5)" );
		Assert.IsTrue( s.BackgroundColor.HasValue );
	}

	[TestMethod]
	public void Background_LeadingHexColorParsed()
	{
		// A leading colour plus an image both apply: #fff sets BackgroundColor (white), url() sets the image.
		var s = new Styles();
		s.Set( "background", "#fff url(/x.png) no-repeat" );
		Assert.AreEqual( new Color( 1, 1, 1, 1 ), s.BackgroundColor.Value );
	}
}
