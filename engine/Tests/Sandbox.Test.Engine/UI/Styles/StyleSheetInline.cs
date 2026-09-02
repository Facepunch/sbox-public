using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class StyleSheetInlineTest
{
	/// <summary>
	/// Inline sheets are parsed once and shared - the same key returns the same instance,
	/// and only a content change causes a reparse.
	/// </summary>
	[TestMethod]
	public void CachedAndReparsedOnChange()
	{
		var sheet = StyleSheet.FromInline( ".thing { margin-top: 10px; }", "inline:UITests.CacheTest" );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".thing" ) ) );

		var again = StyleSheet.FromInline( ".thing { margin-top: 10px; }", "inline:UITests.CacheTest" );
		Assert.AreSame( sheet, again );

		// changed content reparses in place, keeping the shared instance
		var changed = StyleSheet.FromInline( ".other { margin-top: 20px; }", "inline:UITests.CacheTest" );
		Assert.AreSame( sheet, changed );
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".other" ) ) );
		Assert.IsFalse( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".thing" ) ) );
	}

	/// <summary>
	/// [StyleSheet.Inline] carries the sheet name and content.
	/// </summary>
	[TestMethod]
	public void InlineAttribute()
	{
		var attr = new StyleSheet.InlineAttribute( "test", ".x { color: red; }" );
		Assert.AreEqual( "test", attr.Name );
		Assert.AreEqual( ".x { color: red; }", attr.Styles );
	}
}
