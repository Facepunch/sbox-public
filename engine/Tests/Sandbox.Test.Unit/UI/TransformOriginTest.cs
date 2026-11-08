using Sandbox.UI;

namespace UITests;

[TestClass]
public class TransformOriginTest
{
	/// <summary>
	/// Keywords pick their own axis, so "bottom left" and "left bottom" are the same point (sbox-public#12195)
	/// </summary>
	[TestMethod]
	public void KeywordOrder()
	{
		foreach ( var value in new[] { "bottom left", "left bottom" } )
		{
			var s = new Styles();
			Assert.IsTrue( s.Set( "transform-origin", value ) );
			Assert.AreEqual( LengthUnit.Start, s.TransformOriginX.Value.Unit, value );
			Assert.AreEqual( LengthUnit.End, s.TransformOriginY.Value.Unit, value );
		}

		var top = new Styles();
		Assert.IsTrue( top.Set( "transform-origin", "top" ) );
		Assert.AreEqual( LengthUnit.Center, top.TransformOriginX.Value.Unit );
		Assert.AreEqual( LengthUnit.Start, top.TransformOriginY.Value.Unit );
	}
}
