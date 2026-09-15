using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	/// <summary>Image lengths resolve for each shape in layout pixels, retaining aspect ratio for an automatic dimension.</summary>
	[TestMethod]
	public void ImageFillSizeAndOffset()
	{
		WithBuffer( layer =>
		{
			using var texture = Texture.Create( 8, 4 ).Finish();
			Fill[] fills = [
				texture,
				Fill.Image( texture, width: 100, offsetX: 50, repeat: BackgroundRepeat.Repeat ),
				Fill.Image( texture, height: Length.Pixels( 30 ), offsetY: -10 ),
				Fill.Image( texture, width: 100, height: 20 ),
				Fill.Image( texture, width: Length.Auto ),
				Fill.Image( texture, width: Length.Percent( 50 ), height: Length.Percent( 25 ),
					offsetX: Length.Percent( 25 ), offsetY: Length.Percent( -50 ) ),
				Fill.Image( texture, width: Length.Percent( 50 ), height: Length.Auto ),
				Fill.Image( texture, height: Length.Percent( 50 ) )];
			foreach ( var size in new[] { new Vector2( 200, 100 ), new Vector2( 400, 300 ) } )
				foreach ( float scale in new[] { 1f, 2f } )
				{
					layer.Clear();
					PaintContext.ScaleToScreen = scale;
					Vector4[] expected = [
						new( 0, 0, size.x, size.y ), new( 50, 0, 100, 50 ), new( 0, -10, 60, 30 ), new( 0, 0, 100, 20 ),
					new( 0, 0, 8, 4 ), new( size.x * 0.25f, size.y * -0.5f, size.x * 0.5f, size.y * 0.25f ),
					new( 0, 0, size.x * 0.5f, size.x * 0.25f ), new( 0, 0, size.y, size.y * 0.5f )];
					for ( int i = 0; i < fills.Length; i++ )
					{
						PaintFill = fills[i];
						Paint.Rect( new Rect( new Vector2( 30, 40 ), size ) );
						var instance = layer.Instances[i];
						Assert.AreEqual( expected[i], instance.GPU.BackgroundRect, $"Fill {i}, bounds {size}, scale {scale}" );
						Assert.AreSame( texture, instance.BackgroundImage );
						Assert.AreEqual( (int)(i == 1 ? BackgroundRepeat.Repeat : BackgroundRepeat.Clamp), instance.GPU.BackgroundRepeat );
					}
				}
		} );
	}

	/// <summary>Reject invalid lengths before they can produce undefined image sampling coordinates.</summary>
	[TestMethod]
	public void ImageFillValidatesLengths()
	{
		WithBuffer( layer =>
		{
			using var texture = Texture.Create( 8, 4 ).Finish();
			foreach ( float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity } )
			{
				Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, width: value ) );
				Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, height: Length.Percent( value ) ) );
			}
			foreach ( float value in new[] { float.NaN, float.NegativeInfinity } )
			{
				Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, offsetX: value ) );
				Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, offsetY: Length.Percent( value ) ) );
			}
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, width: Length.Cover ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, offsetX: Length.Auto ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, height: new Length { Value = 1, Unit = (LengthUnit)255 } ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, repeat: (BackgroundRepeat)(-1) ) );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.Image( texture, filter: (FilterMode)(-1) ) );
			foreach ( var width in new[] { Length.Pixels( float.Epsilon ), Length.Calc( "calc(100% - 300px)" ) } )
			{
				Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
				{
					PaintFill = Fill.Image( texture, width: width );
					Paint.Rect( new Rect( 0, 0, 200, 100 ) );
				} );
			}
			Assert.AreEqual( 0, layer.Instances.Count );
		} );
	}
}
