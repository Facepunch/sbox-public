using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class StyleSheetParse
{
	[TestMethod]
	public void Empty()
	{
		var sheet = StyleParser.ParseSheet( ".one { }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 0, sheet.Nodes.Count );
	}

	[TestMethod]
	public void SingleSimple()
	{
		var sheet = StyleParser.ParseSheet( ".one { width: 100%; height: 10%; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );

		Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
		Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );

		Assert.IsTrue( sheet.Nodes[0].Styles.Height.HasValue );
		Assert.AreEqual( 10, sheet.Nodes[0].Styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Height.Value.Unit );

		sheet = StyleParser.ParseSheet( "  .one \n\n{\n \twidth: 100%;\n \theight: 10%;\n\n}\n\n\n  " );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
		Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );

		Assert.IsTrue( sheet.Nodes[0].Styles.Height.HasValue );
		Assert.AreEqual( 10, sheet.Nodes[0].Styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Height.Value.Unit );
	}

	[TestMethod]
	public void MultipleSimple()
	{
		var sheet = StyleParser.ParseSheet( ".one { width: 100%; height: 10%; } .two { width: 30%; height: 40%; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );

		Assert.AreEqual( "one", sheet.Nodes[0].Selectors[0].Classes.First() );
		Assert.AreEqual( "two", sheet.Nodes[1].Selectors[0].Classes.First() );

		Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
		Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );

		Assert.IsTrue( sheet.Nodes[0].Styles.Height.HasValue );
		Assert.AreEqual( 10, sheet.Nodes[0].Styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Height.Value.Unit );

		Assert.IsTrue( sheet.Nodes[1].Styles.Width.HasValue );
		Assert.AreEqual( 30, sheet.Nodes[1].Styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[1].Styles.Width.Value.Unit );

		Assert.IsTrue( sheet.Nodes[1].Styles.Height.HasValue );
		Assert.AreEqual( 40, sheet.Nodes[1].Styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[1].Styles.Height.Value.Unit );
	}

	[TestMethod]
	public void MultiSelectors()
	{
		var sheet = StyleParser.ParseSheet( ".one, .two, .three { width: 100%; height: 10%; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 3, sheet.Nodes[0].Selectors.Length );

		Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
		Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );

		Assert.IsTrue( sheet.Nodes[0].Styles.Height.HasValue );
		Assert.AreEqual( 10, sheet.Nodes[0].Styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Height.Value.Unit );
	}

	[TestMethod]
	public void NestedRulesSimple()
	{
		var variants = new[]
		{
			".one { .two { width: 50%; } width: 100%; height: 10%; }",
			".one { width: 100%; height: 10%; .two { width: 50%; } }",
			".one { width: 100%; .two { width: 50%; } height: 10%;  }",
		};

		foreach ( var v in variants )
		{
			var sheet = StyleParser.ParseSheet( v );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 2, sheet.Nodes.Count );

			Assert.AreEqual( "one", sheet.Nodes[1].Selectors[0].Classes.First() );
			Assert.AreEqual( "two", sheet.Nodes[0].Selectors[0].Classes.First() );

			var one = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".one" ) );
			Assert.IsNotNull( one );

			Assert.IsTrue( one.Styles.Width.HasValue );
			Assert.AreEqual( 100, one.Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, one.Styles.Width.Value.Unit );

			Assert.IsTrue( one.Styles.Height.HasValue );
			Assert.AreEqual( 10, one.Styles.Height.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, one.Styles.Height.Value.Unit );

			var two = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".one .two" ) );
			Assert.IsNotNull( two );

			Assert.IsTrue( two.Styles.Width.HasValue );
			Assert.AreEqual( 50, two.Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, two.Styles.Width.Value.Unit );

			Assert.IsFalse( two.Styles.Height.HasValue );
		}
	}

	[TestMethod]
	public void NestedRulesComplicated()
	{
		var variants = new[]
		{
			".one { color: red; .two { width: 50%; } .three { width: 50%; .four { width: 50%; &:hover { width: 20%; } } } width: 100%; height: 10%; }",
			".one { color: red; .three { width: 50%; .four { width: 50%; &:hover { width: 20%; } } } width: 100%; .two { width: 50%; } height: 10%; }",
			".one \n{\n color: red; .three \n{\n color: red; .four \n{\n     width:          50%;     &:hover\n {\n width:    20%; }\n } \n} width: 100%;\n .two { width: 50%; }\n height: 10%; }",
			".one .two { width: 50%; } .one .three { width: 50%; .four { width: 50%; &:hover { width: 20%; } } } .one { width: 100%; height: 10%; }",
		};

		foreach ( var v in variants )
		{
			var sheet = StyleParser.ParseSheet( v );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 5, sheet.Nodes.Count );

			var one = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".one" ) );
			Assert.IsNotNull( one );

			Assert.IsTrue( one.Styles.Width.HasValue );
			Assert.AreEqual( 100, one.Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, one.Styles.Width.Value.Unit );

			Assert.IsTrue( one.Styles.Height.HasValue );
			Assert.AreEqual( 10, one.Styles.Height.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, one.Styles.Height.Value.Unit );

			var two = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".one .two" ) );
			Assert.IsNotNull( two );

			Assert.IsTrue( two.Styles.Width.HasValue );
			Assert.AreEqual( 50, two.Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, two.Styles.Width.Value.Unit );

			Assert.IsFalse( two.Styles.Height.HasValue );

			var five = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".one .three .four:hover" ) );
			Assert.IsNotNull( five );

			Assert.IsTrue( five.Styles.Width.HasValue );
			Assert.AreEqual( 20, five.Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, five.Styles.Width.Value.Unit );

			Assert.IsFalse( five.Styles.Height.HasValue );
		}
	}

	[TestMethod]
	public void WithTestData()
	{
		var sheet = StyleParser.ParseSheet( System.IO.File.ReadAllText( "unittest/styles/valid.simple.scss" ) );
	}

	[TestMethod]
	public void FlagSelectors()
	{
		{
			var sheet = StyleParser.ParseSheet( ".one:hover { width: 100%; height: 10%; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( ".one:hover", sheet.Nodes[0].Selectors[0].AsString );
			Assert.AreEqual( PseudoClass.Hover, sheet.Nodes[0].Selectors[0].Flags );

			Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
			Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );
		}

		{
			var sheet = StyleParser.ParseSheet( ".one:hover, .one:active { width: 100%; height: 10%; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 2, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( PseudoClass.Hover, sheet.Nodes[0].Selectors[0].Flags );
			Assert.AreEqual( PseudoClass.Active, sheet.Nodes[0].Selectors[1].Flags );

			Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
			Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );
		}

		{
			var sheet = StyleParser.ParseSheet( ".one{ &:hover, &:active, &:intro, &:outro { width: 100%; height: 10%; } }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 4, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( ".one:hover", sheet.Nodes[0].Selectors[0].AsString );
			Assert.AreEqual( ".one:active", sheet.Nodes[0].Selectors[1].AsString );
			Assert.AreEqual( ".one:intro", sheet.Nodes[0].Selectors[2].AsString );
			Assert.AreEqual( ".one:outro", sheet.Nodes[0].Selectors[3].AsString );
			Assert.AreEqual( PseudoClass.Hover, sheet.Nodes[0].Selectors[0].Flags );
			Assert.AreEqual( PseudoClass.Active, sheet.Nodes[0].Selectors[1].Flags );

			Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
			Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );
		}
	}

	[TestMethod]
	public void BeforeAfter()
	{
		{
			var sheet = StyleParser.ParseSheet( ".one:before { width: 100%; height: 10%; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( ".one:before", sheet.Nodes[0].Selectors[0].AsString );
			Assert.AreEqual( PseudoClass.Before, sheet.Nodes[0].Selectors[0].Flags );

			Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
			Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );
		}

		{
			var sheet = StyleParser.ParseSheet( ".one::before { width: 100%; height: 10%; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( ".one::before", sheet.Nodes[0].Selectors[0].AsString );
			Assert.AreEqual( PseudoClass.Before, sheet.Nodes[0].Selectors[0].Flags );

			Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
			Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );
		}

		{
			var sheet = StyleParser.ParseSheet( ".one:after { width: 100%; height: 10%; }" );

			Assert.IsNotNull( sheet );
			Assert.AreEqual( 1, sheet.Nodes.Count );
			Assert.AreEqual( 1, sheet.Nodes[0].Selectors.Length );
			Assert.AreEqual( ".one:after", sheet.Nodes[0].Selectors[0].AsString );
			Assert.AreEqual( PseudoClass.After, sheet.Nodes[0].Selectors[0].Flags );

			Assert.IsTrue( sheet.Nodes[0].Styles.Width.HasValue );
			Assert.AreEqual( 100, sheet.Nodes[0].Styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Percentage, sheet.Nodes[0].Styles.Width.Value.Unit );
		}
	}
}
