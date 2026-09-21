using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	static T PrivateField<T>( object owner, string name ) =>
		(T)owner.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( owner );

	[TestMethod]
	public void ShadowCornerOverloadsMatch()
	{
		WithBuffer( layer =>
		{
			var bounds = new Rect( 10, 20, 60, 40 );
			foreach ( float radius in new[] { -2f, 0f, 8f, 200f, float.NaN } )
				foreach ( bool inset in new[] { false, true } )
				{
					layer.Clear();
					Paint.RectShadow( bounds, radius, color: Color.Black, blur: 4, inset: inset );
					var expected = layer.Instances.Single().GPU;
					layer.Clear();
					Paint.RectShadow( bounds, new Painter.CornerRadii( radius ), color: Color.Black, blur: 4, inset: inset );
					Assert.AreEqual( expected, layer.Instances.Single().GPU, $"Radius {radius}, inset {inset}" );
				}
		} );
	}

	[TestMethod]
	public void DrawingAndPanelTransformsCompose()
	{
		WithBuffer( layer =>
		{
			using var texture = Texture.Create( 8, 4 ).Finish();
			var rect = new Rect( -20, -10, 40, 20 );
			var gradient = Fill.LinearGradient( Color.Red, Color.Blue );
			void Draw()
			{
				PaintFill = gradient;
				PaintStroke = Stroke.Dashed( Color.White, 2, 6, 3 );
				Paint.Rect( rect, 4 );
				Paint.Circle( Vector2.Zero, 10 );
				Paint.Polygon( [new( -20, 10 ), new( 0, -10 ), new( 20, 10 )] );
				Paint.Line( rect.TopLeft, rect.BottomRight );
				PaintFill = Fill.Image( texture, width: 10, offsetX: 5, repeat: BackgroundRepeat.Repeat );
				Paint.Rect( rect );
				Paint.Texture( texture, rect );
				PaintFill = Color.Red;
				PaintStroke = Stroke.None;
				Paint.Rect( rect, 4 );
				PaintFill = Color.Blue;
				Paint.Circle( Vector2.Zero, 10 );
				PaintStroke = Stroke.Solid( Color.Green, 2 );
				Paint.Outline( rect, 4 );
				Paint.RectShadow( rect, 4, color: Color.Black, blur: 3, offset: new Vector2( 2, 3 ) );
				Paint.RectShadow( rect, 4, color: Color.Black, blur: 3, inset: true );
			}

			Draw();
			var original = layer.Instances.ToArray();
			var scissor = Painter.Scissoring.Single( new Rect( 0, 0, 500, 500 ), BorderRadii.Zero, Matrix.Identity );
			foreach ( var panelTransform in new[] { Matrix.Identity, Matrix.CreateScale( new Vector3( 2, 2, 1 ) ) * Matrix.CreateTranslation( new Vector3( 20, 40, 0 ) ) } )
			{
				layer.Clear();
				layer.Batcher.Destination.Transform = panelTransform;
				layer.Batcher.Destination.SetScissor( scissor );
				Matrix drawingTransform;
				using ( Paint.Scope() )
				{
					Paint.Translate( 100, 200 );
					Paint.Rotate( 30 );
					Paint.Scale( -2, 3 );
					drawingTransform = PaintTransform;
					Draw();
				}
				Assert.AreEqual( Matrix.Identity, PaintTransform );
				var instances = layer.Instances;
				Assert.AreEqual( original.Length, instances.Count );
				var clips = layer.Batcher.Scissors;
				for ( int i = 0; i < original.Length; i++ )
				{
					var instance = instances[i];
					var gpu = instance.GPU;
					Assert.AreEqual( original[i].GPU, gpu with
					{
						TransformIndex = original[i].GPU.TransformIndex,
						ScissorIndex = original[i].GPU.ScissorIndex,
						InverseScissorIndex = original[i].GPU.InverseScissorIndex
					}, "Geometry and texture coordinates stay in drawing space." );
					var combined = instance.Transform;
					Assert.AreEqual( drawingTransform * panelTransform, combined );
					var inheritedClip = clips[gpu.ScissorIndex];
					Assert.AreEqual( scissor.Clips[0].Rect.ToVector4(), inheritedClip.Clips[0].Rect );
					Assert.AreEqual( Matrix.Identity, inheritedClip.Clips[0].TransformMat );
					if ( gpu.InverseScissorIndex < 0 ) continue;
					var clip = clips[gpu.InverseScissorIndex];
					foreach ( var point in new[] { rect.TopLeft, rect.Center, rect.BottomRight } )
					{
						var local = clip.Clips[0].TransformMat.Transform( combined.Transform( point ) );
						Assert.AreEqual( point.x, local.x, 0.001f );
						Assert.AreEqual( point.y, local.y, 0.001f );
					}
				}
			}
		} );
	}

	[TestMethod]
	public void CollapsedTransformsDrawNothing()
	{
		WithBuffer( layer =>
		{
			PaintFill = Color.Red;
			PaintStroke = Stroke.Solid( Color.White, 2 );
			using ( Paint.Scope() )
			{
				Paint.Scale( 0, 1 );
				Paint.Rect( new Rect( 0, 0, 40, 20 ) );
				Paint.Outline( new Rect( 0, 0, 40, 20 ) );
				Paint.RectShadow( new Rect( 0, 0, 40, 20 ), color: Color.Black, blur: 5 );
			}
			Assert.AreEqual( 0, layer.Instances.Count );
			Paint.Rect( new Rect( 0, 0, 40, 20 ) );
			Assert.AreEqual( 2, layer.Instances.Count );
		} );
	}

	[TestMethod]
	public void VideoPresentationUsesEachDrawsTransformedBounds()
	{
		WithBuffer( layer =>
		{
			using var visible = new VideoPlayer();
			using var hidden = new VideoPlayer();
			var frame = Application.FrameCount;
			try
			{
				Application.FrameCount += 3;
				layer.Batcher.Destination.SetScissor( Painter.Scissoring.Single( new Rect( 0, 0, 100, 100 ), BorderRadii.Zero, Matrix.Identity ) );
				Paint.Texture( visible.Texture, new Rect( 0, 0, 20, 20 ) );
				Paint.Translate( 300, 0 );
				Paint.Texture( hidden.Texture, new Rect( 0, 0, 20, 20 ) );
				Assert.AreEqual( 0, visible.LastPresented );
				Assert.IsTrue( hidden.LastPresented > 2 );
			}
			finally
			{
				Application.FrameCount = frame;
			}
		} );
	}
}
