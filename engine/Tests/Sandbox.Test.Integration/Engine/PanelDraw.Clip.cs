using Sandbox.UI;
using System;
using System.Collections.Generic;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	[TestMethod]
	public void NestedClipsCaptureTransforms()
	{
		WithBuffer( layer =>
		{
			var bounds = new Rect( -30, -20, 60, 40 );
			var inherited = Painter.Scissoring.Single( new Rect( 0, 0, 800, 800 ), BorderRadii.Zero, Matrix.Identity );
			foreach ( var panelTransform in new[] { Matrix.Identity, Matrix.CreateTranslation( new Vector3( 15, 25, 0 ) ) } )
			{
				layer.Clear();
				layer.Batcher.Destination.Transform = panelTransform;
				layer.Batcher.Destination.SetScissor( inherited );
				PaintFill = Color.Red;
				Paint.Rect( bounds );
				using ( Paint.Scope() )
				{
					Paint.Translate( 100, 200 );
					for ( int i = 0; i < 12; i++ )
					{
						Paint.Rotate( 5 );
						Paint.Clip( bounds, 8 );
						Assert.AreEqual( PaintTransform.Inverted, layer.DrawClips[i].Matrix );
						Assert.AreEqual( i - 1, layer.DrawClips[i].Parent );
					}
					PaintTransform = Matrix.Identity;
					Paint.Rect( new Rect( 0, 0, 400, 400 ) );
					Paint.RectShadow( bounds, color: Color.Black, blur: 5 );
					int count = layer.Batcher.Scissors.Count;
					Paint.RectShadow( bounds, color: Color.Black, blur: 5 );
					Assert.AreEqual( count, layer.Batcher.Scissors.Count, "Repeated draws reuse the clip chain." );
				}
				Paint.Rect( bounds );
				var instances = layer.Batcher.Instances;
				Assert.AreEqual( instances[0].ScissorIndex, instances[^1].ScissorIndex );
				Assert.AreEqual( instances[1].ScissorIndex, instances[2].ScissorIndex );
				Assert.IsTrue( instances[2].InverseScissorIndex >= 0 );
				var clips = layer.Batcher.Scissors;
				int index = instances[2].ScissorIndex;
				for ( int i = 11; i >= 0; i-- )
				{
					var clip = clips[index];
					Assert.AreEqual( bounds.ToVector4(), clip.Clips[0].Rect );
					Assert.AreEqual( panelTransform.Inverted * layer.DrawClips[i].Matrix, clip.Clips[0].TransformMat );
					Assert.AreEqual( new Vector4( 8 ), clip.Clips[0].RadiiH );
					index = clip.Next;
				}
				Assert.AreEqual( inherited.Clips[0].Rect.ToVector4(), clips[index].Clips[0].Rect );
				Assert.AreEqual( -1, clips[index].Next );
			}
		} );
	}

	[TestMethod]
	public void EmptyClipsAndRebuiltLayersDoNotReuseOldClips()
	{
		WithBuffer( layer =>
		{
			var batcher = layer.Batcher;
			var clips = batcher.Scissors;
			PaintFill = Color.Red;
			foreach ( bool collapsed in new[] { false, true } )
			{
				layer.Clear();
				using ( Paint.Scope() )
				{
					if ( collapsed ) Paint.Scale( 0 );
					Paint.Clip( collapsed ? new Rect( 10, 20, 40, 50 ) : new Rect( 100, 200, -1, 30 ) );
					PaintTransform = Matrix.Identity;
					Paint.Rect( new Rect( 0, 0, 400, 400 ) );
					var gpu = layer.Batcher.Instances[0];
					var clip = clips[gpu.ScissorIndex].Clips[0];
					Assert.IsTrue( clip.Rect.z <= clip.Rect.x || clip.Rect.w <= clip.Rect.y );
					Assert.AreEqual( layer.DrawClips[0].Rect.ToVector4(), clip.Rect );
				}
			}
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Paint.Clip( new Rect( float.NaN, 0, 20, 20 ) ) );
			Assert.AreEqual( -1, PaintContext.State.ClipIndex );
		} );
	}

	[TestMethod]
	public void ClipScopeRestoresThePreviousClip()
	{
		WithBuffer( layer =>
		{
			using ( Paint.Scope() )
			{
				Paint.Clip( new Rect( 0, 0, 100, 100 ), 4 );
				Assert.AreEqual( 0, PaintContext.State.ClipIndex );
			}
			Assert.AreEqual( -1, PaintContext.State.ClipIndex );
		} );
	}
}
