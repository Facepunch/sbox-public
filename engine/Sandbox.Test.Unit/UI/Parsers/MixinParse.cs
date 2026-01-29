using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class MixinParse
{
	[TestMethod]
	public void BasicMixin()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin button { background-color: red; padding: 8px; }" +
			".btn { @include button; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Mixins.Count );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var mixin = sheet.Mixins["button"];
		Assert.IsNotNull( mixin );
		Assert.AreEqual( "button", mixin.Name );
		Assert.AreEqual( 0, mixin.Parameters.Count );

		var btn = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "red" ), btn.Styles.BackgroundColor );
	}

	[TestMethod]
	public void MixinWithParameters()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin button($bg-color, $padding) { background-color: $bg-color; padding: $padding; }" +
			".btn { @include button(blue, 16px); }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Mixins.Count );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var mixin = sheet.Mixins["button"];
		Assert.AreEqual( 2, mixin.Parameters.Count );
		Assert.AreEqual( "bg-color", mixin.Parameters[0].Name );
		Assert.AreEqual( "padding", mixin.Parameters[1].Name );
		Assert.IsNull( mixin.Parameters[0].DefaultValue );
		Assert.IsNull( mixin.Parameters[1].DefaultValue );

		var btn = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "blue" ), btn.Styles.BackgroundColor );
		Assert.IsTrue( btn.Styles.PaddingLeft.HasValue );
		Assert.AreEqual( 16, btn.Styles.PaddingLeft.Value.Value );
	}

	[TestMethod]
	public void MixinWithDefaultParameters()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin button($bg-color, $padding: 8px, $radius: 4px) { " +
			"	background-color: $bg-color; " +
			"	padding: $padding; " +
			"	border-radius: $radius; " +
			"}" +
			".btn { @include button(green); }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var mixin = sheet.Mixins["button"];
		Assert.AreEqual( 3, mixin.Parameters.Count );
		Assert.IsNull( mixin.Parameters[0].DefaultValue );
		Assert.AreEqual( "8px", mixin.Parameters[1].DefaultValue );
		Assert.AreEqual( "4px", mixin.Parameters[2].DefaultValue );

		var btn = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "green" ), btn.Styles.BackgroundColor );
		Assert.AreEqual( 8, btn.Styles.PaddingLeft.Value.Value );
		Assert.AreEqual( 4, btn.Styles.BorderTopLeftRadius.Value.Value );
	}

	[TestMethod]
	public void MixinWithNamedArguments()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin button($bg-color, $padding: 8px, $radius: 4px) { " +
			"	background-color: $bg-color; " +
			"	padding: $padding; " +
			"	border-radius: $radius; " +
			"}" +
			".btn { @include button($radius: 16px, $bg-color: yellow); }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var btn = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "yellow" ), btn.Styles.BackgroundColor );
		Assert.AreEqual( 8, btn.Styles.PaddingLeft.Value.Value ); // default
		Assert.AreEqual( 16, btn.Styles.BorderTopLeftRadius.Value.Value ); // overridden
	}

	[TestMethod]
	public void MixinWithNestedSelectors()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin interactive { " +
			"	cursor: pointer; " +
			"	&:hover { opacity: 0.8; } " +
			"}" +
			".card { @include interactive; background-color: white; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );

		var card = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".card" ) );
		Assert.IsNotNull( card );
		Assert.AreEqual( Color.Parse( "white" ), card.Styles.BackgroundColor );

		var cardHover = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".card:hover" ) );
		Assert.IsNotNull( cardHover );
		Assert.AreEqual( 0.8f, cardHover.Styles.Opacity );
	}

	[TestMethod]
	public void MixinWithChildSelectors()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin card-with-icon { " +
			"	padding: 16px; " +
			"	.icon { width: 24px; height: 24px; } " +
			"}" +
			".card { @include card-with-icon; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );

		var card = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".card" ) );
		Assert.IsNotNull( card );
		Assert.AreEqual( 16, card.Styles.PaddingLeft.Value.Value );

		var icon = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".card .icon" ) );
		Assert.IsNotNull( icon );
		Assert.AreEqual( 24, icon.Styles.Width.Value.Value );
		Assert.AreEqual( 24, icon.Styles.Height.Value.Value );
	}

	[TestMethod]
	public void MixinWithContentBlock()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin on-hover { " +
			"	&:hover { @content; } " +
			"}" +
			".btn { " +
			"	background-color: blue; " +
			"	@include on-hover { " +
			"		background-color: red; " +
			"		transform: scale(1.05); " +
			"	} " +
			"}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );

		var btn = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".btn" ) );
		Assert.IsNotNull( btn );
		Assert.AreEqual( Color.Parse( "blue" ), btn.Styles.BackgroundColor );

		var btnHover = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".btn:hover" ) );
		Assert.IsNotNull( btnHover );
		Assert.AreEqual( Color.Parse( "red" ), btnHover.Styles.BackgroundColor );
	}

	[TestMethod]
	public void MixinWithContentBlockAndParameters()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin respond-to($size) { " +
			"	width: $size; " +
			"	&:active { @content; } " +
			"}" +
			".box { " +
			"	@include respond-to(200px) { " +
			"		opacity: 0.5; " +
			"	} " +
			"}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );

		var box = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".box" ) );
		Assert.IsNotNull( box );
		Assert.AreEqual( 200, box.Styles.Width.Value.Value );

		var boxActive = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".box:active" ) );
		Assert.IsNotNull( boxActive );
		Assert.AreEqual( 0.5f, boxActive.Styles.Opacity );
	}

	[TestMethod]
	public void VariadicParameters()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin box-shadow($shadows...) { " +
			"	box-shadow: $shadows; " +
			"}" +
			".elevated { @include box-shadow(0 2px 4px rgba(0, 0, 0, 0.1), 0 4px 8px rgba(0, 0, 0, 0.2)); }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Mixins.Count );

		var mixin = sheet.Mixins["box-shadow"];
		Assert.AreEqual( 1, mixin.Parameters.Count );
		Assert.IsTrue( mixin.Parameters[0].IsVariadic );
		Assert.IsTrue( mixin.HasVariadicParameter );

		Assert.AreEqual( 1, sheet.Nodes.Count );
		var elevated = sheet.Nodes[0];
		Assert.IsTrue( elevated.Styles.BoxShadow.Count > 0 );
	}

	[TestMethod]
	public void VariadicWithPrecedingParams()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin text-style($color, $sizes...) { " +
			"	color: $color; " +
			"}" +
			".text { @include text-style(red, 12px, 14px, 16px); }" );

		Assert.IsNotNull( sheet );

		var mixin = sheet.Mixins["text-style"];
		Assert.AreEqual( 2, mixin.Parameters.Count );
		Assert.IsFalse( mixin.Parameters[0].IsVariadic );
		Assert.IsTrue( mixin.Parameters[1].IsVariadic );

		var text = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "red" ), text.Styles.FontColor );
	}

	[TestMethod]
	public void TopLevelInclude()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin utility-classes { " +
			"	.mt-1 { margin-top: 4px; } " +
			"	.mt-2 { margin-top: 8px; } " +
			"}" +
			"@include utility-classes;" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 2, sheet.Nodes.Count );

		var mt1 = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".mt-1" ) );
		Assert.IsNotNull( mt1 );
		Assert.AreEqual( 4, mt1.Styles.MarginTop.Value.Value );

		var mt2 = sheet.Nodes.Single( x => x.Selectors.Any( y => y.AsString == ".mt-2" ) );
		Assert.IsNotNull( mt2 );
		Assert.AreEqual( 8, mt2.Styles.MarginTop.Value.Value );
	}

	[TestMethod]
	public void NestedMixinIncludes()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin reset { margin: 0; padding: 0; }" +
			"@mixin box($size) { @include reset; width: $size; height: $size; }" +
			".square { @include box(100px); }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var square = sheet.Nodes[0];
		Assert.AreEqual( 0, square.Styles.MarginLeft.Value.Value );
		Assert.AreEqual( 0, square.Styles.PaddingLeft.Value.Value );
		Assert.AreEqual( 100, square.Styles.Width.Value.Value );
		Assert.AreEqual( 100, square.Styles.Height.Value.Value );
	}

	[TestMethod]
	public void MultipleMixinIncludes()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin size($w, $h) { width: $w; height: $h; }" +
			"@mixin colors($bg, $fg) { background-color: $bg; color: $fg; }" +
			".card { @include size(200px, 100px); @include colors(white, black); }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var card = sheet.Nodes[0];
		Assert.AreEqual( 200, card.Styles.Width.Value.Value );
		Assert.AreEqual( 100, card.Styles.Height.Value.Value );
		Assert.AreEqual( Color.Parse( "white" ), card.Styles.BackgroundColor );
		Assert.AreEqual( Color.Parse( "black" ), card.Styles.FontColor );
	}

	[TestMethod]
	public void MixinWithVariables()
	{
		var sheet = StyleParser.ParseSheet(
			"$primary: #3498db;" +
			"$spacing: 16px;" +
			"@mixin button { background-color: $primary; padding: $spacing; }" +
			".btn { @include button; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var btn = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "#3498db" ), btn.Styles.BackgroundColor );
		Assert.AreEqual( 16, btn.Styles.PaddingLeft.Value.Value );
	}

	[TestMethod]
	public void MixinPreservesOtherStyles()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin base { padding: 8px; }" +
			".box { " +
			"	background-color: red; " +
			"	@include base; " +
			"	margin: 16px; " +
			"}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var box = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "red" ), box.Styles.BackgroundColor );
		Assert.AreEqual( 8, box.Styles.PaddingLeft.Value.Value );
		Assert.AreEqual( 16, box.Styles.MarginLeft.Value.Value );
	}

	[TestMethod]
	public void ContentBlockEmpty()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin wrapper { " +
			"	padding: 8px; " +
			"	@content; " +
			"}" +
			".box { @include wrapper; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var box = sheet.Nodes[0];
		Assert.AreEqual( 8, box.Styles.PaddingLeft.Value.Value );
	}

	[TestMethod]
	public void MixinCaseInsensitive()
	{
		var sheet = StyleParser.ParseSheet(
			"@mixin MyButton { background-color: red; }" +
			".btn { @include mybutton; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var btn = sheet.Nodes[0];
		Assert.AreEqual( Color.Parse( "red" ), btn.Styles.BackgroundColor );
	}

	// Error cases

	[TestMethod]
	public void InvalidMixinDefinition()
	{
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@mixin { }" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@mixin name" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@mixin name(" ); } );
	}

	[TestMethod]
	public void UnknownMixin()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			StyleParser.ParseSheet( ".btn { @include nonexistent; }" );
		} );
	}

	[TestMethod]
	public void MissingRequiredParameter()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			StyleParser.ParseSheet(
				"@mixin button($color) { background-color: $color; }" +
				".btn { @include button; }" );
		} );
	}

	[TestMethod]
	public void TooManyArguments()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			StyleParser.ParseSheet(
				"@mixin button($color) { background-color: $color; }" +
				".btn { @include button(red, blue, green); }" );
		} );
	}

	[TestMethod]
	public void PositionalAfterNamed()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			StyleParser.ParseSheet(
				"@mixin button($a, $b) { width: $a; height: $b; }" +
				".btn { @include button($a: 10px, 20px); }" );
		} );
	}

	[TestMethod]
	public void VariadicNotLast()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			StyleParser.ParseSheet( "@mixin test($a..., $b) { }" );
		} );
	}

	[TestMethod]
	public void ParameterWithoutDollar()
	{
		Assert.ThrowsException<System.Exception>( () =>
		{
			StyleParser.ParseSheet( "@mixin test(color) { }" );
		} );
	}
}
