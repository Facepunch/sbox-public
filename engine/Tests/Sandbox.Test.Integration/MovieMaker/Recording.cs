using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MovieMakerTests;

#nullable enable

[TestClass]
public sealed class RecorderTest : SceneTestBase
{
	private static MovieClip Record( MovieTime duration, Action<MovieTime>? simulate = null ) =>
		Record( null, duration, simulate );

	private static MovieClip Record( MovieRecorderOptions? options, MovieTime duration, Action<MovieTime>? simulate = null )
	{
		options ??= MovieRecorderOptions.Default;

		var recorder = new MovieRecorder( Game.ActiveScene, options );
		var dt = MovieTime.FromFrames( 1, options.SampleRate * 4 );

		while ( true )
		{
			simulate?.Invoke( recorder.Time );

			Game.ActiveScene.ProcessDeletes();

			recorder.Capture();

			if ( recorder.Time >= duration ) break;

			recorder.Advance( dt );
		}

		return recorder.ToClip();
	}

	public static MovieClip Record( MovieRecorderOptions? options, MovieTime duration,
		params IEnumerable<(MovieTime Time, Action Action)> events )
	{
		using var eventEnumerator = events.GetEnumerator();

		var hasEvent = eventEnumerator.MoveNext();

		// ReSharper disable AccessToDisposedClosure
		return Record( options, duration, t =>
		{
			if ( !hasEvent || eventEnumerator.Current.Time > t ) return;

			eventEnumerator.Current.Action();
			hasEvent = eventEnumerator.MoveNext();
		} );
		// ReSharper restore AccessToDisposedClosure
	}

