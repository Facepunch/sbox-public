using Sandbox.UI;

namespace UITest;

/// <summary>
/// Verifies that 'none' and colour-only shorthand forms reset the longhands they own back to their
/// initial values, so they override a value set by a less specific (base class) rule - matching how
/// the web's shorthand reset works, instead of leaving the base value showing through.
///
/// These exercise the cascade directly via Styles.Add (base added first, then the more specific rule)
/// rather than a full panel Layout, because reading a computed background-image during layout forces
/// the texture, which isn't available headless.
/// </summary>
[TestClass]
public class ShorthandReset
{
	/// <summary>
	/// Cascades a base rule then a more specific override rule, the way the style system does.
	/// </summary>
	static Styles Cascade( Styles baseRule, Styles overrideRule )
	{
		var result = new Styles();
		result.Add( baseRule );
		result.Add( overrideRule );
		return result;
	}

	static Styles Rule( params (string prop, string value)[] declarations )
	{
		var s = new Styles();
		foreach ( var (prop, value) in declarations )
			s.Set( prop, value );
		return s;
	}

	/// <summary>
	/// background: &lt;colour&gt; clears a background-image set by a base class.
	/// </summary>
	[TestMethod]
	public void BackgroundColorClearsBaseImage()
	{
		var result = Cascade(
			Rule( ("background-image", "url(x.png)") ),
			Rule( ("background", "#fff") ) );

		Assert.AreSame( Styles.NoImage, result._backgroundImage );
		Assert.AreEqual( Color.White, result.BackgroundColor.Value );
	}

	/// <summary>
	/// background: none clears a base class's background image and colour.
	/// </summary>
	[TestMethod]
	public void BackgroundNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("background-image", "url(x.png)"), ("background-color", "red") ),
			Rule( ("background", "none") ) );

		Assert.AreSame( Styles.NoImage, result._backgroundImage );
		Assert.AreEqual( Color.Transparent, result.BackgroundColor.Value );
	}

	/// <summary>
	/// border: none clears a base class's border.
	/// </summary>
	[TestMethod]
	public void BorderNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("border", "4px solid red") ),
			Rule( ("border", "none") ) );

		Assert.AreEqual( 0, result.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 0, result.BorderTopWidth.Value.Value );
		Assert.AreEqual( 0, result.BorderRightWidth.Value.Value );
		Assert.AreEqual( 0, result.BorderBottomWidth.Value.Value );
	}

	/// <summary>
	/// filter: none clears a base class's filter.
	/// </summary>
	[TestMethod]
	public void FilterNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("filter", "blur(8px)") ),
			Rule( ("filter", "none") ) );

		Assert.AreEqual( 0, result.FilterBlur.Value.Value );
	}

	/// <summary>
	/// backdrop-filter: none clears a base class's backdrop-filter.
	/// </summary>
	[TestMethod]
	public void BackdropFilterNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("backdrop-filter", "blur(8px)") ),
			Rule( ("backdrop-filter", "none") ) );

		Assert.AreEqual( 0, result.BackdropFilterBlur.Value.Value );
	}
}
