using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class Variables
{
	[TestMethod]
	public void Parsing()
	{
		var sheet = StyleParser.ParseSheet( "$varname: value;" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( "value", sheet.GetVariable( "$varname" ) );

		sheet = StyleParser.ParseSheet( "$one: value; $two: red ; .class{ background-color: red; } $three: blue;" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( "value", sheet.GetVariable( "$one" ) );
		Assert.AreEqual( "red", sheet.GetVariable( "$two" ) );
		Assert.AreEqual( "blue", sheet.GetVariable( "$three" ) );
	}

	[TestMethod]
	public void VariablesInsideVariables()
	{
		var sheet = StyleParser.ParseSheet( "$primary-color: #36CEDC; $secondary-color: #5889E3; $menu-background: linear-gradient($primary-color, $secondary-color);" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( "linear-gradient(#36CEDC, #5889E3)", sheet.GetVariable( "$menu-background" ) );
	}

	[TestMethod]
	public void Using()
	{
		var sheet = StyleParser.ParseSheet( "$variable: red; .class{ background-color: $variable; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( "red", sheet.GetVariable( "$variable" ) );

		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
		Assert.AreEqual( true, sheet.Nodes[0].Styles.BackgroundColor.HasValue );
		Assert.AreEqual( Color.Red, sheet.Nodes[0].Styles.BackgroundColor.Value );
	}

	[TestMethod]
	public void MissingVariable()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			var sheet = StyleParser.ParseSheet( "$variable: red; .class{ background-color: $varifable; }" );
		} );
	}

	[TestMethod]
	[DoNotParallelize] // Modfiies UI System Global
	public void Inherit()
	{
		var panel = new RootPanel();
		panel.StyleSheet.Parse( "$variable: red;" );
		panel.StyleSheet.Parse( "RootPanel { background-color: $variable; }" );
		panel.Layout();

		Assert.AreEqual( Color.Red, panel.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	[DoNotParallelize] // Modfiies UI System Global
	public void InheritFromAncestor()
	{
		var panel = new RootPanel();
		panel.StyleSheet.Parse( "$variable: red;" );

		var child = panel.Add.Panel( "child" );
		child.StyleSheet.Parse( ".child { background-color: $variable; }" );

		panel.Layout();

		Assert.AreEqual( Color.Red, child.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void Default()
	{
		{
			var sheet = StyleParser.ParseSheet( "$variable: red; $variable: green !default; .class{ background-color: $variable; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( "red", sheet.GetVariable( "$variable" ) );

			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( true, sheet.Nodes[0].Styles.BackgroundColor.HasValue );
			Assert.AreEqual( Color.Red, sheet.Nodes[0].Styles.BackgroundColor.Value );
		}

		{
			var sheet = StyleParser.ParseSheet( "$variable: green !default; $variable: red; .class{ background-color: $variable; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( "red", sheet.GetVariable( "$variable" ) );

			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( true, sheet.Nodes[0].Styles.BackgroundColor.HasValue );
			Assert.AreEqual( Color.Red, sheet.Nodes[0].Styles.BackgroundColor.Value );
		}

		{
			var sheet = StyleParser.ParseSheet( "$variable: red !default; .class{ background-color: $variable; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( "red", sheet.GetVariable( "$variable" ) );

			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( true, sheet.Nodes[0].Styles.BackgroundColor.HasValue );
			Assert.AreEqual( Color.Red, sheet.Nodes[0].Styles.BackgroundColor.Value );
		}
	}
}
