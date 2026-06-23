using System;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;

namespace MovieMakerTests;

#nullable enable

[TestClass]
public sealed class PlayerTest : SceneTestBase
{
	private static MoviePlayer CreateMoviePlayer()
	{
		var obj = new GameObject();
		return obj.AddComponent<MoviePlayer>();
	}

	private static MovieClip CreateMovieClip( bool deterministicIds = false )
	{
		return MovieClip.FromTracks(
			MovieClip.RootGameObject( "Example", id: deterministicIds ? Guid.Parse( "6e032a93-7cc5-42c2-9b4c-9ca17eab22c0" ) : Guid.NewGuid() )
				.Component<ModelRenderer>( id: deterministicIds ? Guid.Parse( "88632560-9d57-47b8-a57e-47c09d0b21db" ) : Guid.NewGuid() )
				.Property<Color>( nameof( ModelRenderer.Tint ) )
				.WithConstant( (0, 10), Color.Red ) );
	}

	private static MovieClip CreateParentChangeMovieClip()
	{
		var parentTrack = MovieClip.RootGameObject( "Parent" );
		var childTrack = MovieClip.RootGameObject( "Child" );

		var parentPropertyTrack = childTrack.ReferenceProperty<GameObject>( nameof( GameObject.Parent ) )
			.WithConstant( (0d, 1d), default )
			.WithConstant( (1d, 2d), parentTrack )
			.WithConstant( (2d, 3d), default );

		return MovieClip.FromTracks( parentTrack, childTrack, parentPropertyTrack );
	}

	/// <summary>
	/// Test <see cref="MoviePlayer.IsCreatedTarget"/>
	/// </summary>
	[TestMethod]
	public void IsCreatedTargetTrue()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.CreateTargets = true;
		player.Clip = clip;

		var renderer = player.GetComponentInChildren<ModelRenderer>( true );

