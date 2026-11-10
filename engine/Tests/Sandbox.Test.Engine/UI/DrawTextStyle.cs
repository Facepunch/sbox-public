using Sandbox.UI;
using System;

namespace UITests;

[TestClass]
public class DrawTextStyleTest : PainterTestBase
{
	[TestMethod]
	public void TextStyleCopiesAndScopes()
	{
		using var scope = Paint.Scope();
		var original = TextStyle.Default;
		var styled = original.WithFont( "Roboto Mono", 24 ).WithBold().WithItalic().WithColor( Color.Red )
			.WithAlignment( TextFlag.Center ).WithLineHeight( 1.25f ).WithLetterSpacing( 1 ).WithWordSpacing( 2 )
			.WithShadow( Color.Black, 3, 4, blur: 5 ).WithOutline( Color.Blue, 2 );
		Assert.AreEqual( TextStyle.Default, original );
		Assert.AreEqual( "Roboto Mono", styled.FontName );
		Assert.AreEqual( 24f, styled.FontSize );
		Assert.AreEqual( 700, styled.FontWeight );
		Assert.IsTrue( styled.Italic );
		Assert.AreEqual( new Vector2( 3, 4 ), styled.Shadow.Offset );
		Assert.AreEqual( 5f, styled.Shadow.Size );
		Assert.AreEqual( 2f, styled.Outline.Size );
		PaintTextStyle = styled;
		using ( Paint.Scope() )
		{
			PaintTextStyle = styled.WithSize( 18 ).WithBold( false ).WithItalic( false ).WithoutShadow().WithoutOutline();
			Assert.IsFalse( PaintTextStyle.Shadow.Enabled );
			Assert.IsFalse( PaintTextStyle.Outline.Enabled );
			Assert.AreEqual( 400, PaintTextStyle.FontWeight );
		}
		Assert.AreEqual( styled, PaintTextStyle );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintTextStyle = styled.WithShadow( blur: -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintTextStyle = styled.WithShadow( offset: new Vector2( float.NaN, 0 ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintTextStyle = styled.WithOutline( width: float.PositiveInfinity ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintTextStyle = styled.WithColor( new Color( float.NaN, 0, 0 ) ) );
	}

	[TestMethod]
	public void TextStyleValidatesValuesAndRestoresScopedChanges()
	{
		using var scope = Paint.Scope();
		PaintTextStyle = TextStyle.Default;
		Assert.AreEqual( "Roboto", PaintTextStyle.FontName );
		Assert.AreEqual( 16f, PaintTextStyle.FontSize );
		Assert.AreEqual( Color.White, PaintTextStyle.Color );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintTextStyle = TextStyle.Default with { FontSize = float.NaN } );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => PaintTextStyle = TextStyle.Default with { LineHeight = 0 } );
		using ( Paint.Scope() )
		{
			PaintTextStyle = PaintTextStyle with { FontSize = 20, FontWeight = 700, Color = Color.Red, LetterSpacing = 1 };
		}
		Assert.AreEqual( TextStyle.Default, PaintTextStyle );
	}
}
