using Sandbox.UI;

namespace UITest;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public class SelectorScore
{
	void TwoIsRed( string style )
	{
		var one = new RootPanel();
		one.ElementName = "rootpanel";
		one.StyleSheet.Parse( style );
		one.AddClass( "one" );
		one.AddClass( "also-one" );

		var two = one.Add.Panel( "two" );
		two.AddClass( "also-two" );
		two.Switch( PseudoClass.Hover, true );
		one.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), two.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void DefinitionOrder()
	{
		TwoIsRed( ".two { background-color: green; } .two { background-color: red; }" );
	}

	[TestMethod]
	public void ParentSpecify()
	{
		TwoIsRed( ".two { background-color: green; } .one .two { background-color: red; }" );
		TwoIsRed( ".one .two { background-color: red; } .two { background-color: green; }" );

		TwoIsRed( ".wrong .wrong .two { background-color: green; } .two { background-color: red; }" );
	}

	[TestMethod]
	public void ElementSpecify()
	{
		TwoIsRed( ".one .two { background-color: red; } rootpanel .two { background-color: green; }" );
		TwoIsRed( "rootpanel .two { background-color: green; } .one .two { background-color: red; }" );
	}

	[TestMethod]
	public void SpecificParentSpecify()
	{
		TwoIsRed( ".one .two { background-color: green; } .one.also-one .two { background-color: red; }" );
		TwoIsRed( ".one.also-one .two { background-color: red; } .one .two { background-color: green; }" );
	}

	[TestMethod]
	public void FlagSpecify()
	{
		TwoIsRed( ".two { background-color: green; } .two:hover { background-color: red; }" );
		TwoIsRed( ".two:hover { background-color: red; } .two { background-color: green; }" );
	}

	[TestMethod]
	public void Not()
	{
		TwoIsRed( ".two { background-color: green; } .two:not( .green ) { background-color: red; }" );
		TwoIsRed( ".two:not( .green ) { background-color: red; } .two { background-color: green; }" );
		TwoIsRed( ".two:not( .green ) { background-color: green; } .two:not( .green.orange ) { background-color: red; }" );
		TwoIsRed( ".two:not( .green.orange ) { background-color: red; } .two:not( .green ) { background-color: green; }" );
	}

	/// <summary>
	/// Specificity should beat load order.
	/// </summary>
	[TestMethod]
	public void LoadOrderDoesNotBleedIntoSpecificity()
	{
		var specific = new StyleBlock();
		specific.SetSelector( "panel.a" );

		var general = new StyleBlock { LoadOrder = 1001 };
		general.SetSelector( ".b" );

		Assert.IsTrue( specific.Selectors[0].Score > general.Selectors[0].Score );
	}

	/// <summary>
	/// Score comparison shouldn't overflow.
	/// </summary>
	[TestMethod]
	public void StyleOrdererHandlesLargeScores()
	{
		var high = new StyleSelector { SelfScore = 2_000_000_000 };
		var low = new StyleSelector { SelfScore = -2_000_000_000 };

		Assert.IsTrue( StyleOrderer.Instance.Compare( high, low ) > 0 );
	}

}
