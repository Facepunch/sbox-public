using Sandbox.UI;

namespace UITest.PropertyCoverage;

/// <summary>
/// Coverage for the handful of properties not owned by the other group files:
/// pointer-events, text-filter, text-background-angle, and the background-image-tint alias.
/// </summary>
[TestClass]
public class MiscProperties
{
	// ── pointer-events (PointerEvents? : auto->null, none->None, all->All) ──────────

	[TestMethod]
	public void PointerEvents_None()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "pointer-events", "none" ) );
		Assert.AreEqual( PointerEvents.None, s.PointerEvents );
	}

	[TestMethod]
	public void PointerEvents_All()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "pointer-events", "all" ) );
		Assert.AreEqual( PointerEvents.All, s.PointerEvents );
	}

	[TestMethod]
	public void PointerEvents_Auto()
	{
		// 'auto' is valid and clears the override (sets the property back to null).
		var s = new Styles();
		Assert.IsTrue( s.Set( "pointer-events", "auto" ) );
		Assert.IsFalse( s.PointerEvents.HasValue );
	}

	[TestMethod]
	public void PointerEvents_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "pointer-events", "banana" ) );
	}

	// ── text-filter (Rendering.FilterMode?) ────────────────────────────────────────

	[TestMethod]
	public void TextFilter_Keywords()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-filter", "point" ) );
		Assert.AreEqual( Sandbox.Rendering.FilterMode.Point, s.TextFilter );

		s = new Styles();
		Assert.IsTrue( s.Set( "text-filter", "linear" ) );
		Assert.AreEqual( Sandbox.Rendering.FilterMode.Bilinear, s.TextFilter );

		s = new Styles();
		Assert.IsTrue( s.Set( "text-filter", "bilinear" ) );
		Assert.AreEqual( Sandbox.Rendering.FilterMode.Bilinear, s.TextFilter );

		s = new Styles();
		Assert.IsTrue( s.Set( "text-filter", "trilinear" ) );
		Assert.AreEqual( Sandbox.Rendering.FilterMode.Trilinear, s.TextFilter );

		s = new Styles();
		Assert.IsTrue( s.Set( "text-filter", "anisotropic" ) );
		Assert.AreEqual( Sandbox.Rendering.FilterMode.Anisotropic, s.TextFilter );
	}

	[TestMethod]
	public void TextFilter_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-filter", "banana" ) );
	}

	// ── text-background-angle (Length?) ────────────────────────────────────────────

	[TestMethod]
	public void TextBackgroundAngle_Value()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-background-angle", "45" ) );
		Assert.IsTrue( s.TextBackgroundAngle.HasValue );
		Assert.AreEqual( 45, s.TextBackgroundAngle.Value.Value );
	}

	[TestMethod]
	public void TextBackgroundAngle_DegParsesAsPixels()
	{
		// Length.Parse has no degrees unit, so a 'deg' suffix is read as a pixel value (the number is
		// preserved). Documents the engine's actual behaviour rather than CSS-ideal.
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-background-angle", "90deg" ) );
		Assert.AreEqual( 90, s.TextBackgroundAngle.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextBackgroundAngle.Value.Unit );
	}

	// ── background-image-tint (alias for background-tint -> BackgroundTint Color?) ──

	[TestMethod]
	public void BackgroundImageTint_AliasSetsTint()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-image-tint", "#ff0000" ) );
		Assert.IsTrue( s.BackgroundTint.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundTint.Value );
	}
}
