using System;
using System.Collections.Generic;

namespace SceneTests.Components;

[TestClass]
public class SpriteRendererPlaybackTest
{
	Scene scene;

	[TestCleanup]
	public void Cleanup() => scene?.Destroy();

	SpriteRenderer CreateRenderer()
	{
		var go = scene.CreateObject();
		go.Flags |= GameObjectFlags.EditorOnly;
		var renderer = go.Components.Create<SpriteRenderer>();
		renderer.Sprite = new Sprite
		{
			Animations =
			[
				new() { Name = "Run", FrameRate = 4, Frames = [new(), new(), new()] },
				new() { Name = "Idle", Frames = [new()] }
			]
		};
		return renderer;
	}

	[TestMethod]
	public void LongUpdatesDispatchOnlyOneReachedFrame()
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		var events = new List<string>();
		renderer.CurrentAnimation.Frames[1].BroadcastMessages.Add( new() { Message = "one" } );
		renderer.CurrentAnimation.Frames[2].BroadcastMessages.Add( new() { Message = "two" } );
		renderer.OnBroadcastMessage = events.Add;
		scene.EditorTick( 10, 10 );
		Assert.AreEqual( 1, renderer.CurrentFrameIndex );
		CollectionAssert.AreEqual( new[] { "one" }, events );
	}

	[TestMethod]
	public void NonLoopingAnimationCompletesOnceAndCanBeSought()
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		renderer.CurrentAnimation.LoopMode = Sprite.LoopMode.None;
		int completed = 0;
		renderer.OnAnimationEnd = _ => completed++;
		for ( int i = 1; i <= 6; i++ ) scene.EditorTick( i * 0.25f, 0.25f );
		Assert.AreEqual( 2, renderer.CurrentFrameIndex );
		Assert.AreEqual( 1, completed );
		renderer.CurrentFrameIndex = 2;
		renderer.PlaybackSpeed = -1;
		for ( int i = 7; i <= 10; i++ ) scene.EditorTick( i * 0.25f, 0.25f );
		Assert.AreEqual( 0, renderer.CurrentFrameIndex );
		Assert.AreEqual( 2, completed );
	}

	[TestMethod]
	public void AnimationSelectionUsesIndexAndTracksResourceReplacement()
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		// Names need not be unique when selecting by index.
		renderer.Sprite.Animations[1].Name = "Run";
		var selected = renderer.Sprite.Animations[1];
		renderer.PlayAnimation( 1 );
		scene.EditorTick( 0.25f, 0.25f );
		Assert.AreSame( selected, renderer.CurrentAnimation );
		Assert.AreEqual( 0, renderer.CurrentFrameIndex );
		renderer.Sprite.Animations[1] = new() { Name = "Replacement", FrameRate = 4, Frames = [new(), new()] };
		scene.EditorTick( 0.5f, 0.25f );
		Assert.AreEqual( 1, renderer.CurrentFrameIndex );
		renderer.Sprite = null;
		scene.EditorTick( 0.75f, 0.25f );
		Assert.IsNull( renderer.CurrentAnimation );
	}

	[TestMethod]
	public void SeekingBeforeFirstUpdatePreservesOffsetAndCanRestart()
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		renderer.CurrentFrameIndex = 1;
		scene.EditorTick( 0.125f, 0.125f );
		Assert.AreEqual( 1, renderer.CurrentFrameIndex );
		scene.EditorTick( 0.25f, 0.125f );
		Assert.AreEqual( 2, renderer.CurrentFrameIndex );
		renderer.CurrentFrameIndex = 0;
		scene.EditorTick( 0.375f, 0.125f );
		Assert.AreEqual( 0, renderer.CurrentFrameIndex );
		scene.EditorTick( 0.5f, 0.125f );
		Assert.AreEqual( 1, renderer.CurrentFrameIndex );
	}

	[TestMethod]
	public void EditorUpdateAdvancesOnceAndDispatchesImmediately()
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		var events = new List<string>();
		renderer.CurrentAnimation.Frames[1].BroadcastMessages.Add( new() { Message = "frame 1" } );
		renderer.OnBroadcastMessage = events.Add;
		scene.EditorTick( 0.25f, 0.25f );
		Assert.AreEqual( 1, renderer.CurrentFrameIndex );
		CollectionAssert.AreEqual( new[] { "frame 1" }, events );
		renderer.Enabled = false;
		scene.EditorTick( 0.5f, 0.25f );
		Assert.AreEqual( 1, renderer.CurrentFrameIndex );
		renderer.Enabled = true;
		scene.EditorTick( 0.75f, 0.25f );
		Assert.AreEqual( 2, renderer.CurrentFrameIndex );
		var loopMessages = renderer.CurrentAnimation.Frames[0].BroadcastMessages;
		renderer.OnAnimationEnd = name => { events.Add( name ); renderer.PlayAnimation( "Idle" ); loopMessages.Clear(); };
		loopMessages.Add( new() { Message = "loop frame" } );
		scene.EditorTick( 1, 0.25f );
		CollectionAssert.AreEqual( new[] { "frame 1", "Run", "loop frame" }, events );
		Assert.AreEqual( "Idle", renderer.CurrentAnimation.Name );
	}

	[TestMethod]
	[DataRow( "destroy" )]
	[DataRow( "switch" )]
	[DataRow( "seek" )]
	[DataRow( "disable" )]
	[DataRow( "restart" )]
	[DataRow( "deactivate" )]
	public void OnlyDestructionStopsRemainingFrameEvents( string action )
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		var survivor = CreateRenderer();
		renderer.CurrentAnimation.Frames[1].BroadcastMessages = [new() { Message = "first" }, new() { Message = "second" }];
		var events = new List<string>();
		renderer.OnBroadcastMessage = message =>
		{
			events.Add( message );
			if ( message != "first" ) return;
			switch ( action )
			{
				case "destroy": renderer.GameObject.Destroy(); break;
				case "switch": renderer.PlayAnimation( "Idle" ); break;
				case "seek": renderer.CurrentFrameIndex = 0; break;
				case "disable": renderer.Enabled = false; break;
				case "restart": renderer.PlayAnimation( "Run" ); break;
				case "deactivate": renderer.GameObject.Enabled = false; break;
			}
		};
		scene.EditorTick( 0.25f, 0.25f );
		CollectionAssert.AreEqual( action == "destroy" ? new[] { "first" } : new[] { "first", "second" }, events );
		Assert.AreEqual( 1, survivor.CurrentFrameIndex );
	}

	[TestMethod]
	public void MessageListCanChangeDuringDispatch()
	{
		scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var renderer = CreateRenderer();
		var messages = renderer.CurrentAnimation.Frames[1].BroadcastMessages;
		messages.Add( new() { Message = "first" } );
		messages.Add( new() { Message = "second" } );
		var events = new List<string>();
		renderer.OnBroadcastMessage = message => { events.Add( message ); messages.Clear(); };
		scene.EditorTick( 0.25f, 0.25f );
		CollectionAssert.AreEqual( new[] { "first", "second" }, events );
	}
}
