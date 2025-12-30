
namespace SystemTest;

[TestClass]
public class ColorTest
{
	[TestMethod]
	public void ToHSV_Red()
	{
		Color color = Color.Red;
		ColorHsv hsv = color.ToHsv();
		Assert.AreEqual( 0, hsv.Hue );
		Assert.AreEqual( 1, hsv.Saturation );
		Assert.AreEqual( 1, hsv.Value );
		Assert.AreEqual( 1, hsv.Alpha );
	}

	[TestMethod]
	public void ToHSV_Green()
	{
		Color color = Color.Green;
		ColorHsv hsv = color.ToHsv();
		Assert.AreEqual( 120.0f, hsv.Hue );
		Assert.AreEqual( 1, hsv.Saturation );
		Assert.AreEqual( 1, hsv.Value );
		Assert.AreEqual( 1, hsv.Alpha );
	}

	[TestMethod]
	public void ToHSV_Blue()
	{
		Color color = Color.Blue;
		ColorHsv hsv = color.ToHsv();
		Assert.AreEqual( 240.0f, hsv.Hue );
		Assert.AreEqual( 1, hsv.Saturation );
		Assert.AreEqual( 1, hsv.Value );
		Assert.AreEqual( 1, hsv.Alpha );
	}

	[TestMethod]
	public void ToHSV_Yellow()
	{
		Color color = Color.Yellow;
		ColorHsv hsv = color.ToHsv();
		Assert.AreEqual( 60.0f, hsv.Hue );
		Assert.AreEqual( 1, hsv.Saturation );
		Assert.AreEqual( 1, hsv.Value );
		Assert.AreEqual( 1, hsv.Alpha );
	}

	[TestMethod]
	public void ToHSV_Darker()
	{
		Color color = new Color( 0, 0.5f, 0, 0.5f );
		ColorHsv hsv = color.ToHsv();
		Assert.AreEqual( 120.0f, hsv.Hue );
		Assert.AreEqual( 1, hsv.Saturation );
		Assert.AreEqual( 0.5f, hsv.Value );
		Assert.AreEqual( 0.5f, hsv.Alpha );
	}

	[TestMethod]
	public void ToHSVAndBack_Red()
	{
		Color color = Color.Red;
		ColorHsv hsv = color.ToHsv();
		Color color_from = hsv;
		Assert.AreEqual( color_from, color );
	}

	[TestMethod]
	public void ToHSVAndBack_Blue()
	{
		Color color = Color.Blue;
		ColorHsv hsv = color.ToHsv();
		Color color_from = hsv;
		Assert.AreEqual( color_from, color );
	}

	[TestMethod]
	public void ToHSVAndBack_Green()
	{
		Color color = Color.Green;
		ColorHsv hsv = color.ToHsv();
		Color color_from = hsv;
		Assert.AreEqual( color_from, color );
	}

	[TestMethod]
	public void ToHSVAndBack_Yellow()
	{
		Color color = Color.Yellow;
		ColorHsv hsv = color.ToHsv();
		Color color_from = hsv;
		Assert.AreEqual( color_from, color );
	}

	[TestMethod]
	public void ToHSVAndBack_Yellow_WithAlpha()
	{
		Color color = Color.Yellow.WithAlpha( 0.45f );
		ColorHsv hsv = color.ToHsv();
		Color color_from = hsv;
		Assert.AreEqual( color_from, color );
	}

	[TestMethod]
	public void ToFloatAndBack()
	{
		for ( var i = 0; i <= 255; ++i )
		{
			var color1 = new Color32( (byte)i, 0, 0 );
			var color2 = new Color32( 0, (byte)i, 0 );
			var color3 = new Color32( 0, 0, (byte)i );
			var color4 = new Color32( 255, 255, 255, (byte)i );

			Assert.AreEqual( color1, color1.ToColor().ToColor32() );
			Assert.AreEqual( color2, color2.ToColor().ToColor32() );
			Assert.AreEqual( color3, color3.ToColor().ToColor32() );
			Assert.AreEqual( color4, color4.ToColor().ToColor32() );
		}
	}

	[TestMethod]
	public void ToRgbaInt()
	{
		var color = new Color32( 0xff, 0xcc, 0x99, 0x66 );

		Assert.AreEqual( 0xffcc9966, color.RgbaInt );
	}

	[TestMethod]
	public void FromRgbaInt()
	{
		var color = Color32.FromRgba( 0xffcc9966 );

		Assert.AreEqual( 0xff, color.r );
		Assert.AreEqual( 0xcc, color.g );
		Assert.AreEqual( 0x99, color.b );
		Assert.AreEqual( 0x66, color.a );
	}

	[TestMethod]
	public void ToRgbInt()
	{
		var color = new Color32( 0x66, 0xcc, 0x99 );

		Assert.AreEqual( 0x66cc99u, color.RgbInt );
	}

	[TestMethod]
	public void FromRgbInt()
	{
		var color = Color32.FromRgb( 0x66cc99 );

		Assert.AreEqual( 0x66, color.r );
		Assert.AreEqual( 0xcc, color.g );
		Assert.AreEqual( 0x99, color.b );
		Assert.AreEqual( 0xff, color.a );
	}

	[TestMethod]
	public void ColorToHsvTest()
	{
		var color = new Color( 0.0f, 1.0f, 1.0f, 1.0f );
		Assert.AreEqual( new ColorHsv( 180.0f, 1.0f, 1.0f, 1.0f ), color.ToHsv() );
	}

	[TestMethod]
	public void HsvToColorTest()
	{
		var hsv = new ColorHsv( 180.0f, 1.0f, 1.0f, 1.0f );
		Assert.AreEqual( new Color( 0.0f, 1.0f, 1.0f, 1.0f ), hsv.ToColor() );
	}
}
