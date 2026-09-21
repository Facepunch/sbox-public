using System;
using Sandbox.Rendering;

namespace EngineTests;

[TestClass]
public class SpriteInstanceTest
{
	static Sprite CreateSprite( Sprite.LoopMode loop = Sprite.LoopMode.Loop ) => new()
	{
		Animations =
		[
			new() { Name = "Run", FrameRate = 4, LoopMode = loop, Frames = [new(), new(), new(), new()] },
			new() { Name = "Idle", FrameRate = 4, Frames = [new()] }
		]
	};

	[TestMethod]
	public void InstancesAreIndependentAndPreserveElapsedTime()
	{
		var resource = CreateSprite();
		var first = new SpriteInstance( resource );
		var second = new SpriteInstance( resource );
		first.Update( 0.625f );
		Assert.AreEqual( 2, first.CurrentFrameIndex );
		Assert.AreEqual( 0, second.CurrentFrameIndex );
		first.Update( 0.125f );
		Assert.AreEqual( 3, first.CurrentFrameIndex );
		Assert.AreSame( resource.Animations[0].Frames[3], first.Frame );
	}

	[TestMethod]
	public void PlaySeekPauseAndReplaceResource()
	{
		var instance = new SpriteInstance( CreateSprite() );
		instance.CurrentFrameIndex = 2;
		instance.Update( 0.25f );
		Assert.AreEqual( 3, instance.CurrentFrameIndex );
		instance.Play( "run" );
		Assert.AreEqual( 3, instance.CurrentFrameIndex );
		instance.Play( "missing" );
		Assert.AreEqual( "Run", instance.Animation.Name );
		instance.Play( "Run", restart: true );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
		instance.Update( 0.125f );
		instance.Paused = true;
		instance.Update( 1 );
		instance.Paused = false;
		instance.Update( 0.125f );
		Assert.AreEqual( 1, instance.CurrentFrameIndex );
		instance.Play( "Idle" );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
		instance.Sprite = CreateSprite();
		Assert.AreEqual( "Run", instance.Animation.Name );
		instance.Sprite = null;
		instance.Update( 1 );
		Assert.IsNull( instance.Frame );
	}

	[TestMethod]
	public void LoopPointsPingPongAndReverse()
	{
		var resource = CreateSprite();
		resource.Animations[0].LoopStart = 1;
		resource.Animations[0].LoopEnd = 2;
		var instance = new SpriteInstance( resource );
		instance.Update( 0.75f );
		Assert.AreEqual( 1, instance.CurrentFrameIndex );
		resource.Animations[0].LoopMode = Sprite.LoopMode.PingPong;
		instance.Play( "Run", restart: true );
		instance.Update( 0.75f );
		Assert.AreEqual( 1, instance.CurrentFrameIndex );
		instance.Update( 0.25f );
		Assert.AreEqual( 2, instance.CurrentFrameIndex );
		instance.PlaybackSpeed = -1;
		instance.CurrentFrameIndex = 2;
		instance.Update( 0.25f );
		Assert.AreEqual( 1, instance.CurrentFrameIndex );
	}

	[TestMethod]
	public void NonLoopingPlaybackFinishesAndCanRestart()
	{
		var instance = new SpriteInstance( CreateSprite( Sprite.LoopMode.None ) );
		instance.Update( 1 );
		Assert.IsTrue( instance.IsFinished );
		Assert.AreEqual( 3, instance.CurrentFrameIndex );
		instance.Update( 1 );
		Assert.AreEqual( 3, instance.CurrentFrameIndex );
		instance.Play( "Run", restart: true );
		Assert.IsFalse( instance.IsFinished );
		instance.CurrentFrameIndex = 3;
		instance.PlaybackSpeed = -2;
		instance.Update( 0.5f );
		Assert.IsTrue( instance.IsFinished );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
	}

	[TestMethod]
	public void PainterDoesNotAdvancePlaybackOrChangeState()
	{
		var instance = new SpriteInstance( CreateSprite() );
		instance.CurrentFrameIndex = 2;
		using var painter = Painter.Begin( new CommandList(), new Rect( 0, 0, 100, 100 ) );
		painter.Opacity = 0.5f;
		painter.Sprite( instance, painter.Bounds );
		painter.Sprite( instance, painter.Bounds );
		Assert.AreEqual( 2, instance.CurrentFrameIndex );
		Assert.AreEqual( 0.5f, painter.Opacity );
		instance.CurrentFrameIndex = 99;
		Assert.IsNull( instance.Texture );
		painter.Sprite( instance, painter.Bounds );
	}

