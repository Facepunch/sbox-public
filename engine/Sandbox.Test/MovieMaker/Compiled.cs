using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;

namespace TestMovieMaker;

[TestClass]
public sealed class CompiledTests
{
	public IMovieClip CreateExampleClip()
	{
		var rootTrack = MovieClip.RootGameObject( "Camera" );
		var cameraTrack = rootTrack.Component<CameraComponent>();

		return MovieClip.FromTracks( rootTrack, cameraTrack,
			rootTrack.Property<Vector3>( nameof( GameObject.LocalPosition ) )
				.WithConstant( (0f, 2f), new Vector3( 100f, 200f, 300f ) ),
			cameraTrack.Property<float>( nameof( CameraComponent.FieldOfView ) )
				.WithSamples( (1f, 3f), sampleRate: 2, [60f, 75f, 65f, 90f, 50f] ) );
	}

	public IMovieClip RoundTripSerialize( IMovieClip clip )
	{
		return Json.Deserialize<MovieClip>( Json.Serialize( clip ) );
	}

	[TestMethod]
	public void EmptyIsNotNull()
	{
		Assert.IsNotNull( MovieClip.Empty );
	}

	[TestMethod]
	public void FromTracksReturnsEmpty()
	{
		Assert.AreEqual( MovieClip.Empty, MovieClip.FromTracks( [] ) );
	}

	[TestMethod]
	public void Serialize()
	{
		var clip = CreateExampleClip();
		var json = Json.Serialize( clip );

		Console.WriteLine( json );

		clip = Json.Deserialize<MovieClip>( json );

		Assert.AreEqual( 3d, clip.Duration.TotalSeconds );

		var cameraPosTrack = clip.GetProperty<Vector3>( "Camera", nameof( GameObject.LocalPosition ) );
		var fovTrack = clip.GetProperty<float>( "Camera", nameof( CameraComponent ), nameof( CameraComponent.FieldOfView ) );

		Assert.IsNotNull( cameraPosTrack );

		Assert.IsTrue( cameraPosTrack.TryGetValue( 1.5, out var position ) );
		Assert.IsFalse( cameraPosTrack.TryGetValue( 2.5, out _ ) );

		Assert.AreEqual( new Vector3( 100f, 200f, 300f ), position );

		Assert.IsNotNull( fovTrack );

		Assert.IsTrue( fovTrack.TryGetValue( 1.25, out var fov ) );
		Assert.IsFalse( fovTrack.TryGetValue( 0.5, out _ ) );

		Assert.AreEqual( (60f + 75f) / 2f, fov );
	}

	private static CompiledSampleBlock<T> LoadExampleSampleBlock<T>( string name )
	{
		var asmDir = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location )!;
		var path = Path.Combine( asmDir, "MovieMaker", "TestData", $"{name}.json" );

