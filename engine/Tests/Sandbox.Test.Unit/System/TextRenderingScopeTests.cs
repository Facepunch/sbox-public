using Sandbox.UI;

namespace SystemTests;

[TestClass]
public class TextRenderingScopeTest
{
	[TestMethod]
	public void HashCodeIncludesFontVariantNumeric()
	{
		var normal = TextRendering.Scope.Default;
		normal.FontVariantNumeric = FontVariantNumeric.Normal;

		var tabular = normal;
		tabular.FontVariantNumeric = FontVariantNumeric.TabularNums;

		Assert.AreNotEqual( normal.GetHashCode(), tabular.GetHashCode() );
	}

	[TestMethod]
	public void ToStyleCopiesFontVariantNumeric()
	{
		var scope = TextRendering.Scope.Default;
		scope.FontVariantNumeric = FontVariantNumeric.TabularNums;

		var style = new Topten.RichTextKit.Style();
		scope.ToStyle( style );

		Assert.AreEqual( FontVariantNumeric.TabularNums, style.FontVariantNumeric );
	}
}
