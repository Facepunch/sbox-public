using System;
using Sandbox.Rendering;

namespace UITests;

[TestClass]
public class PainterDestinationScopesTest
{
	static readonly Rect Bounds = new( 0, 0, 200, 200 );

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void NestedClipsRestoreTheirParent( bool rounded, bool invert )
	{
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( Bounds );
		context.Batcher.Destination.Scissor.Invert = invert;
		CheckNestedClips( painter, context.Batcher, rounded, 12 );
		Assert.AreEqual( 0, context.Batcher.Destination.Scissor.Count );
		Assert.AreEqual( invert, context.Batcher.Destination.Scissor.Invert );
	}

	static void CheckNestedClips( Painter painter, PainterBatcher batcher, bool rounded, int depth )
	{
		if ( depth == 0 ) return;

		var previous = batcher.Destination.Scissor;
		var spatial = batcher.Destination.ResolveSpatial( batcher, Matrix.Identity );
		var rect = Bounds.Shrink( 20 - depth );
		var radii = rounded ? new BorderRadii { TopLeft = new Vector2( depth, depth + 1 ) } : BorderRadii.Zero;
		var transform = rounded ? Matrix.CreateTranslation( new Vector3( depth, 0, 0 ) ) : Matrix.Identity;
		using ( painter.ClipDestination( rect, radii, transform ) )
		{
			var expected = previous;
			expected.Push( rect, radii, transform );
			Assert.IsTrue( expected.Equals( in batcher.Destination.Scissor ) );
			CheckNestedClips( painter, batcher, rounded, depth - 1 );
		}

		Assert.IsTrue( previous.Equals( in batcher.Destination.Scissor ) );
		Assert.AreEqual( spatial, batcher.Destination.ResolveSpatial( batcher, Matrix.Identity ) );
	}

	[TestMethod]
	public void TargetsRestoreTransformsAndClips()
	{
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( Bounds );
		var batcher = context.Batcher;
		batcher.Destination.Transform = Matrix.CreateRotationZ( 30 );
		batcher.Destination.WorldMatrix = Matrix.CreateScale( 2 );
		batcher.Destination.WorldPanelCombo = 1;
		batcher.Destination.PlaybackPaused = true;
		batcher.Destination.GammaOutput = false;
		using var outerClip = painter.ClipDestination( Bounds, new BorderRadii { TopLeft = new Vector2( 8 ) }, Matrix.CreateRotationZ( 10 ) );
		batcher.Destination.ResolveSpatial( batcher, Matrix.Identity );
		var previous = batcher.Destination;

		using ( painter.Target( "ScopeTest.Outer", Bounds.Shrink( 10 ) ) )
		{
			// Draws keep their screen transform and clips; the layer matrix cancels the transform onto the target.
			Assert.IsTrue( batcher.Destination.Layered );
			Assert.AreEqual( previous.Transform, batcher.Destination.Transform );
			Assert.AreEqual( previous.Transform.Inverted * Matrix.CreateTranslation( new Vector3( -10, -10, 0 ) ), batcher.Destination.LayerMatrix );
			Assert.AreEqual( Matrix.CreateRotationZ( 10 ), batcher.Destination.Scissor.Clips[0].Matrix );
			using var innerClip = painter.ClipDestination( Bounds.Shrink( 20 ), BorderRadii.Zero, Matrix.Identity );
			var inner = batcher.Destination;
			using ( painter.Target( "ScopeTest.Inner", Bounds.Shrink( 30 ) ) )
			{
				using var clip = painter.ClipDestination( Bounds.Shrink( 40 ), BorderRadii.Zero, Matrix.Identity );
			}
			AssertTarget( inner, batcher.Destination );
		}

		AssertTarget( previous, batcher.Destination );
	}

	static void AssertTarget( in PainterBatcher.Target expected, in PainterBatcher.Target actual )
	{
		Assert.AreEqual( expected.Transform, actual.Transform );
		Assert.AreEqual( expected.LayerMatrix, actual.LayerMatrix );
		Assert.AreEqual( expected.WorldMatrix, actual.WorldMatrix );
		Assert.AreEqual( expected.WorldPanelCombo, actual.WorldPanelCombo );
		Assert.AreEqual( expected.Layered, actual.Layered );
		Assert.AreEqual( expected.PlaybackPaused, actual.PlaybackPaused );
		Assert.AreEqual( expected.GammaOutput, actual.GammaOutput );
		Assert.IsTrue( expected.Scissor.Equals( in actual.Scissor ) );
	}