	/// <summary>
	/// Record a <see cref="GameObject"/> moving between two points.
	/// </summary>
	[TestMethod]
	public void RecordPosition()
	{
		var startPos = new Vector3( 0f, 0f, 0f );
		var endPos = new Vector3( 500f, 100f, -200f );
		var duration = MovieTime.FromSeconds( 10d );

		var go = new GameObject( "Example" ) { WorldPosition = startPos };

		var options = new MovieRecorderOptions()
			.WithCaptureAction( x => x.GetTrackRecorder( go )!.Capture() );

		var clip = Record( options, duration, t =>
		{
			go.WorldPosition = Vector3.Lerp( startPos, endPos, duration.GetFraction( t ) );
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		var posTrack = clip.GetTrack( go.Name, nameof( GameObject.LocalPosition ) ) as CompiledPropertyTrack<Vector3>;

		Assert.IsNotNull( posTrack );

		// Position starts at startPos

		Assert.IsTrue( posTrack.TryGetValue( 0d, out var posA ) );
		Assert.AreEqual( startPos, posA );

		// Position ends at endPos

		Assert.IsTrue( posTrack.TryGetValue( duration, out var posB ) );
		Assert.AreEqual( endPos, posB );

		// It's half-way after half the time

		Assert.IsTrue( posTrack.TryGetValue( MovieTime.Lerp( 0d, duration, 0.5 ), out var posC ) );
		Assert.AreEqual( (startPos + endPos) * 0.5f, posC );
	}

	/// <summary>
	/// Make sure two objects moving along the exact same path are still
	/// at the same positions, even if one starts recording later.
	/// </summary>
	[TestMethod]
	public void RecordPositionAligned()
	{
		var foo = new GameObject( "Foo" );
		var bar = new GameObject( "Bar" ) { Enabled = false };

		var startPos = new Vector3( 100f, 0f, 0f );
		var endPos = new Vector3( 200f, 0f, 0f );

		foo.LocalPosition = bar.LocalPosition = startPos;

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( foo )
			.WithCaptureGameObject( bar );

		var clip = Record( options, 1d, t =>
		{
			// Enable bar half a sample period after the start of the movie

			bar.Enabled = t >= 0.5 / options.SampleRate;

			// Both objects are always at the same position as each other during recording

			foo.LocalPosition = bar.LocalPosition = Vector3.Lerp( startPos, endPos, (float)t.TotalSeconds );
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		var fooPosTrack = clip.GetProperty<Vector3>( foo.Name, nameof( GameObject.LocalPosition ) );
		var barPosTrack = clip.GetProperty<Vector3>( bar.Name, nameof( GameObject.LocalPosition ) );

		Assert.IsNotNull( fooPosTrack );
		Assert.IsNotNull( barPosTrack );

		Assert.IsTrue( fooPosTrack.TryGetValue( 0.0, out var fooStartPos ) );
		Assert.AreEqual( startPos, fooStartPos );

		// Bar track only starts recording a short time after 0.0

		Assert.IsFalse( barPosTrack.TryGetValue( 0.0, out _ ) );

		// Nevertheless, both tracks should be approximately the same at 0.5 seconds

		Assert.IsTrue( fooPosTrack.TryGetValue( 0.5, out var fooMidPos ) );
		Assert.IsTrue( barPosTrack.TryGetValue( 0.5, out var barMidPos ) );

		Assert.IsTrue( fooMidPos.AlmostEqual( (startPos + endPos) * 0.5f, 0.01f ) );
		Assert.IsTrue( barMidPos.AlmostEqual( (startPos + endPos) * 0.5f, 0.01f ) );
	}

	/// <summary>
	/// Capture a game object, but choose a custom name for the track.
	/// </summary>
	[TestMethod]
	public void RecordGameObjectCustomName()
	{
		var go = new GameObject( "Foo" );

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( go, trackName: "Bar" );

		var clip = Record( options, 10d );

		Console.WriteLine( Json.Serialize( clip ) );

		// We called the track "Bar" instead of "Foo"

		Assert.IsNull( clip.GetReference<GameObject>( "Foo" ) );
		Assert.IsNotNull( clip.GetReference<GameObject>( "Bar" ) );
	}

	/// <summary>
	/// Capture a game object's tags.
	/// </summary>
	[TestMethod]
	public void RecordTags()
	{
		var go = new GameObject( "Example" );

		go.Tags.Add( "player" );
		go.Tags.Add( "test" );

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( go );

		var clip = Record( options, 4.0,
			(1.0, () => go.Tags.Remove( "test" )),
			(2.0, () => go.Tags.Add( "test" )),
			(3.0, () => go.Tags.RemoveAll()) );

		Console.WriteLine( Json.Serialize( clip ) );

		var playerTagTrack = clip.GetProperty<bool>( go.Name, nameof( GameObject.Tags ), "player" );
		var testTagTrack = clip.GetProperty<bool>( go.Name, nameof( GameObject.Tags ), "test" );
		var unknownTagTrack = clip.GetProperty<bool>( go.Name, nameof( GameObject.Tags ), "unknown" );

		Assert.IsNotNull( playerTagTrack );
		Assert.IsNotNull( testTagTrack );
		Assert.IsNull( unknownTagTrack );

		bool tag;

		Assert.IsTrue( playerTagTrack.TryGetValue( 0.5, out tag ) && tag );
		Assert.IsTrue( testTagTrack.TryGetValue( 0.5, out tag ) && tag );

		Assert.IsTrue( playerTagTrack.TryGetValue( 1.5, out tag ) && tag );
		Assert.IsTrue( testTagTrack.TryGetValue( 1.5, out tag ) && !tag );

		Assert.IsTrue( playerTagTrack.TryGetValue( 2.5, out tag ) && tag );
		Assert.IsTrue( testTagTrack.TryGetValue( 2.5, out tag ) && tag );

		Assert.IsTrue( playerTagTrack.TryGetValue( 3.5, out tag ) && !tag );
		Assert.IsTrue( testTagTrack.TryGetValue( 3.5, out tag ) && !tag );
	}

	/// <summary>
	/// Don't capture tags inherited from a parent object.
	/// </summary>
	[TestMethod]
	public void DontRecordInheritedTags()
	{
		var parent = new GameObject( "Parent" );

		parent.Tags.Add( "foo" );

		var child = new GameObject( parent, name: "Child" );

		child.Tags.Add( "bar" );

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( child );

		var clip = Record( options, 1.0 );

		Console.WriteLine( Json.Serialize( clip ) );

		Assert.IsNotNull( clip.GetProperty<bool>( parent.Name, nameof( GameObject.Tags ), "foo" ) );
		Assert.IsNotNull( clip.GetProperty<bool>( parent.Name, child.Name, nameof( GameObject.Tags ), "bar" ) );
		Assert.IsNull( clip.GetProperty<bool>( parent.Name, child.Name, nameof( GameObject.Tags ), "foo" ) );
	}

	[TestMethod]
	public void EnableTrackRecording_Control()
	{
		var obj = new GameObject( "Control" );

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( obj );

		var clip = Record( options, 1.0 );

		Assert.IsNotNull( clip.GetProperty<bool>( obj.Name, nameof( GameObject.Enabled ) ) );
	}

	[TestMethod]
	[DataRow( GameObjectFlags.Static )]
	[DataRow( GameObjectFlags.DontDestroyOnLoad )]
	public void EnableTrackRecording_Flags( GameObjectFlags flag )
	{
		var obj = new GameObject( "Flagged" ) { Flags = flag };

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( obj );

		var clip = Record( options, 1.0 );

		Assert.IsNotNull( clip.GetReference<GameObject>( obj.Name ) );
		Assert.IsNull( clip.GetProperty<bool>( obj.Name, nameof( GameObject.Enabled ) ) );
	}

	[TestMethod]
	public void EnableTrackRecording_MapInstance()
	{
		var mapInst = new GameObject( "Map" )
			.AddComponent<MapInstance>();

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureComponent( mapInst );

		var clip = Record( options, 1.0 );

		Assert.IsNotNull( clip.GetReference<MapInstance>( mapInst.GameObject.Name, nameof( MapInstance ) ) );
		Assert.IsNull( clip.GetProperty<bool>( mapInst.GameObject.Name, nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( mapInst.GameObject.Name, nameof( MapInstance ), nameof( MapInstance.Enabled ) ) );
	}

	[TestMethod]
	public void EnableTrackRecording_StaticDisabled()
	{
		var obj = new GameObject( "Static" ) { Flags = GameObjectFlags.Static };

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( obj );

		var clip = Record( options, 1.0, time =>
		{
			obj.Enabled = time < 0.25 || time > 0.75;
		} );

		Assert.IsNotNull( clip.GetReference<GameObject>( obj.Name ) );
		Assert.IsNotNull( clip.GetProperty<bool>( obj.Name, nameof( GameObject.Enabled ) ) );
	}

	private static ModelRenderer CreateMapProp()
	{
		var map = new GameObject( "Map" );
		map.AddComponent<MapInstance>();

		var prop = new GameObject( map, name: "Prop" );

		return prop.AddComponent<ModelRenderer>();
	}

	private static MovieRecorderOptions CaptureAllRenderers => new MovieRecorderOptions()
		.WithDefaultComponentCapturers()
		.WithCaptureAll<Renderer>();

	/// <summary>
	/// Map objects that are never disabled don't need <see cref="GameObject.Enabled"/> tracks.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObject()
	{
		CreateMapProp();

		var clip = Record( CaptureAllRenderers, 1.0 );

		Assert.IsNotNull( clip.GetReference<ModelRenderer>( "Map", "Prop", nameof( ModelRenderer ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Objects loaded by a <see cref="MapInstance"/> have <see cref="TrackFlags.MapObject"/>, so playback
	/// doesn't create them again. Objects created at runtime under the map don't.
	/// </summary>
	[TestMethod]
	public void RecordMapObjectFlag()
	{
		var renderer = CreateMapProp();
		renderer.GameObject.SetMapSource( "maps/example.vmap" );

		var mapInstance = renderer.GameObject.Parent.GetComponent<MapInstance>();

		mapInstance.MapName = "maps/example.vmap";
		mapInstance.IsLoaded = true;

		var effect = new GameObject( renderer.GameObject.Parent, name: "Effect" );
		effect.AddComponent<ModelRenderer>();

		var clip = Record( CaptureAllRenderers.WithCaptureAll<MapInstance>(), 1.0 );

		// The map instance's track says its objects were recorded with the flag

		Assert.AreEqual( TrackFlags.MapObject, clip.GetReference<MapInstance>( "Map", nameof( MapInstance ) )?.Metadata?.Flags );
		Assert.AreEqual( TrackFlags.MapObject, clip.GetReference<GameObject>( "Map", "Prop" )?.Metadata?.Flags );
		Assert.AreEqual( TrackFlags.None, clip.GetReference<GameObject>( "Map", "Effect" )?.Metadata?.Flags ?? TrackFlags.None );
		Assert.IsNotNull( clip.GetReference<GameObject>( "Map", "Effect" ) );
	}

	/// <summary>
	/// Hidden objects aren't recorded, but objects parented to them are, like impact effects stuck to a compiled
	/// map's merged world geometry. They're recorded under the nearest ancestor that isn't hidden, with their
	/// transform relative to that.
	/// </summary>
	[TestMethod]
	public void RecordChildOfHiddenObject()
	{
		var map = new GameObject( "Map" ) { WorldPosition = new Vector3( 100f, 0f, 0f ) };
		var hidden = new GameObject( map, name: "World" ) { Flags = GameObjectFlags.Hidden };

		hidden.LocalPosition = new Vector3( 0f, 200f, 0f );
		hidden.LocalRotation = Rotation.FromYaw( 90f );

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () =>
		{
			var impact = new GameObject( hidden, name: "Impact" ) { WorldPosition = new Vector3( 50f, 60f, 70f ) };
			impact.AddComponent<ModelRenderer>();
		}
		) );

		Assert.IsNull( clip.GetReference<GameObject>( "Map", "World" ) );
		Assert.IsNotNull( clip.GetReference<ModelRenderer>( "Map", "Impact", nameof( ModelRenderer ) ) );

		// Created part way through, so needs an Enabled track

		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Impact", nameof( GameObject.Enabled ) ) );

		// Parent is the default (the Map track), so it's omitted

		Assert.IsNull( clip.GetProperty<BindingReference<GameObject>>( "Map", "Impact", nameof( GameObject.Parent ) ) );

		var posTrack = clip.GetProperty<Vector3>( "Map", "Impact", nameof( GameObject.LocalPosition ) );
		var rotTrack = clip.GetProperty<Rotation>( "Map", "Impact", nameof( GameObject.LocalRotation ) );

		Assert.IsNotNull( posTrack );
		Assert.IsTrue( posTrack.TryGetValue( 0.75, out var position ) );
		Assert.IsTrue( new Vector3( -50f, 60f, 70f ).AlmostEqual( position, 0.01f ) );

		// Map isn't rotated, so this is the world rotation, inherited from the hidden parent

		Assert.IsNotNull( rotTrack );
		Assert.IsTrue( rotTrack.TryGetValue( 0.75, out var rotation ) );
		Assert.IsTrue( Rotation.FromYaw( 90f ).Forward.AlmostEqual( rotation.Forward ) );
	}

	/// <summary>
	/// <see cref="Decal"/>s are recorded by default.
	/// </summary>
	[TestMethod]
	public void RecordDecal()
	{
		var clip = Record( MovieRecorderOptions.Default, 1.0, (0.5, () =>
		{
			new GameObject( "Impact" ).AddComponent<Decal>();
		}
		) );

		Assert.IsNotNull( clip.GetReference<Decal>( "Impact", nameof( Decal ) ) );
		Assert.IsNotNull( clip.GetProperty<bool>( "Impact", nameof( GameObject.Enabled ) ) );
	}

	/// <summary>
	/// Post-processing components are recorded by default.
	/// </summary>
	[TestMethod]
	public void RecordPostProcessing()
	{
		var go = new GameObject( "Post Processing" );

		var ao = go.AddComponent<AmbientOcclusion>();
		var vignette = go.AddComponent<Vignette>();
		var tonemapping = go.AddComponent<Tonemapping>();
		var bloom = go.AddComponent<Bloom>();

		var clip = Record( MovieRecorderOptions.Default, 1.0, t =>
		{
			var fraction = (float)t.TotalSeconds;

			ao.Intensity = fraction;
			vignette.Intensity = fraction;
			tonemapping.ExposureCompensation = fraction;
			bloom.Strength = fraction;
		} );

		Assert.IsNotNull( clip.GetProperty<float>( go.Name, nameof( AmbientOcclusion ), nameof( AmbientOcclusion.Intensity ) ) );
		Assert.IsNotNull( clip.GetProperty<float>( go.Name, nameof( Vignette ), nameof( Vignette.Intensity ) ) );
		Assert.IsNotNull( clip.GetProperty<float>( go.Name, nameof( Tonemapping ), nameof( Tonemapping.ExposureCompensation ) ) );
		Assert.IsNotNull( clip.GetProperty<float>( go.Name, nameof( Bloom ), nameof( Bloom.Strength ) ) );
	}

	/// <summary>
	/// Map objects that get disabled need <see cref="GameObject.Enabled"/> tracks, even though
	/// capturing stops finding them once they're disabled.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectDisabled()
	{
		var renderer = CreateMapProp();

		var clip = Record( CaptureAllRenderers, 1.0, time =>
		{
			renderer.GameObject.Enabled = time < 0.25 || time > 0.75;
		} );

		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map components that get disabled need <see cref="Component.Enabled"/> tracks.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapComponentDisabled()
	{
		var renderer = CreateMapProp();

		var clip = Record( CaptureAllRenderers, 1.0, time =>
		{
			renderer.Enabled = time < 0.25 || time > 0.75;
		} );

		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map objects that get destroyed need <see cref="GameObject.Enabled"/> tracks, so they're hidden after
	/// being destroyed. Their components don't need tracks of their own.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectDestroyed()
	{
		var renderer = CreateMapProp();

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () => renderer.GameObject.Destroy()) );

		var enabledTrack = clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) );

		Assert.IsNotNull( enabledTrack );
		Assert.IsTrue( enabledTrack.TryGetValue( 0.25, out var before ) && before );
		Assert.IsFalse( enabledTrack.TryGetValue( 0.75, out var after ) && after );

		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map components that get destroyed need <see cref="Component.Enabled"/> tracks.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapComponentDestroyed()
	{
		var renderer = CreateMapProp();

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () => renderer.Destroy()) );

		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Destroying a map object's parent only needs an <see cref="GameObject.Enabled"/> track for that parent.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectParentDestroyed()
	{
		var renderer = CreateMapProp();
		var group = new GameObject( renderer.GameObject.Parent, name: "Group" );

		renderer.GameObject.SetParent( group );

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () => group.Destroy()) );

		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Group", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Group", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Group", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map objects that start disabled and later get enabled need <see cref="GameObject.Enabled"/> tracks,
	/// so they're hidden until enabled. Their active parents, only captured once they're enabled, don't.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectStartsDisabled()
	{
		var renderer = CreateMapProp();
		var group = new GameObject( renderer.GameObject.Parent, name: "Group" );

		renderer.GameObject.SetParent( group );
		renderer.GameObject.Enabled = false;

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () => renderer.GameObject.Enabled = true) );

		var enabledTrack = clip.GetProperty<bool>( "Map", "Group", "Prop", nameof( GameObject.Enabled ) );

		Assert.IsNotNull( enabledTrack );
		Assert.IsFalse( enabledTrack.TryGetValue( 0.25, out var before ) && before );
		Assert.IsTrue( enabledTrack.TryGetValue( 0.75, out var after ) && after );

		Assert.IsNull( clip.GetProperty<bool>( "Map", "Group", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Group", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map objects inside a parent that starts disabled don't need <see cref="GameObject.Enabled"/> tracks of
	/// their own, the parent's track hides them until it's enabled.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectParentStartsDisabled()
	{
		var renderer = CreateMapProp();
		var group = new GameObject( renderer.GameObject.Parent, name: "Group" );

		renderer.GameObject.SetParent( group );
		group.Enabled = false;

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () => group.Enabled = true) );

		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Group", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Group", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNull( clip.GetProperty<bool>( "Map", "Group", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map objects created part way through a recording need <see cref="GameObject.Enabled"/> tracks,
	/// so they're hidden until they were created.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectCreated()
	{
		var map = new GameObject( "Map" );
		map.AddComponent<MapInstance>();

		var options = CaptureAllRenderers.WithCaptureAll<MapInstance>();

		var clip = Record( options, 1.0, (0.5, () =>
		{
			var prop = new GameObject( map, name: "Prop" );
			prop.AddComponent<ModelRenderer>();
		}
		) );

		var enabledTrack = clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) );

		Assert.IsNotNull( enabledTrack );
		Assert.IsFalse( enabledTrack.TryGetValue( 0.25, out var before ) && before );
		Assert.IsTrue( enabledTrack.TryGetValue( 0.75, out var after ) && after );

		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map components that start disabled and later get enabled need <see cref="Component.Enabled"/> tracks.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapComponentStartsDisabled()
	{
		var renderer = CreateMapProp();

		renderer.Enabled = false;

		var clip = Record( CaptureAllRenderers, 1.0, (0.5, () => renderer.Enabled = true) );

		Assert.IsNull( clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) ) );
		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Prop", nameof( ModelRenderer ), nameof( Component.Enabled ) ) );
	}

	/// <summary>
	/// Map objects disabled for the rest of the recording still need <see cref="GameObject.Enabled"/> tracks.
	/// </summary>
	[TestMethod]
	public void EnableTrackRecording_MapObjectDisabledUntilEnd()
	{
		var renderer = CreateMapProp();

		var clip = Record( CaptureAllRenderers, 1.0, time =>
		{
			renderer.GameObject.Enabled = time < 0.5;
		} );

		Assert.IsNotNull( clip.GetProperty<bool>( "Map", "Prop", nameof( GameObject.Enabled ) ) );
	}

	/// <summary>
	/// Capture a game object's <see cref="GameObjectFlags.Absolute"/>.
	/// </summary>
	[TestMethod]
	public void RecordAbsoluteFlag()
	{
		var go = new GameObject( "Example" );

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( go );

		var clip = Record( options, 3.0,
			(1.0, () => go.Flags |= GameObjectFlags.Absolute),
			(2.0, () => go.Flags &= ~GameObjectFlags.Absolute) );

		Console.WriteLine( Json.Serialize( clip ) );

		var flagTrack = clip.GetProperty<bool>( go.Name, nameof( GameObject.Flags ), nameof( GameObjectFlags.Absolute ) );

		Assert.IsNotNull( flagTrack );

		bool flag;

		Assert.IsTrue( flagTrack.TryGetValue( 0.5, out flag ) && !flag );
		Assert.IsTrue( flagTrack.TryGetValue( 1.5, out flag ) && flag );
		Assert.IsTrue( flagTrack.TryGetValue( 2.5, out flag ) && !flag );
	}

	/// <summary>
	/// Capture <see cref="Renderer.RenderOptions"/>.
	/// </summary>
	[TestMethod]
	public void RecordRenderOptions()
	{
		var renderer = new GameObject( "Example" )
			.AddComponent<ModelRenderer>();

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureComponent( renderer );

		var clip = Record( options, 3.0,
			(1.0, () =>
			{
				renderer.RenderOptions.Game = false;
				renderer.RenderOptions.Overlay = true;
			}
		),
			(2.0, () =>
			{
				renderer.RenderOptions.Game = true;
				renderer.RenderOptions.Overlay = false;
			}
		) );

		Console.WriteLine( Json.Serialize( clip ) );

		var gameTrack = clip.GetProperty<bool>( renderer.GameObject.Name, nameof( ModelRenderer ), nameof( Renderer.RenderOptions ), nameof( RenderOptions.Game ) );
		var overlayTrack = clip.GetProperty<bool>( renderer.GameObject.Name, nameof( ModelRenderer ), nameof( Renderer.RenderOptions ), nameof( RenderOptions.Overlay ) );

		Assert.IsNotNull( gameTrack );
		Assert.IsNotNull( overlayTrack );

		bool flag;

		Assert.IsTrue( gameTrack.TryGetValue( 0.5, out flag ) && flag );
		Assert.IsTrue( gameTrack.TryGetValue( 1.5, out flag ) && !flag );
		Assert.IsTrue( gameTrack.TryGetValue( 2.5, out flag ) && flag );

		Assert.IsTrue( overlayTrack.TryGetValue( 0.5, out flag ) && !flag );
		Assert.IsTrue( overlayTrack.TryGetValue( 1.5, out flag ) && flag );
		Assert.IsTrue( overlayTrack.TryGetValue( 2.5, out flag ) && !flag );
	}

	/// <summary>
	/// <see cref="TracerEffect.EndPoint"/> is recorded as its position and the object it's anchored to,
	/// and plays back onto another tracer.
	/// </summary>
	[TestMethod]
	public void RecordTracerEffect()
	{
		var anchorGo = new GameObject( "Anchor" ) { WorldPosition = new Vector3( 0f, 500f, 0f ) };

		var tracerGo = new GameObject( "Tracer" );
		var tracer = tracerGo.AddComponent<TracerEffect>();

		tracer.Length = 50f;
		tracer.EndPoint = new SceneAnchor { Parent = anchorGo, LocalPosition = new Vector3( 10f, 20f, 30f ) };

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( tracer )!.Capture() );

		var clip = Record( options, 0.5 );

		Console.WriteLine( Json.Serialize( clip ) );

		var lengthTrack = clip.GetProperty<float>( tracerGo.Name, nameof( TracerEffect ), nameof( TracerEffect.Length ) );
		var positionTrack = clip.GetProperty<Vector3>( tracerGo.Name, nameof( TracerEffect ), nameof( TracerEffect.EndPoint ), nameof( SceneAnchor.LocalPosition ) );
		var parentTrack = clip.GetProperty<BindingReference<GameObject>>( tracerGo.Name, nameof( TracerEffect ), nameof( TracerEffect.EndPoint ), nameof( SceneAnchor.Parent ) );

		Assert.IsNotNull( lengthTrack );
		Assert.IsNotNull( positionTrack );
		Assert.IsNotNull( parentTrack );

		Assert.IsTrue( lengthTrack.TryGetValue( 0.25, out var length ) );
		Assert.AreEqual( 50f, length );

		Assert.IsTrue( positionTrack.TryGetValue( 0.25, out var position ) );
		Assert.AreEqual( new Vector3( 10f, 20f, 30f ), position );

		Assert.IsTrue( parentTrack.TryGetValue( 0.25, out var parent ) );

		var anchorTrack = clip.Tracks.OfType<IReferenceTrack<GameObject>>().SingleOrDefault( x => x.Id == parent.TrackId );

		Assert.IsNotNull( anchorTrack );
		Assert.AreEqual( anchorGo.Name, anchorTrack.Name );

		// Play back the end point onto another tracer

		var otherGo = new GameObject( "Other Tracer" );
		var other = otherGo.AddComponent<TracerEffect>();
		var binder = new TrackBinder( Game.ActiveScene );

		binder.Get( (IReferenceTrack)positionTrack.Parent!.Parent! ).Bind( other );

		var positionProperty = (ITrackProperty<Vector3>)binder.Get( positionTrack );

		Assert.IsTrue( positionProperty.IsBound );

		positionProperty.Value = new Vector3( 1f, 2f, 3f );

		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), other.EndPoint.LocalPosition );
	}