		Assert.IsTrue( player.IsCreatedTarget( renderer ) );
	}

	/// <summary>
	/// Test <see cref="MoviePlayer.IsCreatedTarget"/>
	/// </summary>
	[TestMethod]
	public void IsCreatedTargetFalse()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		var renderer = new GameObject( "Example" ).AddComponent<ModelRenderer>();

		player.CreateTargets = true;
		player.Clip = clip;

		Assert.IsFalse( player.IsCreatedTarget( renderer ) );
	}

	/// <summary>
	/// When setting <see cref="MoviePlayer.CreateTargets"/> to true,
	/// missing targets must be immediately created.
	/// </summary>
	[TestMethod]
	public void CreateTargetsOnToggle()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.CreateTargets = false;
		player.Clip = clip;

		Assert.IsNull( player.GetComponentInChildren<ModelRenderer>( true ) );

		player.CreateTargets = true;

		Assert.IsNotNull( player.GetComponentInChildren<ModelRenderer>( true ) );
	}

	/// <summary>
	/// When setting <see cref="MoviePlayer.CreateTargets"/> to false,
	/// created targets must be immediately destroyed.
	/// </summary>
	[TestMethod]
	public void DestroyTargetsOnToggle()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.CreateTargets = true;
		player.Clip = clip;

		Assert.IsNotNull( player.GetComponentInChildren<ModelRenderer>( true ) );

		player.CreateTargets = false;
		player.Scene.ProcessDeletes();

		Assert.IsNull( player.GetComponentInChildren<ModelRenderer>( true ) );
	}

	/// <summary>
	/// When setting <see cref="MoviePlayer.Clip"/> given <see cref="MoviePlayer.CreateTargets"/> is true,
	/// missing targets must be immediately created.
	/// </summary>
	[TestMethod]
	public void CreateTargetsOnSetClip()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.CreateTargets = true;

		Assert.IsNull( player.GetComponentInChildren<ModelRenderer>( true ) );

		player.Clip = clip;

		Assert.IsNotNull( player.GetComponentInChildren<ModelRenderer>( true ) );
	}

	/// <summary>
	/// When clearing <see cref="MoviePlayer.Clip"/> given <see cref="MoviePlayer.CreateTargets"/> is true,
	/// created targets must be immediately destroyed.
	/// </summary>
	[TestMethod]
	public void DestroyTargetsOnClearClip()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.CreateTargets = true;
		player.Clip = clip;

		Assert.IsNotNull( player.GetComponentInChildren<ModelRenderer>( true ) );

		player.Clip = null;
		player.Scene.ProcessDeletes();

		Assert.IsNull( player.GetComponentInChildren<ModelRenderer>( true ) );
	}

	/// <summary>
	/// Create missing targets on enable if <see cref="MoviePlayer.CreateTargets"/> is true.
	/// </summary>
	[TestMethod]
	public void CreateTargetsOnEnabled()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.Enabled = false;
		player.CreateTargets = true;
		player.Clip = clip;

		Assert.IsNull( player.GetComponentInChildren<ModelRenderer>( true ) );

		player.Enabled = true;

		Assert.IsNotNull( player.GetComponentInChildren<ModelRenderer>( true ) );
	}

	/// <summary>
	/// Keep targets around when changing between clips that have overlapping tracks.
	/// </summary>
	[TestMethod]
	public void KeepCreatedTargetsOnChangeClip()
	{
		var player = CreateMoviePlayer();
		var clip1 = CreateMovieClip( deterministicIds: true );
		var clip2 = CreateMovieClip( deterministicIds: true );

		Assert.AreNotSame( clip1, clip2 );

		player.CreateTargets = true;
		player.Clip = clip1;

		var renderer = player.GetComponentInChildren<ModelRenderer>( true );

		Assert.IsNotNull( renderer );

		player.Clip = clip2;

		Assert.AreEqual( renderer, player.GetComponentInChildren<ModelRenderer>( true ) );
	}

	/// <summary>
	/// Auto-bind to the same target when changing clip.
	/// </summary>
	[TestMethod]
	[Ignore( "Auto-bound targets are not re-bound after changing clips - the binder resolves null where the original ModelRenderer is expected. Re-enable when auto-bind survives clip changes." )]
	public void KeepAutoBoundTargetsOnChangeClip()
	{
		var player = CreateMoviePlayer();

		player.CreateTargets = false;

		var clip1 = CreateMovieClip( deterministicIds: false );
		var clip2 = CreateMovieClip( deterministicIds: false );

		var rendererTrack1 = clip1.GetReference<ModelRenderer>( "Example", nameof( ModelRenderer ) )!;
		var rendererTrack2 = clip2.GetReference<ModelRenderer>( "Example", nameof( ModelRenderer ) )!;

		var renderer = new GameObject( "Example" ).AddComponent<ModelRenderer>();

		player.Clip = clip1;

		Assert.AreEqual( renderer, player.Binder.Get( rendererTrack1 ).Value );

		player.Clip = clip2;

		Assert.AreEqual( renderer, player.Binder.Get( rendererTrack2 ).Value );
	}

	/// <summary>
	/// Promoted created targets shouldn't get removed.
	/// </summary>
	[TestMethod]
	public void KeepPromotedTarget()
	{
		var player = CreateMoviePlayer();
		var clip = CreateMovieClip();

		player.CreateTargets = true;
		player.Clip = clip;

		var renderer = player.GetComponentInChildren<ModelRenderer>( true );

		// Move created target to scene root, to promote it to a saved object

		renderer.GameObject.Parent = null;

		player.Clip = null;

		player.Scene.ProcessDeletes();

		Assert.IsTrue( renderer.IsValid );
	}

	/// <summary>
	/// If a target gets promoted, a new clip with a matching track can auto-bind to it.
	/// </summary>
	[TestMethod]
	[Ignore( "Promoted targets resolve to a DIFFERENT ModelRenderer instance after changing clips instead of re-binding the promoted one. Re-enable when auto-bind survives clip changes." )]
	public void AutoBindPromotedTargetOnChangeClip()
	{
		var player = CreateMoviePlayer();
		var clip1 = CreateMovieClip( deterministicIds: false );
		var clip2 = CreateMovieClip( deterministicIds: false );

		player.CreateTargets = true;
		player.Clip = clip1;

		var renderer = player.GetComponentInChildren<ModelRenderer>( true );

		// Move created target to scene root, to promote it to a saved object

		renderer.GameObject.Parent = null;

		// Clip 2 should still bind to the original created ModelRenderer

		player.Clip = clip2;

		var rendererTrack = clip2.GetReference<ModelRenderer>( "Example", nameof( ModelRenderer ) )!;

		Assert.AreEqual( renderer, player.Binder.Get( rendererTrack ).Value );
	}

	/// <summary>
	/// A created target shouldn't escape to the scene root if a movie tries to set its parent to null.
	/// It should be parented under <see cref="MoviePlayer.CreatedTargetsRoot"/> instead.
	/// </summary>
	[TestMethod]
	[DataRow( false, DisplayName = "Parent Enabled" )]
	[DataRow( true, DisplayName = "Parent Disabled" )]
	public void CreatedTargetsAreNeverMovedToSceneRoot( bool parentDisabled )
	{
		var player = CreateMoviePlayer();
		var clip = CreateParentChangeMovieClip();

		player.CreateTargets = true;
		player.Clip = clip;

		var parent = player.CreatedTargetsRoot!.Children.First( x => x.Name == "Parent" );
		var child = player.CreatedTargetsRoot!.Children.First( x => x.Name == "Child" );

		parent.Enabled = !parentDisabled;

		player.Position = 0.5;

		Assert.AreEqual( player.CreatedTargetsRoot, child.Parent );

		player.Position = 1.5;

		Assert.AreEqual( parent, child.Parent );

		player.Position = 2.5;

		Assert.AreEqual( player.CreatedTargetsRoot, child.Parent );
	}
}
