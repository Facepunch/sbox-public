using Sandbox.UI;

namespace UITests.Parsers;

[TestClass]
public class StyleSheetParseTest
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
	public void RecoversFromMalformedRule()
	{
		// The runtime loader (FromString) should skip a single broken rule rather than discarding the
		// whole sheet. The middle rule uses an unsupported nth-child form and throws while parsing.
		var sheet = StyleSheet.FromString( ".a { width: 100%; } .b:nth-child(2n+1) { width: 50%; } .c { height: 10%; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );
		Assert.IsNotNull( sheet.Nodes.SingleOrDefault( x => x.Selectors.Any( y => y.AsString == ".a" ) ) );
		Assert.IsNotNull( sheet.Nodes.SingleOrDefault( x => x.Selectors.Any( y => y.AsString == ".c" ) ) );
	}

	[TestMethod]
	public void RecoversFromMalformedProperty()
	{
		// A bad variable reference in one rule shouldn't take down the rest of the sheet.
		var sheet = StyleSheet.FromString( "$ok: red; .a { color: $ok; } .b { color: $typo; } .c { width: 5px; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );
		Assert.IsNotNull( sheet.Nodes.SingleOrDefault( x => x.Selectors.Any( y => y.AsString == ".a" ) ) );
		Assert.IsNotNull( sheet.Nodes.SingleOrDefault( x => x.Selectors.Any( y => y.AsString == ".c" ) ) );
	}

	[TestMethod]
	public void MalformedRuleRecoveryIsAllOrNothing()
	{
		// The parent rule fails only after its nested child was already added; the child must be rolled back.
		var sheet = StyleSheet.FromString( ".a { .child { width: 10px; } color: $typo; } .b { width: 5px; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( ".b", sheet.Nodes[0].Selectors[0].AsString );
	}

	/// <summary>
	/// An unknown at-rule like @media should be skipped, not discard the whole sheet.
	/// </summary>
	[TestMethod]
	public void RecoversFromUnknownAtRule()
	{
		var sheet = StyleSheet.FromString( ".a { width: 100%; } @media (min-width: 100px) { .x { width: 1px; } } .b { height: 10%; }" );

		Assert.IsNotNull( sheet );
		Assert.IsNotNull( sheet.Nodes.SingleOrDefault( x => x.Selectors.Any( y => y.AsString == ".a" ) ) );
		Assert.IsNotNull( sheet.Nodes.SingleOrDefault( x => x.Selectors.Any( y => y.AsString == ".b" ) ) );
	}

	/// <summary>
	/// Nested rules need a unique load order.
	/// </summary>
	[TestMethod]
	public void NestedBlocksHaveDistinctLoadOrder()
	{
		var sheet = StyleParser.ParseSheet( ".a { color: red; .b { color: blue; } }" );

		var a = sheet.Nodes.Single( x => x.SelectorStrings.Contains( ".a" ) );
		var b = sheet.Nodes.Single( x => x.SelectorStrings.Contains( ".a .b" ) );

		Assert.AreNotEqual( a.LoadOrder, b.LoadOrder );
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
