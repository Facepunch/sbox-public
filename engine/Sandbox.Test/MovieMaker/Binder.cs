using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;

namespace TestMovieMaker;

#nullable enable

[TestClass]
public sealed class BinderTests : SceneTests
{
	/// <summary>
	/// Game object tracks without an explicit binding must auto-bind to root objects
	/// in the current scene with a matching name.
	/// </summary>
	[TestMethod]
	public void BindRootGameObjectMatchingName()
	{
		var exampleObject = new GameObject( true, "Example" );
		var exampleTrack = MovieClip.RootGameObject( exampleObject.Name );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsTrue( target.IsBound );
		Assert.AreEqual( exampleObject, target.Value );
	}

	/// <summary>
	/// Don't auto-bind to a root object with a different name.
	/// </summary>
	[TestMethod]
	public void BindRootGameObjectNoMatchingName()
	{
		var exampleObject = new GameObject( true, "Examble" );
		var exampleTrack = MovieClip.RootGameObject( "Example" );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsFalse( target.IsBound );
	}

	/// <summary>
	/// We can bind to a game object if it changes name to match the track.
	/// </summary>
	[TestMethod]
	public void LateBindRootGameObjectMatchingName()
	{
		var exampleObject = new GameObject( true, "Examble" );
		var exampleTrack = MovieClip.RootGameObject( "Example" );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsFalse( target.IsBound );

		exampleObject.Name = "Example";

		Assert.IsTrue( target.IsBound );
		Assert.AreEqual( exampleObject, target.Value );
	}

	/// <summary>
	/// Bindings will persist, even if the bound object changes name.
	/// </summary>
	[TestMethod]
	public void StickyBinding()
	{
		var exampleObject = new GameObject( true, "Example" );
		var exampleTrack = MovieClip.RootGameObject( exampleObject.Name );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsTrue( target.IsBound );
		Assert.AreEqual( exampleObject, target.Value );

		exampleObject.Name = "Examble";

		Assert.IsTrue( target.IsBound );
		Assert.AreEqual( exampleObject, target.Value );

		target.Reset();

		Assert.IsFalse( target.IsBound );
	}

	/// <summary>
	/// We can manually bind a track to a particular object.
	/// </summary>
	[TestMethod]
	public void ExplicitBinding()
	{
		var exampleObject = new GameObject( true, "Examble" );
		var exampleTrack = MovieClip.RootGameObject( "Example" );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsFalse( target.IsBound );

		target.Bind( exampleObject );

		Assert.IsTrue( target.IsBound );
		Assert.AreEqual( exampleObject, target.Value );
	}

	/// <summary>
	/// Properties are bound based on their parent track's binding.
	/// </summary>
	[TestMethod]
	public void PropertyBinding()
	{
		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property<Vector3>( nameof( GameObject.LocalPosition ) );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsFalse( target.IsBound );

		var exampleObject = new GameObject( true, "Example" );

		Assert.IsTrue( target.IsBound );

		target.Value = new Vector3( 100, 200, 300 );

		Assert.AreEqual( new Vector3( 100, 200, 300 ), exampleObject.LocalPosition );
	}

	/// <summary>
	/// Properties are bound based on their parent track's binding.
	/// </summary>
	[TestMethod]
	public void SubPropertyBinding()
	{
		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property<Vector3>( nameof( GameObject.LocalPosition ) )
			.Property<float>( nameof( Vector3.y ) );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsFalse( target.IsBound );

		var exampleObject = new GameObject( true, "Example" );

		Assert.IsTrue( target.IsBound );

		target.Value = 100f;

		Assert.AreEqual( new Vector3( 0, 100, 0 ), exampleObject.LocalPosition );
	}

	/// <summary>
	/// Support custom <see cref="ITrackPropertyFactory"/> implementations.
	/// </summary>
	[TestMethod]
	public void CustomPropertyBinding()
	{
		var exampleObject = new GameObject( true, "Example" );
		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property<Vector3>( "LookAt" );

		var target = TrackBinder.Default.Get( exampleTrack );

		Assert.IsTrue( target.IsBound );

		target.Value = new Vector3( 100f, 0f, 0f );

		Assert.IsTrue( new Vector3( 1f, 0f, 0f ).AlmostEqual( exampleObject.WorldRotation.Forward ) );

		target.Value = new Vector3( 0f, -100f, 0f );

		Assert.IsTrue( new Vector3( 0f, -1f, 0f ).AlmostEqual( exampleObject.WorldRotation.Forward ) );
	}