	/// <summary>
	/// <see cref="HighlightOutline"/> records its colours, and its manual <see cref="HighlightOutline.Targets"/>
	/// as references to the renderers' tracks.
	/// </summary>
	[TestMethod]
	public void RecordHighlightOutline()
	{
		var go = new GameObject( "Example" );
		var outline = go.AddComponent<HighlightOutline>();

		var targetGo = new GameObject( go, true, "Target" );
		var target = targetGo.AddComponent<ModelRenderer>();

		outline.Color = Color.Red;
		outline.Width = 0.5f;
		outline.OverrideTargets = true;
		outline.Targets = [target];

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( outline )!.Capture() );

		var clip = Record( options, 0.5 );

		Console.WriteLine( Json.Serialize( clip ) );

		var colorTrack = clip.GetProperty<Color>( go.Name, nameof( HighlightOutline ), nameof( HighlightOutline.Color ) );
		var widthTrack = clip.GetProperty<float>( go.Name, nameof( HighlightOutline ), nameof( HighlightOutline.Width ) );
		var targetTrack = clip.GetProperty<BindingReference<Renderer>>( go.Name, nameof( HighlightOutline ), nameof( HighlightOutline.Targets ), "0" );

		Assert.IsNotNull( colorTrack );
		Assert.IsNotNull( widthTrack );
		Assert.IsNotNull( targetTrack );

