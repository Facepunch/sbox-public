using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// The value model behind the colour picker - splitting a colour into what the controls show
/// and putting it back together again, and the text form that goes with it.
/// </summary>
[TestClass]
public class PickerColorTests
{
	[TestMethod]
	public void SdrRoundTrips()
	{
		var color = new Color( 0.2f, 0.6f, 0.9f, 0.5f );
		var picker = PickerColor.FromColor( color );

		Assert.AreEqual( 1.0f, picker.Brightness );
		Assert.AreEqual( 0.5f, picker.Hsv.Alpha );
		AssertClose( color, picker.ToColor() );
	}

	[TestMethod]
	public void HdrFoldsIntoBrightness()
	{
		var color = new Color( 1.0f, 0.5f, 0.0f ).ScaleBrightness( 4.0f );
		var picker = PickerColor.FromColor( color );

		Assert.AreEqual( 4.0f, picker.Brightness, 0.01f );
		Assert.IsTrue( picker.BaseColor.IsSdr, "the base colour stays inside 0..1" );
		AssertClose( color, picker.ToColor() );
	}

	[TestMethod]
	public void TextIsHexForSdr()
	{
		Assert.AreEqual( "#3273EB", PickerColor.FromColor( Color.Parse( "#3273eb" ).Value ).ToText() );
		Assert.AreEqual( "#3273EB80", PickerColor.FromColor( Color.Parse( "#3273eb80" ).Value ).ToText() );
	}

	[TestMethod]
	public void TypedBrightnessIsKeptAsTyped()
	{
		Assert.IsTrue( PickerColor.TryParse( "#3273eb * 4", 0, out var picker ) );

		Assert.AreEqual( 4.0f, picker.Brightness );
		Assert.AreEqual( "#3273EB * 4", picker.ToText() );
		AssertClose( Color.Parse( "#3273eb * 4" ).Value, picker.ToColor() );
	}

	[TestMethod]
	public void RawHdrValueSplitsAtTheBrightestChannel()
	{
		// Loaded from a property there's no text to keep, so the base colour is scaled until its brightest channel is 1
		var color = Color.Parse( "#3273eb * 4" ).Value;
		var picker = PickerColor.FromColor( color );

		Assert.AreEqual( 1.0f, picker.Hsv.Value, 0.001f );
		Assert.IsTrue( picker.Brightness > 1.0f );
		AssertClose( color, picker.ToColor() );

		Assert.IsTrue( PickerColor.TryParse( picker.ToText(), 0, out var parsed ), "the text form parses back" );
		AssertClose( color, parsed.ToColor() );
	}

	[TestMethod]
	public void ParsesEveryFormColorDoes()
	{
		Assert.IsTrue( PickerColor.TryParse( "white", 0, out var named ) );
		Assert.AreEqual( 1.0f, named.Hsv.Value );

		Assert.IsTrue( PickerColor.TryParse( "rgba( 255, 0, 0, 0.5 )", 0, out var rgba ) );
		Assert.AreEqual( 0.5f, rgba.Hsv.Alpha, 0.01f );
		Assert.AreEqual( 0.0f, rgba.Hsv.Hue );

		Assert.IsTrue( PickerColor.TryParse( "white * 2", 0, out var bright ) );
		Assert.AreEqual( 2.0f, bright.Brightness, 0.01f );

		Assert.IsFalse( PickerColor.TryParse( "nonsense", 0, out _ ) );
		Assert.IsFalse( PickerColor.TryParse( "", 0, out _ ) );
	}

	[TestMethod]
	public void GreysKeepTheHueTheyWereGiven()
	{
		Assert.AreEqual( 210.0f, PickerColor.FromColor( Color.Gray, 210.0f ).Hsv.Hue );
		Assert.AreEqual( 210.0f, PickerColor.FromColor( Color.Black, 210.0f ).Hsv.Hue );
		Assert.AreEqual( 0.0f, PickerColor.FromColor( Color.Red, 210.0f ).Hsv.Hue, "a real colour brings its own hue" );
	}

	static void AssertClose( Color expected, Color actual )
	{
		Assert.AreEqual( expected.r, actual.r, 0.01f, "r" );
		Assert.AreEqual( expected.g, actual.g, 0.01f, "g" );
		Assert.AreEqual( expected.b, actual.b, 0.01f, "b" );
		Assert.AreEqual( expected.a, actual.a, 0.01f, "a" );
	}
}