	/// <summary>
	/// Tests accessing <see cref="SkinnedModelRenderer.Parameters"/>.
	/// </summary>
	[TestMethod]
	public void AnimGraphParameters()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var cmpTrack = goTrack.Component<SkinnedModelRenderer>();
		var paramsTrack = cmpTrack.Property<SkinnedModelRenderer.ParameterAccessor>(
			nameof( SkinnedModelRenderer.Parameters ) );
		var paramTrack = paramsTrack.Property<float>( "example" );

		// If we can't access, will be an UnknownProperty with IsValid = false

		Assert.IsTrue( TrackBinder.Default.Get( paramTrack ).IsValid );
	}

	/// <summary>
	/// Tests accessing <see cref="SkinnedModelRenderer.Morphs"/>.
	/// </summary>
	[TestMethod]
	public void MorphParameters()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var cmpTrack = goTrack.Component<SkinnedModelRenderer>();
		var morphsTrack = cmpTrack.Property<SkinnedModelRenderer.MorphAccessor>(
			nameof( SkinnedModelRenderer.Morphs ) );
		var morphTrack = morphsTrack.Property<float>( "example" );

		// If we can't access, will be an UnknownProperty with IsValid = false

		Assert.IsTrue( TrackBinder.Default.Get( morphTrack ).IsValid );
	}

	/// <summary>
	/// Tests accessing <see cref="Transform"/> property.
	/// </summary>
	[TestMethod]
	public void TransformProperty()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var transformTrack = goTrack.Property<Transform>( nameof( GameObject.WorldTransform ) );

		// If we can't access, will be an UnknownProperty with IsValid = false

		Assert.IsTrue( TrackBinder.Default.Get( transformTrack ).IsValid );
	}

	/// <summary>
	/// Properties with <see cref="GameObject"/> / <see cref="Component"/> values are interpreted as
	/// <see cref="BindingReference{T}"/> properties.
	/// </summary>
	[TestMethod]
	public void CreateReferenceProperty()
	{
		var rendererTrack = MovieClip.RootGameObject( "Example" ).Component<SkinnedModelRenderer>();
		var rendererTarget = TrackBinder.Default.Get( rendererTrack );

		var boneMergeTargetProperty = TrackProperty.Create( rendererTarget, nameof( SkinnedModelRenderer.BoneMergeTarget ) );

		Assert.IsNotNull( boneMergeTargetProperty );
		Assert.AreEqual( typeof( BindingReference<SkinnedModelRenderer> ), boneMergeTargetProperty.TargetType );
	}

	[TestMethod]
	public void CreateListItemReferenceProperty()
	{
		var pointsTrack = MovieClip.RootGameObject( "Example" )
			.Component<LineRenderer>()
			.Property<List<GameObject>>( nameof( LineRenderer.Points ) );

		var pointsProperty = TrackBinder.Default.Get( pointsTrack );
		var pointProperty = TrackProperty.Create( pointsProperty, "0" );

		Assert.IsNotNull( pointProperty );
		Assert.AreEqual( typeof( BindingReference<GameObject> ), pointProperty.TargetType );
	}

	/// <summary>
	/// Tests accessing a GameObject property block that references another track.
	/// </summary>
	[TestMethod]
	public void ReferenceProperty()
	{
		var referencedTrack = MovieClip.RootGameObject( "Foo" );
		var referencingTrack = MovieClip.RootGameObject( "Bar" )
			.Component<VerletRope>()
			.ReferenceProperty<GameObject>( nameof( VerletRope.Attachment ) )
			.WithConstant( (0f, 1f), referencedTrack.Id );

		var exampleObject = new GameObject( true, "Example" );

		TrackBinder.Default.Get( referencedTrack ).Bind( exampleObject );

		// referencingTrack says it contains whatever referencedTrack is bound to

		Assert.IsTrue( referencingTrack.TryGetValue( 0.5f, out var value ) );

		Assert.AreSame( exampleObject, value.Get( TrackBinder.Default ) );
	}

	[TestMethod]
	public void ListCountProperty()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var cmpTrack = goTrack.Component<ExampleComponent>();
		var listTrack = cmpTrack.Property<List<Vector3>>( nameof( ExampleComponent.List ) );
		var countTrack = listTrack.Property<int>( nameof( IList.Count ) );

		var exampleObject = new GameObject( true, "Example" );
		var component = exampleObject.AddComponent<ExampleComponent>();
		var countProperty = TrackBinder.Default.Get( countTrack );

		Assert.IsTrue( countProperty.IsBound );

		component.List.Clear();

		Assert.AreEqual( 0, countProperty.Value );

		countProperty.Value = 4;

		Assert.AreEqual( 4, component.List.Count );
	}

	[TestMethod]
	public void ListItemProperty()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var cmpTrack = goTrack.Component<ExampleComponent>();
		var listTrack = cmpTrack.Property<List<Vector3>>( nameof( ExampleComponent.List ) );
		var item0Track = listTrack.Item( 0 );
		var item1Track = listTrack.Item( 1 );

		var exampleObject = new GameObject( true, "Example" );
		var component = exampleObject.AddComponent<ExampleComponent>();
		var item0Property = TrackBinder.Default.Get( item0Track );
		var item1Property = TrackBinder.Default.Get( item1Track );

		component.List.Clear();

		Assert.IsFalse( item0Property.IsBound );
		Assert.IsFalse( item1Property.IsBound );

		component.List.Add( new Vector3( 1f, 2f, 3f ) );

		Assert.IsTrue( item0Property.IsBound );
		Assert.IsFalse( item1Property.IsBound );

		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), item0Property.Value );

		item0Property.Value = new Vector3( 10f, 20f, 30f );

		Assert.AreEqual( new Vector3( 10f, 20f, 30f ), component.List[0] );
	}

	[TestMethod]
	public void LineRendererVectorPointsProperty()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var cmpTrack = goTrack.Component<LineRenderer>();

		var cmpRef = TrackBinder.Default.Get( cmpTrack );
		var property = TrackProperty.Create( cmpRef, nameof( LineRenderer.VectorPoints ) );

		Assert.IsNotNull( property );
		Assert.AreEqual( typeof( List<Vector3> ), property.TargetType );
	}

	[TestMethod]
	public void TemporaryEffectIsActive()
	{
		var goTrack = MovieClip.RootGameObject( "Example" );
		var cmpTrack = goTrack.Component<ExampleTemporaryEffect>();

		var cmpRef = TrackBinder.Default.Get( cmpTrack );
		var property = TrackProperty.Create( cmpRef, nameof( Component.ITemporaryEffect.IsActive ) );

		Assert.IsNotNull( property );
		Assert.AreEqual( typeof( bool ), property.TargetType );
		Assert.IsFalse( property.IsBound );

		var exampleObject = new GameObject( true, "Example" );
		var cmp = exampleObject.AddComponent<ExampleTemporaryEffect>() as Component.ITemporaryEffect;

		Assert.IsTrue( property.IsBound );
		Assert.IsTrue( cmp.IsActive );

		property.Value = false;

		Assert.IsFalse( cmp.IsActive );
	}

	/// <summary>
	/// Given a clip, create any missing <see cref="GameObject"/>s and <see cref="Component"/>s
	/// for each track.
	/// </summary>
	[TestMethod]
	public void CreateTargets()
	{
		var clip = MovieClip.FromTracks(
			MovieClip.RootGameObject( "Example" )
				.Component<ModelRenderer>()
				.Property<Color>( nameof( ModelRenderer.Tint ) )
				.WithConstant( (0d, 10d), Color.Red ) );

		var exampleTrack = clip.GetTrack( "Example" );
		var rendererTrack = clip.GetTrack( "Example", nameof( ModelRenderer ) )!;

		Assert.IsNotNull( exampleTrack );
		Assert.IsNotNull( rendererTrack );

		var binder = new TrackBinder();

		var exampleRef = binder.Get( exampleTrack );
		var rendererRef = binder.Get( rendererTrack );

		// There are no game objects in the scene, these tracks shouldn't be bound to anything

		Assert.IsFalse( exampleRef.IsBound );
		Assert.IsFalse( rendererRef.IsBound );

		// Create any objects needed to play the clip

		binder.CreateTargets( clip );

		// Now they should be bound

		Assert.IsTrue( exampleRef.IsBound );
		Assert.IsTrue( rendererRef.IsBound );
	}

	/// <summary>
	/// When creating targets, each track should get a unique target even if they have the same name.
	/// </summary>
	[TestMethod]
	public void CreateTargetsUnique()
	{
		var clip = MovieClip.FromTracks(
			MovieClip.RootGameObject( "Example" ),
			MovieClip.RootGameObject( "Example" ) );

		var track1 = clip.Tracks[0];
		var track2 = clip.Tracks[1];

		Assert.AreEqual( "Example", track1.Name );
		Assert.AreEqual( "Example", track2.Name );
		Assert.AreNotSame( track1, track2 );

		var binder = new TrackBinder();

		var ref1 = binder.Get( track1 );
		var ref2 = binder.Get( track2 );

		Assert.IsFalse( ref1.IsBound );
		Assert.IsFalse( ref2.IsBound );
		Assert.AreNotSame( ref1, ref2 );

		binder.CreateTargets( clip );

		Assert.IsTrue( ref1.IsBound );
		Assert.IsTrue( ref2.IsBound );
		Assert.AreNotSame( ref1.Value, ref2.Value );
	}

	/// <summary>
	/// Calling <see cref="TrackBinder.CreateTargets(IMovieClip, bool)"/> should instantiate any relevant prefabs,
	/// as specified by <see cref="TrackMetadata"/>, to set default property values.
	/// </summary>
	[TestMethod]
	public void CreatePrefabTarget()
	{
		const string prefabPath = "prefabs/example.prefab";

		RegisterSimplePrefab( prefabPath, new JsonObject
		{
			{ "__type", "ModelRenderer" },
			{ "Tint", "1,0,0,1" }
		} );

		// Example track says it's using the above prefab in TrackMetadata

		var clip = MovieClip.FromTracks(
			MovieClip.RootGameObject( "Example", metadata: new TrackMetadata( PrefabSource: prefabPath ) )
				.Component<ModelRenderer>()
				.Property<bool>( nameof( ModelRenderer.Enabled ) )
				.WithConstant( (0d, 10d), true ) );

		var rendererTrack = clip.GetTrack( "Example", nameof( ModelRenderer ) ) as CompiledReferenceTrack<ModelRenderer>;

		Assert.IsNotNull( rendererTrack );

		var binder = new TrackBinder();
		var rendererRef = binder.Get( rendererTrack );

		binder.CreateTargets( clip );

		var renderer = rendererRef.Value;

		Assert.IsNotNull( renderer );

		// Tint should be loaded from the prefab

		Assert.AreEqual( Color.Red, renderer.Tint );
	}

	/// <summary>
	/// When <see cref="TrackBinder.CreateTargets(IMovieClip)"/> instantiates a prefab, it should only
	/// create components that have tracks in the given clip. This is so we include all display-related components,
	/// but exclude any that would try to do game logic like <see cref="PlayerController"/>, unless opted-in
	/// by having a track.
	/// </summary>
	[TestMethod]
	public void CreatePrefabTargetOnlyTrackComponents()
	{
		const string prefabPath = "prefabs/example.prefab";

		RegisterSimplePrefab( prefabPath,
			new JsonObject { { "__type", "ModelRenderer" } },
			new JsonObject { { "__type", "PlayerController" } } );

		// Example track says it's using the above prefab in TrackMetadata

		var clip = MovieClip.FromTracks(
			MovieClip.RootGameObject( "Example", metadata: new TrackMetadata( PrefabSource: prefabPath ) )
				.Component<ModelRenderer>()
				.Property<bool>( nameof( ModelRenderer.Enabled ) )
				.WithConstant( (0d, 10d), true ) );

		var exampleTrack = clip.GetTrack( "Example" ) as CompiledReferenceTrack<GameObject>;

		Assert.IsNotNull( exampleTrack );

		var binder = new TrackBinder();
		var exampleRef = binder.Get( exampleTrack );

		binder.CreateTargets( clip );

		var go = exampleRef.Value;

		Assert.IsNotNull( go );

		// Only ModelRenderer should exist, not PlayerController

		Assert.IsNotNull( go.GetComponent<ModelRenderer>() );
		Assert.IsNull( go.GetComponent<PlayerController>() );
	}

	/// <summary>
	/// Test (de)serializing a <see cref="TrackBinder"/>.
	/// </summary>
	[TestMethod]
	public void SerializeBindings()
	{
		var binder = new TrackBinder();

		var exampleTrack = MovieClip.RootGameObject( "Example" );
		var target = binder.Get( exampleTrack );

		// Make sure the object isn't called Example, or it will get auto-bound
		// to the Example track.

		var fooObject = new GameObject( "Foo" );

		Assert.IsFalse( target.IsBound );

		target.Bind( fooObject );

		Assert.IsTrue( target.IsBound );

		var jsonNode = binder.Serialize();

		Console.WriteLine( jsonNode.ToJsonString( Json.options ) );

		// Create a new empty binder

		binder = new TrackBinder();
		target = binder.Get( exampleTrack );

		Assert.IsFalse( target.IsBound );

		binder.Deserialize( jsonNode );

		Assert.IsTrue( target.IsBound );
	}
}

public class ExampleComponent : Component
{
	[Property] public List<Vector3> List { get; } = new();
}

public class ExampleTemporaryEffect : Component, Component.ITemporaryEffect
{
	private bool _isActive;

	bool ITemporaryEffect.IsActive => _isActive;

	protected override void OnEnabled()
	{
		_isActive = true;
	}

	void ITemporaryEffect.DisableLooping()
	{
		_isActive = false;
	}
}