	[TestMethod]
	public void DefaultScopesDoNothing()
	{
		var clip = default( Painter.DestinationClipScope );
		var target = default( Painter.TargetScope );
		clip.Dispose();
		clip.Dispose();
		target.Dispose();
		target.Dispose();
	}

	[TestMethod]
	public void NestedScopeStacksRestoreTheirCheckpoint()
	{
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( Bounds );
		var batcher = context.Batcher;
		EnterScopes( painter, batcher, 64 );

		var checkpoint = batcher.GetCheckpoint();
		Assert.AreEqual( 0, checkpoint.Targets );
		Assert.AreEqual( 0, checkpoint.Clips );
	}

	static void EnterScopes( Painter painter, PainterBatcher batcher, int depth )
	{
		if ( depth == 0 ) return;

		using var clip = painter.ClipDestination( Bounds, BorderRadii.Zero, Matrix.Identity );
		int target = batcher.PushTarget();
		try
		{
			EnterScopes( painter, batcher, depth - 1 );
		}
		finally
		{
			batcher.PopTarget( target );
		}
	}

	[TestMethod]
	[DataRow( "reset" )]
	[DataRow( "restart" )]
	[DataRow( "end" )]
	public void OldScopesDoNotAffectNewRecordings( string operation )
	{
		var list = new CommandList();
		var context = new Painter.Context( list );
		var painter = context.Begin( Bounds );
		var oldTarget = painter.Target( "ScopeTest.Old", Bounds );
		var oldClip = painter.ClipDestination( Bounds.Shrink( 10 ), BorderRadii.Zero, Matrix.Identity );
		if ( operation == "reset" ) list.Reset();
		if ( operation == "end" ) painter.Dispose();

		using var next = context.Begin( Bounds );
		using var newTarget = next.Target( "ScopeTest.New", Bounds.Shrink( 20 ) );
		using var newClip = next.ClipDestination( Bounds.Shrink( 30 ), BorderRadii.Zero, Matrix.Identity );
		var expected = context.Batcher.Destination;
		oldClip.Dispose();
		oldTarget.Dispose();
		AssertTarget( expected, context.Batcher.Destination );
		Assert.AreEqual( 1, context.Batcher.GetCheckpoint().Targets );
		Assert.AreEqual( 1, context.Batcher.GetCheckpoint().Clips );
	}

	[TestMethod]
	public void NestedRecordingsKeepParentScopes()
	{
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( Bounds );
		using var destination = painter.WithDestination( Bounds, 1, 1, BlendMode.Normal, Matrix.Identity );
		using var clip = painter.ClipDestination( Bounds.Shrink( 10 ), BorderRadii.Zero, Matrix.Identity );
		using var target = painter.Target( "ScopeTest.Parent", Bounds );
		var expected = context.Batcher.Destination;
		using ( var nested = context.Begin( Bounds ) )
		{
			using var nestedTarget = nested.Target( "ScopeTest.Child", Bounds.Shrink( 20 ) );
			using var nestedClip = nested.ClipDestination( Bounds.Shrink( 30 ), BorderRadii.Zero, Matrix.Identity );
		}

		AssertTarget( expected, context.Batcher.Destination );
		Assert.AreEqual( 1, context.Batcher.GetCheckpoint().Targets );
		Assert.AreEqual( 1, context.Batcher.GetCheckpoint().Clips );
	}

	[TestMethod]
	public void ExceptionsRestoreDestinationScopes()
	{
		var context = new Painter.Context( new CommandList() );
		using var painter = context.Begin( Bounds );
		using var clip = painter.ClipDestination( Bounds, BorderRadii.Zero, Matrix.Identity );
		var previous = context.Batcher.Destination;
		try
		{
			using var target = painter.Target( "ScopeTest.Exception", Bounds.Shrink( 10 ) );
			using var nested = painter.ClipDestination( Bounds.Shrink( 20 ), BorderRadii.Zero, Matrix.Identity );
			throw new InvalidOperationException();
		}
		catch ( InvalidOperationException ) { }

		AssertTarget( previous, context.Batcher.Destination );
		Assert.AreEqual( 0, context.Batcher.GetCheckpoint().Targets );
		Assert.AreEqual( 1, context.Batcher.GetCheckpoint().Clips );
	}
}