		Assert.IsTrue( colorTrack.TryGetValue( 0.25, out var color ) );
		Assert.AreEqual( Color.Red, color );

		Assert.IsTrue( widthTrack.TryGetValue( 0.25, out var width ) );
		Assert.AreEqual( 0.5f, width );

		Assert.IsTrue( targetTrack.TryGetValue( 0.25, out var reference ) );

		var referencedTrack = clip.Tracks.OfType<IReferenceTrack>().SingleOrDefault( x => x.Id == reference.TrackId );

		Assert.IsNotNull( referencedTrack );
		Assert.AreEqual( typeof( ModelRenderer ), referencedTrack.TargetType );
		Assert.AreEqual( targetGo.Name, referencedTrack.Parent?.Name );
	}

	private const string OutlineShaderPath = "shaders/postprocess/objecthighlight/objecthighlight.shader";

	/// <summary>
	/// A <see cref="HighlightOutline"/> with a material made at runtime records the material's shader,
	/// because a reference to the material itself can't be loaded again.
	/// </summary>
	[TestMethod]
	public void RecordHighlightOutlineRuntimeMaterial()
	{
		var go = new GameObject( "Example" );
		var outline = go.AddComponent<HighlightOutline>();

		outline.Material = Material.Create( "movie_outline_test", OutlineShaderPath );

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( outline )!.Capture() );

		var clip = Record( options, 0.5 );

		clip = Json.Deserialize<MovieClip>( Json.Serialize( clip ) )!;

		var materialTrack = clip.GetProperty<Material>( go.Name, nameof( HighlightOutline ), nameof( HighlightOutline.Material ) );
		var shaderTrack = clip.GetProperty<Shader>( go.Name, nameof( HighlightOutline ), nameof( HighlightOutline.Material ), MaterialShaderProperty.PropertyName );

		// The material track only holds the shader track, it has no values of its own

		Assert.IsFalse( materialTrack?.TryGetValue( 0.25, out _ ) ?? false );
		Assert.IsNotNull( shaderTrack );

		Assert.IsTrue( shaderTrack.TryGetValue( 0.25, out var shader ) );
		Assert.AreEqual( outline.Material.Shader, shader );
	}

	/// <summary>
	/// Writing <see cref="MaterialShaderProperty"/> gives the outline a material with that shader,
	/// and leaves it alone if its material already has that shader.
	/// </summary>
	[TestMethod]
	public void PlayHighlightOutlineShader()
	{
		var go = new GameObject( "Example" );
		var outline = go.AddComponent<HighlightOutline>();

		var binder = new TrackBinder( Game.ActiveScene );
		var outlineTrack = MovieClip.RootGameObject( go.Name ).Component<HighlightOutline>();

		binder.Get( outlineTrack ).Bind( outline );

		var property = (ITrackProperty<Shader?>)binder.Get( outlineTrack
			.Property<Material>( nameof( HighlightOutline.Material ) )
			.Property<Shader>( MaterialShaderProperty.PropertyName ) );
		var shader = Shader.Load( OutlineShaderPath );

		Assert.IsNull( property.Value );

		property.Value = shader;

		Assert.IsNotNull( outline.Material );
		Assert.AreEqual( shader, outline.Material.Shader );

		var material = Material.Create( "movie_outline_test", OutlineShaderPath );

		outline.Material = material;
		property.Value = shader;

		Assert.AreSame( material, outline.Material );

		property.Value = null;

		Assert.IsNull( outline.Material );
	}

	/// <summary>
	/// Like <see cref="RecordHighlightOutlineRuntimeMaterial"/>, but for <see cref="ModelRenderer.MaterialOverride"/>
	/// set to a copy of a material, like the physgun's grab effect does.
	/// </summary>
	[TestMethod]
	public void RecordModelRendererRuntimeMaterialOverride()
	{
		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<ModelRenderer>();

		renderer.MaterialOverride = Material.Create( "movie_override_test", OutlineShaderPath ).CreateCopy();

		var clip = Record( CaptureAllRenderers, 0.5 );

		clip = Json.Deserialize<MovieClip>( Json.Serialize( clip ) )!;

		var materialTrack = clip.GetProperty<Material>( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.MaterialOverride ) );
		var shaderTrack = clip.GetProperty<Shader>( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.MaterialOverride ), MaterialShaderProperty.PropertyName );

		// The material track only holds the shader track, it has no values of its own

		Assert.IsFalse( materialTrack?.TryGetValue( 0.25, out _ ) ?? false );
		Assert.IsNotNull( shaderTrack );

		Assert.IsTrue( shaderTrack.TryGetValue( 0.25, out var shader ) );
		Assert.AreEqual( renderer.MaterialOverride.Shader, shader );
	}

	/// <summary>
	/// A prefab instance's <see cref="ModelRenderer.MaterialOverride"/> set to a runtime material, then back to the
	/// prefab's default. The material track must be kept so playback restores the default, even though it only
	/// ever recorded that default.
	/// </summary>
	[TestMethod]
	public void RecordRuntimeMaterialRestoredToDefault()
	{
		const string prefabPath = "prefabs/example.prefab";

		RegisterSimplePrefab( prefabPath, new JsonObject
		{
			{ "__type", "ModelRenderer" }
		} );

		var go = GameObject.GetPrefab( prefabPath ).Clone();
		var renderer = go.GetComponent<ModelRenderer>();
		var runtimeMaterial = Material.Create( "movie_restore_test", OutlineShaderPath ).CreateCopy();

		var clip = Record( CaptureAllRenderers, 1.0, time =>
		{
			renderer.MaterialOverride = time >= 0.25 && time < 0.5 ? runtimeMaterial : null;
		} );

		clip = Json.Deserialize<MovieClip>( Json.Serialize( clip ) )!;

		var materialTrack = clip.GetProperty<Material>( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.MaterialOverride ) );
		var shaderTrack = clip.GetProperty<Shader>( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.MaterialOverride ), MaterialShaderProperty.PropertyName );

		Assert.IsNotNull( shaderTrack );
		Assert.IsTrue( shaderTrack.TryGetValue( 0.375, out var shader ) );
		Assert.AreEqual( runtimeMaterial.Shader, shader );

		Assert.IsNotNull( materialTrack );
		Assert.IsFalse( materialTrack.TryGetValue( 0.375, out _ ) );
		Assert.IsTrue( materialTrack.TryGetValue( 0.75, out var restored ) );
		Assert.IsNull( restored );
	}

	[TestMethod]
	public void RecordTextRenderer()
	{
		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<TextRenderer>();

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( renderer )!.Capture() );

		renderer.Text = "Hello, worlb!";

		var clip = Record( options, 1d );

		Console.WriteLine( Json.Serialize( clip ) );

		var textTrack = clip.GetProperty<string>( go.Name, nameof( TextRenderer ), nameof( TextRenderer.TextScope ), nameof( TextRendering.Scope.Text ) );

		Assert.IsNotNull( textTrack );

		Assert.IsTrue( textTrack.TryGetValue( 0.5, out var value ) );
		Assert.AreEqual( "Hello, worlb!", value );
	}

	/// <summary>
	/// Renderers using an AnimGraph2 graph record <see cref="Sandbox.Animation.AnimationAccessor.Parameters"/>.
	/// </summary>
	[TestMethod]
	public void RecordGraphParameters()
	{
		const string paramName = "move_x";

		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();

		renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = Sandbox.Animation.Graph.Load( "models/citizen_human/animgraphs/human.ag" );

		var graph = renderer.Animation.Graph;

		Console.WriteLine( $"Graph: {graph?.ResourcePath}, IsValid: {renderer.Animation.IsValid}" );
		Console.WriteLine( $"Parameters: {string.Join( ", ", graph?.Parameters.Select( x => $"{x.Name}:{x.Type.Name}" ) ?? [] )}" );

		if ( graph is null ) Assert.Inconclusive( "Human anim graph isn't available." );

		var duration = MovieTime.FromSeconds( 1d );

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( renderer )!.Capture() );

		var clip = Record( options, duration, t =>
		{
			Assert.IsTrue( renderer.Animation.Parameters.TrySet( paramName, duration.GetFraction( t ) * 100f ) );
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		var paramTrack = clip.GetProperty<float>( go.Name, nameof( SkinnedModelRenderer ),
			nameof( SkinnedModelRenderer.Animation ), nameof( Sandbox.Animation.AnimationAccessor.Parameters ), paramName );

		Assert.IsNotNull( paramTrack );
		Assert.IsTrue( paramTrack.TryGetValue( duration, out var value ) );
		Assert.AreEqual( 100f, value, 0.01f );
	}

	/// <summary>
	/// Bone objects posed by code, flagged with <see cref="GameObjectFlags.ProceduralBone"/> like a vehicle's wheels,
	/// have their transforms recorded. Other bone objects are posed by animation, so they don't.
	/// </summary>
	[TestMethod]
	public void RecordProceduralBones()
	{
		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();

		renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );
		renderer.CreateBoneObjects = true;

		var procedural = renderer.GetBoneObject( "hand_R" );
		var animated = renderer.GetBoneObject( "hand_L" );

		Assert.IsNotNull( procedural );
		Assert.IsNotNull( animated );

		procedural.Flags |= GameObjectFlags.ProceduralBone;

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( renderer )!.Capture() );

		var clip = Record( options, 1.0, t =>
		{
			procedural.LocalRotation = Rotation.FromRoll( (float)t.TotalSeconds * 90f );
		} );

		Rotation? GetRotation( string boneName ) => clip.Tracks
			.OfType<IPropertyTrack<Rotation>>()
			.FirstOrDefault( x => x.Name == nameof( GameObject.LocalRotation ) && x.Parent?.Name == boneName ) is { } track
			&& track.TryGetValue( 1.0, out var rotation ) ? rotation : null;

		Assert.IsNotNull( GetRotation( "hand_R" ) );
		Assert.IsTrue( Rotation.FromRoll( 90f ).Forward.AlmostEqual( GetRotation( "hand_R" )!.Value.Forward ) );

		Assert.IsNull( GetRotation( "hand_L" ) );
	}

	/// <summary>
	/// A renderer using an AnimGraph2 graph, and the name of one of its Bool parameters.
	/// </summary>
	private static (SkinnedModelRenderer Renderer, string BoolParamName) CreateAnimGraph2Renderer()
	{
		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();

		renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = Sandbox.Animation.Graph.Load( "models/citizen_human/animgraphs/human.ag" );

		if ( renderer.Animation.Graph?.Parameters.FirstOrDefault( x => x.Type == typeof( bool ) ) is not { } boolParam )
		{
			Assert.Inconclusive( "Human anim graph with a Bool parameter isn't available." );
			throw new InvalidOperationException();
		}

		return (renderer, boolParam.Name);
	}

	private static string[] TriggerTrackPath( SkinnedModelRenderer renderer, string paramName ) =>
	[
		renderer.GameObject.Name, nameof( SkinnedModelRenderer ),
		nameof( SkinnedModelRenderer.Animation ), GraphTriggerAccessor.PropertyName, paramName
	];

	/// <summary>
	/// One-shot pulses from <see cref="Sandbox.Animation.GraphParameters.Trigger"/> don't change the parameter's value,
	/// so they're recorded to their own track, true for one sample. Bool parameters that never fire don't get one.
	/// </summary>
	[TestMethod]
	public void RecordGraphTrigger()
	{
		var (renderer, paramName) = CreateAnimGraph2Renderer();

		var triggerTime = MovieTime.FromSeconds( 0.5d );
		var triggered = false;

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( renderer )!.Capture() );

		var clip = Record( options, 1d, t =>
		{
			// Evaluating the graph uses up any pulse from last frame

			renderer.SceneModel!.Update( 0.01f );

			if ( triggered || t < triggerTime ) return;

			triggered = true;
			Assert.IsTrue( renderer.Animation.Parameters.TryTrigger( paramName ) );
		} );

		// Make sure the pseudo-property's type survives a round trip

		clip = Json.Deserialize<MovieClip>( Json.Serialize( clip ) )!;

		Console.WriteLine( Json.Serialize( clip ) );

		var triggerTrack = clip.GetProperty<bool>( TriggerTrackPath( renderer, paramName ) );

		Assert.IsNotNull( triggerTrack );

		Assert.IsFalse( triggerTrack.TryGetValue( 0.25d, out _ ), "Shouldn't record until first triggered." );
		Assert.IsTrue( triggerTrack.TryGetValue( triggerTime, out var pulse ) );
		Assert.IsTrue( pulse );
		Assert.IsTrue( triggerTrack.TryGetValue( 0.75d, out var after ) );
		Assert.IsFalse( after );

		Assert.AreEqual( 1, clip.Tracks.OfType<IPropertyTrack>().Count( x => x.Parent?.Name == GraphTriggerAccessor.PropertyName ),
			"Only the triggered parameter should have a trigger track." );
	}

	/// <summary>
	/// Playing a trigger track queues one pulse when it becomes true, even though it stays true for several frames.
	/// </summary>
	[TestMethod]
	public void PlayGraphTrigger()
	{
		var (renderer, paramName) = CreateAnimGraph2Renderer();

		var binder = new TrackBinder( Game.ActiveScene );
		var rendererTrack = MovieClip.RootGameObject( renderer.GameObject.Name ).Component<SkinnedModelRenderer>();

		binder.Get( rendererTrack ).Bind( renderer );

		var triggerTrack = rendererTrack
			.Property<Sandbox.Animation.AnimationAccessor>( nameof( SkinnedModelRenderer.Animation ) )
			.Property<GraphTriggerAccessor>( GraphTriggerAccessor.PropertyName )
			.Property<bool>( paramName );

		var property = (ITrackProperty<bool>)binder.Get( triggerTrack );

		Assert.IsTrue( property.IsBound );
		Assert.IsFalse( property.Value );

		var ownValue = renderer.Animation.Parameters.GetBool( paramName );

		property.Value = true;

		Assert.IsTrue( property.Value );
		Assert.IsTrue( renderer.Animation.Parameters.IsTriggerPending( paramName ) );
		Assert.AreEqual( ownValue, renderer.Animation.Parameters.GetBool( paramName ), "Triggering shouldn't change the parameter's own value." );

		// Evaluating uses up the pulse, writing true again shouldn't queue another

		renderer.SceneModel!.Update( 0.01f );
		property.Value = true;

		Assert.IsFalse( renderer.Animation.Parameters.IsTriggerPending( paramName ) );

		// Until it goes false, then true again

		property.Value = false;
		property.Value = true;

		Assert.IsTrue( renderer.Animation.Parameters.IsTriggerPending( paramName ) );
	}

	/// <summary>
	/// An AnimGraph2 bool parameter that's only true for one frame between samples, like <c>b_jump</c>
	/// set by <see cref="PlayerController"/>, is still recorded for one sample.
	/// </summary>
	[TestMethod]
	public void RecordGraphParameterPulse()
	{
		const string paramName = "b_jump";

		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();

		renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = Sandbox.Animation.Graph.Load( "models/citizen_human/animgraphs/human.ag" );

		if ( renderer.Animation.Graph is null ) Assert.Inconclusive( "Human anim graph isn't available." );

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( renderer )!.Capture() );

		// Record() advances 4 frames per sample, so this frame is between two samples

		var sampleRate = options.SampleRate;
		var jumpFrame = MovieTime.FromFrames( sampleRate * 4 + 1, sampleRate * 4 );
		var nextSample = MovieTime.FromFrames( sampleRate + 1, sampleRate );

		var clip = Record( options, 2d, t =>
		{
			renderer.Animation.Parameters.TrySet( paramName, t == jumpFrame );
		} );

		var paramTrack = clip.GetProperty<bool>( go.Name, nameof( SkinnedModelRenderer ),
			nameof( SkinnedModelRenderer.Animation ), nameof( Sandbox.Animation.AnimationAccessor.Parameters ), paramName );

		Assert.IsNotNull( paramTrack );

		Assert.IsTrue( paramTrack.TryGetValue( 0.5, out var before ) && !before );
		Assert.IsTrue( paramTrack.TryGetValue( nextSample, out var during ) && during );
		Assert.IsTrue( paramTrack.TryGetValue( 1.5, out var after ) && !after );
	}

	/// <summary>
	/// A recorded <see cref="BindingReference{T}"/> must reference the track recorded for the referenced object, even if
	/// the binder has other tracks bound to that object (like an editor project's own tracks).
	/// </summary>
	[TestMethod]
	public void RecordBoneMergeTargetWithSharedBinder()
	{
		var bodyGo = new GameObject( "Body" );
		var body = bodyGo.AddComponent<SkinnedModelRenderer>();

		var clothingGo = new GameObject( bodyGo, true, "Clothing" );
		var clothing = clothingGo.AddComponent<SkinnedModelRenderer>();

		clothing.BoneMergeTarget = body;

		// Some other track is already bound to the body renderer

		var binder = new TrackBinder( Game.ActiveScene );
		var otherTrackId = Guid.NewGuid();

		binder.Bind( otherTrackId, body );

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( clothing )!.Capture() );

		var recorder = new MovieRecorder( binder, options );

		for ( var i = 0; i < 8; i++ )
		{
			recorder.Capture();
			recorder.Advance( MovieTime.FromFrames( 1, options.SampleRate ) );
		}

		var clip = recorder.ToClip();

		Console.WriteLine( Json.Serialize( clip ) );

		var mergeTargetTrack = clip.GetProperty<BindingReference<SkinnedModelRenderer>>(
			bodyGo.Name, clothingGo.Name, nameof( SkinnedModelRenderer ), nameof( SkinnedModelRenderer.BoneMergeTarget ) );

		Assert.IsNotNull( mergeTargetTrack );
		Assert.IsTrue( mergeTargetTrack.TryGetValue( 0d, out var value ) );

		Assert.AreNotEqual( otherTrackId, value.TrackId );

		var referencedTrack = clip.Tracks.OfType<IReferenceTrack>().SingleOrDefault( x => x.Id == value.TrackId );

		Assert.IsNotNull( referencedTrack, "Referenced track isn't in the clip." );
		Assert.AreEqual( typeof( SkinnedModelRenderer ), referencedTrack.TargetType );
		Assert.AreEqual( bodyGo.Name, referencedTrack.Parent?.Name );
	}

	/// <summary>
	/// An auto-reset anim graph parameter set for one frame between samples, then reset by the graph,
	/// is still recorded for one sample.
	/// </summary>
	[TestMethod]
