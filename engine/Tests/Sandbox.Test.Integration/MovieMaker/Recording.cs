using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Sandbox.MovieMaker.Properties;

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
}