	[TestMethod]
	public void EmptyStaticAndStoppedAnimations()
	{
		var resource = CreateSprite();
		var instance = new SpriteInstance( resource );
		instance.PlaybackSpeed = 0;
		instance.Update( 1 );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
		instance.PlaybackSpeed = 1;
		instance.Play( "Idle" );
		instance.Update( 1 );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
		Assert.IsFalse( instance.IsFinished );
		instance.Animation.LoopMode = Sprite.LoopMode.None;
		instance.Update( 0.25f );
		Assert.IsTrue( instance.IsFinished );
		instance.Play( "Run" );
		instance.Animation.Frames.Clear();
		instance.Update( 1 );
		Assert.IsNull( instance.Frame );
	}

	[TestMethod]
	public void DurationCacheTracksSpeedFrameRateAndAnimationChanges()
	{
		var instance = new SpriteInstance( CreateSprite() );
		Assert.AreEqual( 1f, instance.PlaybackSpeed );
		instance.Update( 0.125f );
		instance.PlaybackSpeed = 2;
		instance.Update( 0 );
		Assert.AreEqual( 1, instance.CurrentFrameIndex );
		instance.Animation.FrameRate = 8;
		instance.Update( 0.0625f );
		Assert.AreEqual( 2, instance.CurrentFrameIndex );
		instance.Animation.FrameRate = 0;
		instance.Update( 10 );
		Assert.AreEqual( 2, instance.CurrentFrameIndex );
		instance.Animation.FrameRate = 4;
		instance.Update( 0.125f );
		Assert.AreEqual( 3, instance.CurrentFrameIndex );
		instance.Play( "Idle" );
		instance.Animation.LoopMode = Sprite.LoopMode.None;
		instance.Update( 0.0625f );
		Assert.IsFalse( instance.IsFinished );
		instance.Update( 0.0625f );
		Assert.IsTrue( instance.IsFinished );
	}

	[TestMethod]
	public void StaticFastPathHandlesResourceBecomingAnimated()
	{
		var instance = new SpriteInstance( CreateSprite() );
		instance.Play( "Idle" );
		instance.Update( 1000 );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
		Assert.IsFalse( instance.IsFinished );
		instance.Animation.Frames.Add( new Sprite.Frame() );
		instance.Update( 0.125f );
		Assert.AreEqual( 0, instance.CurrentFrameIndex );
		instance.Update( 0.125f );
		Assert.AreEqual( 1, instance.CurrentFrameIndex );
	}

	[TestMethod]
	[DataRow( Sprite.LoopMode.Loop, 1f )]
	[DataRow( Sprite.LoopMode.Loop, -1f )]
	[DataRow( Sprite.LoopMode.PingPong, 1f )]
	[DataRow( Sprite.LoopMode.PingPong, -1f )]
	public void SkippingCyclesMatchesIndividualUpdates( Sprite.LoopMode mode, float speed )
	{
		var resource = CreateSprite( mode );
		resource.Animations[0].LoopStart = 1;
		for ( int frame = 0; frame < 4; frame++ )
		{
			var fast = new SpriteInstance( resource ) { PlaybackSpeed = speed, CurrentFrameIndex = frame };
			var slow = new SpriteInstance( resource ) { PlaybackSpeed = speed, CurrentFrameIndex = frame };
			fast.Update( 250.125f );
			for ( int i = 0; i < 2001; i++ ) slow.Update( 0.125f );
			Assert.AreEqual( slow.CurrentFrameIndex, fast.CurrentFrameIndex );
			// Check direction and remaining elapsed time as well as the final frame.
			for ( int i = 0; i < 8; i++ )
			{
				fast.Update( 0.125f );
				slow.Update( 0.125f );
				Assert.AreEqual( slow.CurrentFrameIndex, fast.CurrentFrameIndex );
			}
		}
	}

	[TestMethod, Timeout( 5000 )]
	[DataRow( Sprite.LoopMode.Loop )]
	[DataRow( Sprite.LoopMode.PingPong )]
	[DataRow( Sprite.LoopMode.None )]
	public void ExtremeFiniteSpeedCompletesUpdate( Sprite.LoopMode mode )
	{
		var instance = new SpriteInstance( CreateSprite( mode ) ) { PlaybackSpeed = 1e20f };
		instance.Update( 1f / 60 );
		Assert.IsNotNull( instance.Frame );
		instance.Update( float.MaxValue );
		Assert.IsNotNull( instance.Frame );
		Assert.AreEqual( mode == Sprite.LoopMode.None, instance.IsFinished );
	}

	[TestMethod]
	public void InvalidTimeAndSpeedAreRejected()
	{
		var instance = new SpriteInstance( CreateSprite() );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => instance.Update( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => instance.Update( float.NaN ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => instance.PlaybackSpeed = float.PositiveInfinity );
	}
}