		return Json.Deserialize<CompiledSampleBlock<T>>( File.ReadAllText( path ) );
	}

	[TestMethod]
	public void SerializeCompressedTransformSamples()
	{
		var uncompressed = LoadExampleSampleBlock<Transform>( "RawTransform" );
		var json = Json.Serialize( uncompressed );

		Console.WriteLine( json );

		var compressed = Json.Deserialize<CompiledSampleBlock<Transform>>( json );

		Assert.IsTrue( uncompressed.Samples.Length > 1_000 );
		Assert.AreEqual( uncompressed.Samples.Length, compressed.Samples.Length );

		for ( var i = 0; i < compressed.Samples.Length; i++ )
		{
			var a = uncompressed.Samples[i];
			var b = compressed.Samples[i];

			Assert.IsTrue( a.Position.AlmostEqual( b.Position, 0.1f ) );
			Assert.IsTrue( AlmostEqual( a.Rotation, b.Rotation ) );
			Assert.IsTrue( a.Scale.AlmostEqual( b.Scale, 0.01f ) );
		}
	}

	[TestMethod]
	public void SerializeCompressedRotationSamples()
	{
		var uncompressed = LoadExampleSampleBlock<Rotation>( "RawRotation" );
		var json = Json.Serialize( uncompressed );

		Console.WriteLine( json );

		var compressed = Json.Deserialize<CompiledSampleBlock<Rotation>>( json );

		Assert.IsTrue( uncompressed.Samples.Length > 1_000 );
		Assert.AreEqual( uncompressed.Samples.Length, compressed.Samples.Length );

		for ( var i = 0; i < compressed.Samples.Length; i++ )
		{
			var a = uncompressed.Samples[i];
			var b = compressed.Samples[i];

			Assert.IsTrue( AlmostEqual( a, b ) );
		}
	}

	private static Rotation Negation( Rotation rotation )
	{
		return new Rotation( -rotation.x, -rotation.y, -rotation.z, -rotation.w );
	}

	private static bool AlmostEqual( Rotation a, Rotation b )
	{
		// A quaternion with all components negated represents the same rotation

		return a.AlmostEqual( b, 0.01f ) || Negation( a ).AlmostEqual( b, 0.01f );
	}

	[TestMethod]
	public void SerializeStringTrackConstant()
	{
		var srcTrack = MovieClip.RootGameObject( "Object" )
			.Property<string>( nameof( GameObject.Name ) )
			.WithConstant( (0f, 1f), "Terry" );

		var clip = MovieClip.FromTracks( srcTrack );
		var json = Json.Serialize( clip );

		Console.WriteLine( json );

		clip = Json.Deserialize<MovieClip>( json );

		var dstTrack = clip.GetProperty<string>( "Object", nameof( GameObject.Name ) );

		Assert.IsNotNull( dstTrack );
		Assert.IsTrue( dstTrack.TryGetValue( 0.5f, out var name ) );
		Assert.AreEqual( "Terry", name );
	}

	[TestMethod]
	public void SerializeStringTrackSamples()
	{
		var srcTrack = MovieClip.RootGameObject( "Object" )
			.Property<string>( nameof( GameObject.Name ) )
			.WithSamples( (0f, 1f), 3, ["Larry", "Terry", "Jerry"] );

		var clip = MovieClip.FromTracks( srcTrack );
		var json = Json.Serialize( clip );

		Console.WriteLine( json );

		clip = Json.Deserialize<MovieClip>( json );

		var dstTrack = clip.GetProperty<string>( "Object", nameof( GameObject.Name ) );

		Assert.IsNotNull( dstTrack );
		Assert.IsTrue( dstTrack.TryGetValue( 0.5f, out var name ) );
		Assert.AreEqual( "Terry", name );
	}

	[TestMethod]
	public void SerializeTextScopeTrack()
	{
		var srcTrack = MovieClip.RootGameObject( "Object" )
			.Component<TextRenderer>()
			.Property<TextRendering.Scope>( nameof( TextRenderer.TextScope ) )
			.Property<string>( nameof( TextRendering.Scope.Text ) )
			.WithConstant( (0f, 1f), "Terry" );

		var clip = MovieClip.FromTracks( srcTrack );
		var json = Json.Serialize( clip );

		Console.WriteLine( json );

		clip = Json.Deserialize<MovieClip>( json );

		var dstTrack = clip.GetProperty<string>(
			"Object",
			nameof( TextRenderer ),
			nameof( TextRenderer.TextScope ),
			nameof( TextRendering.Scope.Text ) );

		Assert.IsNotNull( dstTrack );
		Assert.IsTrue( dstTrack.TryGetValue( 0.5f, out var name ) );
		Assert.AreEqual( "Terry", name );
	}

	[TestMethod]
	public void SerializeReferenceProperty()
	{
		var referencedTrack = MovieClip.RootGameObject( "Foo" );
		var referencingTrack = MovieClip.RootGameObject( "Bar" )
			.Component<VerletRope>()
			.ReferenceProperty<GameObject>( nameof( VerletRope.Attachment ) )
			.WithConstant( (0f, 1f), referencedTrack.Id );

		var clip = MovieClip.FromTracks( referencedTrack, referencingTrack );
		var json = Json.Serialize( clip );

		Console.WriteLine( json );

		clip = Json.Deserialize<MovieClip>( json );

		var fooTrack = clip.GetReference<GameObject>( "Foo" );
		var attachmentTrack = clip.GetProperty<BindingReference<GameObject>>( "Bar", nameof( VerletRope ), nameof( VerletRope.Attachment ) );

		Assert.IsNotNull( fooTrack );
		Assert.IsNotNull( attachmentTrack );

		Assert.IsTrue( attachmentTrack.TryGetValue( 0.5f, out var value ) );
		Assert.AreEqual( fooTrack.Id, value );
	}

	[TestMethod]
	public void ValidateBlocks()
	{
		var track = MovieClip.RootGameObject( "Example" )
			.Property<Vector3>( "LocalPosition" )
			.WithConstant( (0d, 2d), default );

		Assert.ThrowsException<ArgumentException>( () => track.WithConstant( (1d, 3d), default ),
			"Overlapping blocks" );
	}

	/// <summary>
	/// Re-use the same <see cref="CompiledSampleBlock{T}"/> instance if no reduction needed.
	/// </summary>
	[TestMethod]
	public void ReduceSamplesSameInstance()
	{
		var sampleBlock = new CompiledSampleBlock<int>( (0.0, 2.0), 0.0, 1, [0, 1, 2] );

		Assert.AreSame( sampleBlock, sampleBlock.Reduce() );
	}

	/// <summary>
	/// Reduce a <see cref="CompiledSampleBlock{T}"/> in a way that's aligned to the sample rate.
	/// </summary>
	[TestMethod]
	public void ReduceSamplesAligned()
	{
		var sampleBlock = new CompiledSampleBlock<int>( (0.0, 2.0), 1.0, 1, [0, 1, 2, 3, 4] );

		Assert.IsInstanceOfType( sampleBlock.Reduce(), out CompiledSampleBlock<int> reduced );

		Assert.AreEqual( (0.0, 2.0), reduced.TimeRange );
		Assert.AreEqual( 0.0, reduced.Offset );

		Assert.IsTrue( reduced.Samples.SequenceEqual( [1, 2, 3] ) );
	}

	/// <summary>
	/// Reduce a <see cref="CompiledSampleBlock{T}"/> in a way that's not aligned to the sample rate.
	/// </summary>
	[TestMethod]
	public void ReduceSamplesUnaligned()
	{
		var sampleBlock = new CompiledSampleBlock<int>( (0.0, 2.0), 1.5, 1, [0, 1, 2, 3, 4] );

		Assert.IsInstanceOfType( sampleBlock.Reduce(), out CompiledSampleBlock<int> reduced );

		Assert.AreEqual( (0.0, 2.0), reduced.TimeRange );
		Assert.AreEqual( 0.5, reduced.Offset );

		Assert.IsTrue( reduced.Samples.SequenceEqual( [1, 2, 3, 4] ) );
	}

	/// <summary>
	/// Turn a <see cref="CompiledSampleBlock{T}"/> into a <see cref="CompiledConstantBlock{T}"/> if we reduce down
	/// to a single sample.
	/// </summary>
	[TestMethod]
	public void ReduceSamplesIntoConstant()
	{
		var sampleBlock = new CompiledSampleBlock<int>( (0.0, 1.0), -1.0, 1, [1, 2] );

		Assert.IsInstanceOfType( sampleBlock.Reduce(), out CompiledConstantBlock<int> reduced );

		Assert.AreEqual( (0.0, 1.0), reduced.TimeRange );
		Assert.AreEqual( 1, reduced.GetValue( 0.0 ) );
	}
}
