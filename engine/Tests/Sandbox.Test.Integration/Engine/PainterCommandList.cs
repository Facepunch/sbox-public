using Sandbox.Rendering;
using System;

#pragma warning disable CS0618 // These tests verify interoperability with the legacy HudPainter API.

namespace EngineTests;

[TestClass]
public class PainterCommandListTest
{
	[TestMethod]
	public void DestinationTablesFollowScopesAndReset()
	{
		var list = new CommandList();
		var bounds = new Rect( 0, 0, 100, 100 );
		var moved = Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) );
		for ( int frame = 0; frame < 2; frame++ )
		{
			list.Reset();
			using var painter = Painter.Begin( list, bounds );
			painter.SetViewport( bounds );
			painter.Fill = Color.Red;
			painter.Rect( bounds );
			using ( var destination = painter.WithDestination( bounds, 1, 1, BlendMode.Normal, moved ) )
			{
				var child = destination.Painter;
				child.Fill = Color.Red;
				child.Rect( bounds );
				using ( child.ClipDestination( bounds.Shrink( 10 ), BorderRadii.Zero, Matrix.Identity ) )
				{
					child.Rect( bounds );
					using ( child.Scope() )
					{
						child.Translate( 5, 0 );
						child.Clip( bounds.Shrink( 20 ) );
						child.Rect( bounds );
					}
					child.Rect( bounds );
				}
				child.Rect( bounds );
			}
			painter.Fill = Color.Red;
			painter.Rect( bounds );

			var instances = list.FindResource<Painter.Context>().Batcher.Instances;
			Assert.AreEqual( 7, instances.Count );
			Assert.AreEqual( instances[0], instances[6] );
			Assert.AreEqual( instances[1], instances[5] );
			Assert.AreEqual( instances[2], instances[4] );
			Assert.AreNotEqual( instances[1].ScissorIndex, instances[2].ScissorIndex );
			Assert.AreNotEqual( instances[2].ScissorIndex, instances[3].ScissorIndex );
			Assert.AreEqual( Matrix.Identity, Transform( list, instances[0] ) );
			Assert.AreEqual( moved, Transform( list, instances[1] ) );
			Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 5, 0, 0 ) ) * moved, Transform( list, instances[3] ) );
		}
		list.Reset();
	}

	[TestMethod]
	public void TextureSamplingDefaults()
	{
		var list = new CommandList();
		var rect = new Rect( 0, 0, 32, 32 );
		using ( var painter = Painter.Begin( list, rect ) )
		{
			painter.Texture( Texture.White, rect );
			painter.Fill = Fill.Image( Texture.White );
			painter.Rect( rect );
		}
		new HudPainter( list ).DrawTexture( Texture.White, rect );
		using var inspection = Painter.Begin( list, rect );
		var recorded = inspection.ActiveContext.Batcher.Instances;
		Assert.AreEqual( 3, recorded.Count );
		Assert.AreEqual( recorded[1].SamplerIndex, recorded[0].SamplerIndex );
		Assert.AreEqual( SamplerState.GetBindlessIndex( new SamplerState
		{
			AddressModeU = TextureAddressMode.Clamp,
			AddressModeV = TextureAddressMode.Clamp,
			Filter = FilterMode.Anisotropic
		} ), recorded[2].SamplerIndex );
	}

	[TestMethod]
	public void LegacyHudLineResetsMatrix()
	{
		var list = new CommandList();
		var hud = new HudPainter( list );
		hud.SetMatrix( Matrix.CreateTranslation( new Vector3( 10, 20, 30 ) ) );
		hud.DrawLine( new Vector2( 0, 10 ), new Vector2( 20, 10 ), 2, Color.Red );
		hud.DrawRect( new Rect( 0, 0, 20, 20 ), Color.Blue );
		using var painter = Painter.Begin( list, new Rect( 0, 0, 100, 100 ) );
		var recorded = painter.ActiveContext.Batcher.Instances;
		Assert.AreEqual( 2, recorded.Count );
		Assert.AreEqual( Matrix.Identity, Transform( list, recorded[0] ) );
		Assert.AreEqual( Matrix.Identity, Transform( list, recorded[1] ) );
	}

	[TestMethod]
	public void LegacyHudSupportsGeneralMatrices()
	{
		var list = new CommandList();
		var hud = new HudPainter( list );
		Matrix[] transforms =
		[
			Matrix.CreateScale( new Vector3( 2, 2, 2 ) ),
			Matrix.CreateRotationY( 30 ),
			Matrix.CreateTranslation( new Vector3( 10, 20, 30 ) )
		];
		foreach ( var transform in transforms )
		{
			list.Reset();
			hud.SetMatrix( transform );
			hud.DrawRect( new Rect( 0, 0, 20, 20 ), Color.Red );
			hud.DrawTexture( Texture.White, new Rect( 20, 0, 20, 20 ) );
			using var painter = Painter.Begin( list, new Rect( 0, 0, 100, 100 ) );
			var recorded = painter.ActiveContext.Batcher.Instances;
			Assert.AreEqual( 2, recorded.Count );
			Assert.AreEqual( transform, Transform( list, recorded[0] ) );
			Assert.AreEqual( transform, Transform( list, recorded[1] ) );
			Assert.AreEqual( Matrix.Identity, painter.Transform );
			bool rejected = false;
			try { painter.Transform = transform; }
			catch ( ArgumentException ) { rejected = true; }
			Assert.IsTrue( rejected, "Painter's public transform remains 2D." );
		}
	}

	[TestMethod]
	public void CommandListRecordingOrder()
	{
		var list = new CommandList();
		PainterBatcher layer = null;
		using ( var painter = Painter.Begin( list, new Rect( 0, 0, 100, 100 ) ) )
		{
			painter.Fill = Color.Red;
			painter.Rect( painter.Bounds );
			layer = painter.ActiveContext.Batcher;
			var resetFromAnotherThread = Task.Run( () => Assert.ThrowsException<InvalidOperationException>( list.Reset ) );
			Assert.IsTrue( resetFromAnotherThread.Wait( TimeSpan.FromSeconds( 5 ) ), "Painting must not hold the command list lock." );
		}
		using ( var painter = Painter.Begin( list, new Rect( 0, 0, 100, 100 ) ) )
		{
			painter.Fill = Color.Blue;
			painter.Rect( painter.Bounds );
			Assert.ThrowsException<InvalidOperationException>( list.Execute );
		}
		Assert.AreEqual( 2, layer.Instances.Count );
		Assert.AreEqual( Color.Red, layer.Instances[0].Color );
		Assert.AreEqual( Color.Blue, layer.Instances[1].Color );
		list.Reset();
		Assert.AreEqual( 0, layer.Instances.Count );
	}

	[TestMethod]
	public void ExceptionsEndCommandListRecording()
	{
		var list = new CommandList();
		Painter.Context context = null;
		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			using var painter = Painter.Begin( list, default );
			context = painter.ActiveContext;
			painter.Fill = Color.Red;
			throw new InvalidOperationException();
		} );
		Assert.IsFalse( context.IsPainting );
		list.Reset();
		using var next = Painter.Begin( list, default );
		Assert.AreEqual( Fill.None, next.Fill );
	}

	[TestMethod]
	public void PanelAndLayerPaintersCannotClearTheDestination()
	{
		var commands = new CommandList();
		var bounds = new Rect( 0, 0, 64, 64 );
		using var painter = Painter.Begin( commands, bounds );
		using ( var destination = painter.WithDestination( bounds, 1, 1, BlendMode.Normal, Matrix.Identity ) )
		{
			bool panelRejected = false;
			try { destination.Painter.Clear( Color.Red ); }
			catch ( InvalidOperationException ) { panelRejected = true; }
			Assert.IsTrue( panelRejected );
		}

		using var layer = painter.BeginLayer( bounds );
		bool rejected = false;
		try { painter.Clear( Color.Red ); }
		catch ( InvalidOperationException ) { rejected = true; }
		Assert.IsTrue( rejected );
	}

	[TestMethod]
	public void BeginReplacesUnfinishedDrawing()
	{
		var list = new CommandList();
		var bounds = new Rect( 0, 0, 100, 100 );
		using ( var first = Painter.Begin( list, bounds ) )
		{
			first.Fill = Color.Red;
			first.Rect( bounds );
		}

		var abandoned = Painter.Begin( list, bounds );
		abandoned.Fill = Color.Blue;
		abandoned.Clip( bounds.Shrink( 10 ) );
		abandoned.Rect( bounds );
		var scope = abandoned.Scope();
		var layer = abandoned.BeginLayer( bounds );
		abandoned.Rect( bounds );

		using var next = Painter.Begin( list, bounds );
		next.Fill = Color.Green;
		layer.Dispose();
		scope.Dispose();
		abandoned.Dispose();
		AssertDisposed( abandoned );
		Assert.AreEqual( Fill.Solid( Color.Green ), next.Fill );
		next.Rect( bounds );

		var commands = next.ActiveContext.Batcher;
		Assert.AreEqual( 0, commands.DrawClips.Count );
		Assert.AreEqual( 2, commands.Instances.Count );
		Assert.AreEqual( Color.Red, commands.Instances[0].Color );
		Assert.AreEqual( Color.Green, commands.Instances[1].Color );
	}

	[TestMethod]
	public void ResetDiscardsUnfinishedDrawing()
	{
		var list = new CommandList();
		var bounds = new Rect( 0, 0, 100, 100 );
		var painter = Painter.Begin( list, bounds );
		painter.Fill = Color.Red;
		painter.Rect( bounds );
		var commands = painter.ActiveContext.Batcher;
		var layer = painter.BeginLayer( bounds );
		painter.Fill = Color.Blue;
		painter.Rect( bounds );

		list.Reset();
		Assert.AreEqual( 0, commands.Instances.Count );
		AssertDisposed( painter );

		using var next = Painter.Begin( list, bounds );
		next.Fill = Color.Green;
		layer.Dispose();
		painter.Dispose();
		next.Rect( bounds );
		Assert.AreEqual( 1, commands.Instances.Count );
		Assert.AreEqual( Color.Green, commands.Instances[0].Color );
	}

	[TestMethod]
	public void LegacyStateResetsWithTheCommandList()
	{
		var list = new CommandList();
		var hud = list.Paint;
		hud.SetMatrix( Matrix.CreateTranslation( new Vector3( 10, 20, 30 ) ) );
		hud.SetBlendMode( BlendMode.Lighten );
		list.Reset();
		hud.DrawRect( new Rect( 0, 0, 20, 20 ), Color.Red );

		using var painter = Painter.Begin( list );
		var instance = painter.ActiveContext.Batcher.Instances.Single();
		Assert.AreEqual( Matrix.Identity, Transform( list, instance ) );
		Assert.AreEqual( BlendMode.Normal, list.FindResource<Painter.Context>().LegacyBlendMode );
		Assert.AreEqual( new Rect( Vector2.Zero, Screen.Size ), painter.Bounds );
	}

	[TestMethod]
	public void CommandListContextsDoNotKeepListsAlive()
	{
		var reference = CreateTemporaryList();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.IsFalse( reference.IsAlive );
	}

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
	static WeakReference CreateTemporaryList()
	{
		var list = new CommandList();
		using var painter = Painter.Begin( list, default );
		return new WeakReference( list );
	}

	static void AssertDisposed( Painter painter )
	{
		try
		{
			_ = painter.Bounds;
		}
		catch ( ObjectDisposedException )
		{
			return;
		}

		Assert.Fail( "A cancelled painter must reject drawing access." );
	}
	static Matrix Transform( CommandList list, UICssBoxBatched.BoxInstance instance )
	{
		var batcher = list.FindResource<Painter.Context>().Batcher;
		return batcher.Transforms[instance.TransformIndex].Mat;
	}

}