#pragma warning disable CS0618
	public void RecordAutoResetAnimParameter()
	{
		const string paramName = "b_attack";

		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();

		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );
		renderer.UseAnimGraph = true;

		if ( renderer.Parameters.Graph is null ) Assert.Inconclusive( "Citizen anim graph isn't available." );

		Assert.IsTrue( renderer.IsLegacyParameterAutoReset( paramName ) );

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x => x.GetTrackRecorder( renderer )!.Capture() );

		// Only parameters that have been set are recorded

		renderer.Set( paramName, false );

		// Record() advances 4 frames per sample, so this frame is between two samples

		var sampleRate = options.SampleRate;
		var attackFrame = MovieTime.FromFrames( sampleRate * 4 + 1, sampleRate * 4 );
		var nextSample = MovieTime.FromFrames( sampleRate + 1, sampleRate );

		var clip = Record( options, 2d, t =>
		{
			// Like a real frame: last frame's graph update resets auto-reset parameters, then components set them,
			// then we capture

			renderer.SceneModel.Update( 0.01f );

			Assert.IsFalse( renderer.GetBool( paramName ) );

			if ( t == attackFrame )
			{
				renderer.Set( paramName, true );
			}
		} );

		var paramTrack = clip.GetProperty<bool>( go.Name, nameof( SkinnedModelRenderer ), nameof( SkinnedModelRenderer.Parameters ), paramName );

		Assert.IsNotNull( paramTrack );

		Assert.IsTrue( paramTrack.TryGetValue( 0.5, out var before ) && !before );
		Assert.IsTrue( paramTrack.TryGetValue( nextSample, out var during ) && during );
		Assert.IsTrue( paramTrack.TryGetValue( 1.5, out var after ) && !after );
	}
