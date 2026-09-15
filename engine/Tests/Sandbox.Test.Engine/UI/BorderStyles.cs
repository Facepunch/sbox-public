using System;
using Sandbox.UI;

namespace UITests;

[TestClass]
public class BorderStylesTest
{
	[TestMethod]
	public void EveryStyleParses()
	{
		foreach ( var style in Enum.GetValues<BorderStyle>() )
		{
			var s = new Styles();
			Assert.IsTrue( s.Set( "border-style", style.ToString().ToUpperInvariant() ) );
			Assert.AreEqual( style, s.BorderStyle );
		}
	}

	[TestMethod]
	public void BorderShorthandsRetainStylesAndRejectPartialValues()
	{
		foreach ( var value in new[] { "4px dashed red", "red 4px dashed", "dashed red 4px" } )
		{
			var s = new Styles();
			Assert.IsTrue( s.Set( "border", value ) );
			Assert.AreEqual( BorderStyle.Dashed, s.BorderStyle );
			Assert.AreEqual( 4f, s.BorderTopWidth.Value.Value );
			Assert.AreEqual( Color.Red, s.BorderTopColor );
			Assert.IsTrue( s.Set( "border-left", "2px dotted blue" ) );
			Assert.AreEqual( BorderStyle.Dotted, s.BorderStyle );
			Assert.IsTrue( s.Set( "border-right", "3px green" ) );
			Assert.AreEqual( BorderStyle.Dotted, s.BorderStyle );
			foreach ( var invalid in new[] { "12px dashed nonsense", "solid dashed", "2px 3px red", "solid red blue", "none nonsense", "-2px dotted", "" } )
			{
				Assert.IsFalse( s.Set( "border", invalid ), invalid );
				Assert.AreEqual( BorderStyle.Dotted, s.BorderStyle );
				Assert.AreEqual( 2f, s.BorderLeftWidth.Value.Value );
				Assert.AreEqual( Color.Blue, s.BorderLeftColor );
			}
		}
	}

	[TestMethod]
	public void BorderNoneZeroesWidthsWithoutHidingLaterSides()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "4px dashed red" ) );
		Assert.IsTrue( s.Set( "border", "none" ) );
		Assert.AreEqual( 0f, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( BorderStyle.Dashed, s.BorderStyle );
		Assert.IsTrue( s.Set( "border-bottom", "2px solid" ) );
		Assert.AreEqual( BorderStyle.Solid, s.BorderStyle );
		s.FillDefaults();
		Assert.AreEqual( new Vector4( 0, 0, 0, 2 ), s.GetBorderWidths( 100 ) );
		Assert.IsTrue( s.HasBorder );
		Assert.AreEqual( 2f, s.GetInset( new Vector2( 100 ) ).Bottom );

		s = new Styles();
		Assert.IsTrue( s.Set( "border", "none" ) );
		Assert.IsTrue( s.Set( "border-bottom", "2px" ) );
		s.FillDefaults();
		Assert.AreEqual( new Vector4( 0, 0, 0, 2 ), s.GetBorderWidths( 100 ) );
		Assert.IsTrue( s.Set( "border", "2px hidden" ) );
		Assert.AreEqual( Vector4.Zero, s.GetBorderWidths( 100 ) );
	}

	[TestMethod]
	public void InvalidStyleDoesNotOverwriteExistingValue()
	{
		var s = new Styles { BorderStyle = BorderStyle.Dashed };
		foreach ( var invalid in new[] { "", "1", "unknown", "solid dotted", "solid dotted dashed double none", "solid, dotted", "solid nonsense" } )
		{
			Assert.IsFalse( s.Set( "border-style", invalid ), invalid );
			Assert.AreEqual( BorderStyle.Dashed, s.BorderStyle );
		}
		Assert.IsFalse( s.Set( "border-left-style", "dotted" ) );
		Assert.AreEqual( BorderStyle.Dashed, s.BorderStyle );
	}

	[TestMethod]
	public void UsedWidthsPreserveAuthoredValues()
	{
		var s = new Styles { BorderWidth = 8, BorderStyle = BorderStyle.Dashed };
		s.FillDefaults();
		Assert.IsTrue( s.HasBorder );
		foreach ( var style in new[] { BorderStyle.None, BorderStyle.Hidden } )
		{
			s.BorderStyle = style;
			Assert.IsFalse( s.HasBorder );
			Assert.AreEqual( Vector4.Zero, s.GetBorderWidths( 100 ) );
			Assert.AreEqual( 8f, s.BorderTopWidth.Value.Value );
			Assert.AreEqual( 0f, s.GetInset( new Vector2( 100 ) ).Top );
		}
		s.BorderStyle = BorderStyle.Dotted;
		Assert.AreEqual( new Vector4( 8 ), s.GetBorderWidths( 100 ) );
		Assert.IsTrue( s.HasBorder );
		Assert.AreEqual( 8f, s.GetInset( new Vector2( 100 ) ).Top );
	}

	[TestMethod]
	public void StylesCopyHashAndDefaultConsistently()
	{
		var s = new Styles();
		s.FillDefaults();
		Assert.AreEqual( BorderStyle.Solid, s.BorderStyle );
		int hash = s.GetHashCode();
		s.BorderStyle = BorderStyle.Dotted;
		Assert.AreNotEqual( hash, s.GetHashCode() );
		Assert.AreEqual( BorderStyle.Dotted, ((Styles)s.Clone()).BorderStyle );
		var copy = new Styles(); copy.Add( s );
		Assert.AreEqual( BorderStyle.Dotted, copy.BorderStyle );
	}
}
