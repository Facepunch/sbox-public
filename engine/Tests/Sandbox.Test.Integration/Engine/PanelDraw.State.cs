using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	/// <summary>Nested scopes restore every drawing field without changing the destination or discarding recorded commands.</summary>
	[TestMethod]
	public void ScopeRestoresCompleteNestedDrawState()
	{
		WithBuffer( layer =>
		{
			var buffer = PaintContext;
			buffer.State.Fill = Color.Red;
			buffer.State.Stroke = Stroke.Solid( Color.Blue, 2 );
			var original = buffer.State;
			var rect = new Rect( 10, 20, 60, 40 );
			Paint.Rect( rect );
			using ( Paint.Scope() )
			{
				buffer.State = new()
				{
					Opacity = 0.5f,
					OverrideBlendMode = BlendMode.Multiply,
					Transform = Matrix.CreateScale( 2 ),
					Fill = Color.Green,
					Stroke = Stroke.Dotted( Color.White, 4 ),
				};
				var inner = buffer.State;
				Paint.Rect( rect );
				Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
				using ( Paint.Scope() )
				{
					buffer.State = new() { Fill = Color.Yellow };
					Paint.Rect( rect );
				}
				Assert.AreEqual( inner, buffer.State );
				Paint.Rect( rect );
				Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
			}
			Assert.AreEqual( original, buffer.State );
			Paint.Rect( rect );
			Assert.AreSame( layer.Batcher, buffer.Batcher );
			Assert.AreEqual( 2f, buffer.ScaleToScreen );
			CollectionAssert.AreEqual( new[] { Color.Red, Color.Blue,
				Color.Green.WithAlpha( 0.5f ), Color.White.WithAlpha( 0.5f ), Color.Yellow,
				Color.Green.WithAlpha( 0.5f ), Color.White.WithAlpha( 0.5f ), Color.Red, Color.Blue },
				layer.Instances.Select( i => i.GPU.Color ).ToArray() );
			Assert.AreEqual( BlendMode.Normal, layer.BlendMode );
		} );
	}

	/// <summary>A helper can return or throw after drawing without leaking its paint into its caller.</summary>
	[TestMethod]
	public void ScopeRestoresStateOnReturnAndException()
	{
		WithBuffer( layer =>
		{
			PaintFill = Color.Red;
			PaintStroke = Stroke.Solid( Color.Blue, 2 );
			var original = PaintContext.State;
			foreach ( bool throwAfterDrawing in new[] { false, true } )
			{
				layer.Clear();
				if ( throwAfterDrawing ) Assert.ThrowsException<InvalidOperationException>( () => DrawScopedHelper( true ) );
				else DrawScopedHelper( false );
				Assert.AreEqual( original, PaintContext.State );
				Paint.Rect( new Rect( 10, 20, 60, 40 ) );
				CollectionAssert.AreEqual( new[] { Color.Green, Color.Red, Color.Blue }, layer.Instances.Select( i => i.GPU.Color ).ToArray() );
			}
		} );
	}

	void DrawScopedHelper( bool throwAfterDrawing )
	{
		using var scope = Paint.Scope();
		PaintFill = Color.Green;
		PaintStroke = Stroke.None;
		Paint.Translate( 30, 40 );
		PaintTextStyle = TextStyle.Default with { FontSize = 30 };
		Paint.Rect( new Rect( 10, 20, 60, 40 ) );
		if ( throwAfterDrawing ) throw new InvalidOperationException( "Expected failure in drawing scope test." );
	}

	/// <summary>Disposing a default scope or disposing the same variable twice must not overwrite later state.</summary>
	[TestMethod]
	public void DrawStateScopeDisposesOnce()
	{
		WithBuffer( layer =>
		{
			PaintFill = Color.Red;
			var original = PaintContext.State;
			var empty = default( Painter.StateScope );
			empty.Dispose();
			Assert.AreEqual( original, PaintContext.State );
			var scope = Paint.Scope();
			PaintFill = Color.Green;
			scope.Dispose();
			Assert.AreEqual( original, PaintContext.State );
			PaintFill = Color.Blue;
			scope.Dispose();
			Assert.AreEqual( Fill.Solid( Color.Blue ), PaintFill );
		} );
	}
}