#pragma warning restore CS0618

	/// <summary>
	/// When using <see cref="MovieRecorderOptions.Default"/>, all <see cref="ModelRenderer"/>s should be recorded.
	/// </summary>
	[TestMethod]
	public void AutoRecordRenderers()
	{
		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<ModelRenderer>();

		var duration = MovieTime.FromSeconds( 10d );

		var clip = Record( duration, t =>
		{
			renderer.Tint = Color.Lerp( Color.Red, Color.Blue, duration.GetFraction( t ) );
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		// Example object should be recorded, including a ModelRenderer.Tint track

		var tintTrack = clip.GetTrack( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.Tint ) ) as CompiledPropertyTrack<Color>;

		Assert.IsNotNull( tintTrack );
	}

	/// <summary>
	/// Record a property value that references another <see cref="GameObject"/> as a late-bound property.
	/// </summary>
	[TestMethod]
	public void RecordGameObjectReference()
	{
		var foo = new GameObject( "Foo" );
		var rope = foo.AddComponent<VerletRope>();

		var bar = new GameObject( "Bar" );

		// Foo's rope references Bar

		rope.Attachment = bar;

		var options = new MovieRecorderOptions()
			.WithCaptureAction( x => x
				.GetTrackRecorder( rope )!
				.Property( nameof( VerletRope.Attachment ) )
				.Capture() );

		var clip = Record( options, 1d );

		Console.WriteLine( Json.Serialize( clip ) );

		var attachmentTrack = clip.GetReferenceProperty<GameObject>( "Foo", nameof( VerletRope ), nameof( VerletRope.Attachment ) );
		var barTrack = clip.GetReference<GameObject>( "Bar" );

		Assert.IsNotNull( attachmentTrack );
		Assert.IsNotNull( barTrack );

		Assert.IsTrue( attachmentTrack.TryGetValue( 0.5, out var value ) );
		Assert.AreEqual( barTrack.Id, value.TrackId );
	}

	/// <summary>
	/// A value that changed between samples, just before a recorder stopped being captured for a while, must not
	/// be written as the first sample once it's captured again.
	/// </summary>
	[TestMethod]
	public void DontRecordStaleValueAfterGap()
	{
		var camera = new GameObject( "Camera" ).AddComponent<CameraComponent>();
		var capturing = true;

		var options = new MovieRecorderOptions()
			.WithDefaultComponentCapturers()
			.WithCaptureAction( x =>
			{
				if ( capturing ) x.GetTrackRecorder( camera )!.Capture();
			} );

		var recorder = new MovieRecorder( Game.ActiveScene, options );
		var dt = MovieTime.FromFrames( 1, options.SampleRate * 4 );

		void Step()
		{
			recorder.Advance( dt );
			recorder.Capture();
		}

		recorder.Capture();

		// Only true between two samples

		recorder.Advance( dt );
		camera.Orthographic = true;
		recorder.Capture();
		camera.Orthographic = false;

		// Not captured, but not skipped either

		capturing = false;

		while ( recorder.Time < 2d ) Step();

		capturing = true;

		while ( recorder.Time < 3d ) Step();

		var clip = recorder.ToClip();

		Console.WriteLine( Json.Serialize( clip ) );

		var track = clip.GetTrack( camera.GameObject.Name, nameof( CameraComponent ), nameof( CameraComponent.Orthographic ) ) as CompiledPropertyTrack<bool>;

		Assert.IsNotNull( track );

		var resumed = track.Blocks.Last();

		Assert.IsTrue( resumed.TimeRange.Start >= 2d );
		Assert.IsTrue( track.TryGetValue( resumed.TimeRange.Start, out var value ) );
		Assert.IsFalse( value );
	}

	/// <summary>
	/// We can re-use track recorders if they've become unbound.
	/// </summary>
	[TestMethod]
	public void ReuseTrackRecorder()
	{
		GameObject? foo = null;

		var options = new MovieRecorderOptions()
			.WithCaptureAction( x => x.GetTrackRecorder( foo )?.Capture() );

		var clip = Record( options, 5d, time =>
		{
			if ( time >= 1d && time <= 2d )
			{
				foo ??= new GameObject( "Foo" );
			}
			else if ( time >= 3d && time <= 4d )
			{
				foo ??= new GameObject( "Foo (2)" );
			}
			else
			{
				foo?.Destroy();
				foo = null;
			}
		} );

		var posTrack = clip.GetTrack( "Foo", nameof( GameObject.LocalPosition ) ) as CompiledPropertyTrack<Vector3>;

		Assert.IsNotNull( posTrack );

		Assert.AreEqual( 2, posTrack.Blocks.Length );

		Assert.IsFalse( posTrack.TryGetValue( 0.5d, out _ ) );
		Assert.IsTrue( posTrack.TryGetValue( 1.5d, out _ ) );
		Assert.IsFalse( posTrack.TryGetValue( 2.5d, out _ ) );
		Assert.IsTrue( posTrack.TryGetValue( 3.5d, out _ ) );
		Assert.IsFalse( posTrack.TryGetValue( 4.5d, out _ ) );
	}

	/// <summary>
	/// We help keep recordings slim by ignoring unchanged property values in prefab instances.
	/// </summary>
	[TestMethod]
	public void DontRecordDefaultPrefabProperty()
	{
		const string prefabPath = "prefabs/example.prefab";

		RegisterSimplePrefab( prefabPath, new JsonObject
		{
			{ "__type", "ModelRenderer" },
			{ "Tint", "1,0,0,1" }
		} );

		var go = GameObject.GetPrefab( prefabPath ).Clone();

		var startPos = new Vector3( 0f, 0f, 0f );
		var endPos = new Vector3( 500f, 100f, -200f );

		var duration = MovieTime.FromSeconds( 10d );

		var clip = Record( duration, t =>
		{
			go.WorldPosition = Vector3.Lerp( startPos, endPos, duration.GetFraction( t ) );
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		var rendererTrack = clip.GetTrack( go.Name, nameof( ModelRenderer ) );

		// The renderer should have a track, to make sure it's instantiated when calling TrackBinder.CreateTargets

		Assert.IsNotNull( rendererTrack );

		var tintTrack = clip.GetTrack( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.Tint ) );

		// Tint is always the prefab's default value, so shouldn't have been recorded

		Assert.IsNull( tintTrack );
	}

	/// <summary>
	/// Make sure that we do actually record properties that changed from their source prefab.
	/// </summary>
	[TestMethod]
	public void RecordChangingPrefabProperty()
	{
		const string prefabPath = "prefabs/example.prefab";

		RegisterSimplePrefab( prefabPath, new JsonObject
		{
			{ "__type", "ModelRenderer" },
			{ "Tint", "1,0,0,1" }
		} );

		var go = GameObject.GetPrefab( prefabPath ).Clone();
		var renderer = go.GetComponent<ModelRenderer>();

		var duration = MovieTime.FromSeconds( 10d );

		var clip = Record( duration, t =>
		{
			renderer.Tint = Color.Lerp( Color.Red, Color.Blue, duration.GetFraction( t ) );
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		var tintTrack = clip.GetTrack( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.Tint ) );

		// Tint changed from the prefab's default value, so it should be recorded

		Assert.IsNotNull( tintTrack );
	}

	[TestMethod]
	public void ChangingParent()
	{
		var foo = new GameObject( "Foo" );
		var bar = new GameObject( "Bar" );

		var options = new MovieRecorderOptions()
			.WithCaptureGameObject( foo )
			.WithCaptureGameObject( bar );

		var recorder = new MovieRecorder( Game.ActiveScene, options );

		recorder.Capture();
		recorder.Advance( 0.9d );
		recorder.Capture();
		recorder.Advance( 0.1d );

		foo.Parent = bar;

		recorder.Capture();
		recorder.Advance( 0.9d );
		recorder.Capture();
		recorder.Advance( 0.1d );

		foo.Parent = null;

		recorder.Capture();
		recorder.Advance( 1d );
		recorder.Capture();

		var clip = recorder.ToClip();

		Console.WriteLine( Json.Serialize( clip ) );

		var barTrack = clip.GetReference<GameObject>( bar.Name );

		Assert.IsNotNull( barTrack );

		var parentTrack = clip.GetReferenceProperty<GameObject>( foo.Name, nameof( GameObject.Parent ) );

		// Foo's parent changed, so we should have recorded a track for that

		Assert.IsNotNull( parentTrack );

		BindingReference<GameObject> parent;

		Assert.IsTrue( parentTrack.TryGetValue( 0.5d, out parent ) );
		Assert.AreEqual( null, parent.TrackId );
		Assert.IsTrue( parentTrack.TryGetValue( 1.5d, out parent ) );
		Assert.AreEqual( barTrack.Id, parent.TrackId );
		Assert.IsTrue( parentTrack.TryGetValue( 2.5d, out parent ) );
		Assert.AreEqual( null, parent.TrackId );

		// We shouldn't have a Bar.Parent track, because its parent
		// never changes

		Assert.IsNull( clip.GetReferenceProperty<GameObject>( bar.Name, nameof( GameObject.Parent ) ) );
	}

	/// <summary>
	/// We can use <see cref="MovieRecorder.Start"/> and <see cref="MovieRecorder.Stop"/> to capture every fixed update.
	/// </summary>
	[TestMethod]
	public void StartStop()
	{
		var go = new GameObject( "Example" );
		var renderer = go.AddComponent<ModelRenderer>();

		var recorder = new MovieRecorder( Game.ActiveScene );

		// Start capturing every fixed update

		recorder.Start();

		var duration = MovieTime.FromSeconds( 10d );

		while ( recorder.Time < duration )
		{
			var prevTime = recorder.Time;

			renderer.Tint = Color.Lerp( Color.Red, Color.Blue, duration.GetFraction( recorder.Time ) );

			// GameTick() should run at least one fixed update, which triggers a capture

			Game.ActiveScene.GameTick();

			// Safety to avoid an infinite loop

			Assert.IsTrue( recorder.Time > prevTime );
		}

		// Stop capturing

		recorder.Stop();

		var clip = recorder.ToClip();

		Console.WriteLine( Json.Serialize( clip ) );

		var tintTrack = clip.GetTrack( go.Name, nameof( ModelRenderer ), nameof( ModelRenderer.Tint ) ) as CompiledPropertyTrack<Color>;

		// We should have recorded the tint changing automatically

		Assert.IsNotNull( tintTrack );

		// We actually have track data

		Assert.IsTrue( tintTrack.TryGetValue( 5d, out _ ) );
	}

	[TestMethod]
	public void RecordMultipleComponents()
	{
		var foo = new GameObject( "Foo" );

		var renderer1 = foo.AddComponent<ModelRenderer>();
		var renderer2 = foo.AddComponent<ModelRenderer>();

		renderer1.Tint = Color.Red;
		renderer2.Tint = Color.Blue;

		var clip = Record( 1d );

		var colorTracks = clip.Tracks.OfType<CompiledPropertyTrack<Color>>()
			.Where( x => x.Name == nameof( ModelRenderer.Tint ) )
			.ToArray();

		Assert.AreEqual( 2, colorTracks.Length );

		Assert.IsTrue( colorTracks[0].TryGetValue( 0.5, out var color1 ) );
		Assert.IsTrue( colorTracks[1].TryGetValue( 0.5, out var color2 ) );

		var colors = new[] { color1, color2 };

		CollectionAssert.Contains( colors, Color.Red );
		CollectionAssert.Contains( colors, Color.Blue );
	}

	[TestMethod]
	public void RecordRingBuffer()
	{
		var go = new GameObject( "Example" );

		var options = new MovieRecorderOptions( BufferDuration: 10.0 )
			.WithCaptureAction( x => x.GetTrackRecorder( go )!.Capture() );

		var clip = Record( options, 60.0, t =>
		{
			go.WorldPosition = Vector3.Forward * (float)t.TotalSeconds * 10f;
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		Assert.AreEqual( options.BufferDuration!.Value, clip.Duration );

		var positionTrack = clip.GetProperty<Vector3>( go.Name, nameof( GameObject.LocalPosition ) );

		Assert.IsNotNull( positionTrack );
		Assert.AreEqual( (0.0, 10.0), positionTrack.Blocks[0].TimeRange );
	}

	[TestMethod]
	public void RecordRingBufferDropEmptyTracks()
	{
		var go1 = new GameObject( "Example 1" );
		var go2 = new GameObject( "Example 2" );

		var options = new MovieRecorderOptions( BufferDuration: 10.0 )
			.WithCaptureAction( x =>
			{
				x.GetTrackRecorder( go1 )?.Capture();
				x.GetTrackRecorder( go2 )?.Capture();
			} );

		var clip = Record( options, 60.0, t =>
		{
			if ( t >= 30.0 )
			{
				go1?.Destroy();
				go1 = null;
			}

			go1?.WorldPosition = Vector3.Forward * (float)t.TotalSeconds * 10f;
			go2.WorldPosition = Vector3.Up * (float)t.TotalSeconds * 10f;
		} );

		Console.WriteLine( Json.Serialize( clip ) );

		Assert.AreEqual( options.BufferDuration!.Value, clip.Duration );

		// Example 1 stopped existing before the recorded time range started,
		// so the recording shouldn't include it at all

		Assert.IsNull( clip.GetReference<GameObject>( "Example 1" ) );
		Assert.IsNotNull( clip.GetReference<GameObject>( "Example 2" ) );
	}

	/// <summary>
	/// Recorded tracks get assigned an ascending <see cref="TrackMetadata.Order"/>.
	/// </summary>
	[TestMethod]
	public void TrackOrderAscending()
	{
		var go1 = new GameObject( "Foo" );
		var go2 = new GameObject( "Bar" );

		var options = new MovieRecorderOptions()
			.WithCaptureAction( x =>
			{
				x.GetTrackRecorder( go1 )?.Capture();

				if ( x.Time > 5.0 )
				{
					x.GetTrackRecorder( go2 )?.Capture();
				}
			} );

		var clip = Record( options, 10.0 );

		Console.WriteLine( Json.Serialize( clip ) );

		var track1 = clip.GetReference<GameObject>( "Foo" );
		var track2 = clip.GetReference<GameObject>( "Bar" );

		Assert.IsNotNull( track1 );
		Assert.IsNotNull( track2 );

		Assert.IsTrue( track1.Metadata?.Order < track2.Metadata?.Order );
	}
}
