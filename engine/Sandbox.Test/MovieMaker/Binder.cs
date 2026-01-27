using System;
using System.Collections;
using System.Collections.Generic;
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
}

public class ExampleComponent : Component
{
	[Property] public List<Vector3> List { get; } = new();
}
