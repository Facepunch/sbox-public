using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker.Test;

[TestClass]
public class Compilation
{
	/// <summary>
	/// Test constant signal compilation.
	/// </summary>
	[TestMethod]
	public void Constant()
	{
		var block = new PropertyBlock<float>( 1f, (0, 10) );

		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property( "Property", block.Compile() );

		Assert.AreEqual( 1, exampleTrack.Blocks.Length );
		Assert.IsInstanceOfType<CompiledConstantBlock<float>>( exampleTrack.Blocks[0] );

		// Compiled block must match start / end time of source block

		Assert.AreEqual( 0d, exampleTrack.Blocks[0].TimeRange.Start );
		Assert.AreEqual( 10d, exampleTrack.Blocks[0].TimeRange.End );
	}

	/// <summary>
	/// Test constant signal compilation, when not aligned to sample rate.
	/// </summary>
	[TestMethod]
	public void ConstantMisaligned()
	{
		var block = new PropertyBlock<float>( 1f, (0.01, 9.99) );

		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property( "Property", block.Compile() );

		Assert.AreEqual( 1, exampleTrack.Blocks.Length );
		Assert.IsInstanceOfType<CompiledConstantBlock<float>>( exampleTrack.Blocks[0] );

		// Compiled block must match start / end time of source block

		Assert.AreEqual( 0.01, exampleTrack.Blocks[0].TimeRange.Start );
		Assert.AreEqual( 9.99, exampleTrack.Blocks[0].TimeRange.End );
	}

	private static PropertySignal<float> CreateKeyframeSignal() => PropertySignal.FromKeyframes( [
		new Keyframe<float>( 0, 0f, KeyframeInterpolation.Linear, KeyframeConnection.Connect ),
		new Keyframe<float>( 5, 0f, KeyframeInterpolation.Linear, KeyframeConnection.Connect ),
		new Keyframe<float>( 10, 1f, KeyframeInterpolation.Cubic, KeyframeConnection.Connect )
	] );

	/// <summary>
	/// Test keyframe signal compilation, where the signal can be represented as a constant block
	/// followed by a sample block.
	/// </summary>
	[TestMethod]
	public void Keyframes()
	{
		var block = new PropertyBlock<float>( CreateKeyframeSignal(), (0, 10) );

		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property( "Property", block.Compile() );

		// Signal is constant, followed by a changing value

		Assert.AreEqual( 2, exampleTrack.Blocks.Length );
		Assert.IsInstanceOfType<CompiledConstantBlock<float>>( exampleTrack.Blocks[0] );
		Assert.IsInstanceOfType<CompiledSampleBlock<float>>( exampleTrack.Blocks[1] );

		// Compiled blocks must be adjacent, and match start / end time of source block

		Assert.AreEqual( 0, exampleTrack.Blocks[0].TimeRange.Start );
		Assert.AreEqual( exampleTrack.Blocks[0].TimeRange.End, exampleTrack.Blocks[1].TimeRange.Start );
		Assert.AreEqual( 10, exampleTrack.Blocks[1].TimeRange.End );
	}

	/// <summary>
	/// Test keyframe signal compilation, when not aligned to sample rate.
	/// </summary>
	[TestMethod]
	public void KeyframesMisaligned()
	{
		var block = new PropertyBlock<float>( CreateKeyframeSignal(), (0.01, 9.99) );

		var exampleTrack = MovieClip.RootGameObject( "Example" )
			.Property( "Property", block.Compile() );

		// Signal is constant, followed by a changing value

		Assert.AreEqual( 2, exampleTrack.Blocks.Length );
		Assert.IsInstanceOfType<CompiledConstantBlock<float>>( exampleTrack.Blocks[0] );
		Assert.IsInstanceOfType<CompiledSampleBlock<float>>( exampleTrack.Blocks[1] );

		// Compiled blocks must be adjacent, and match start / end time of source block

		Assert.AreEqual( 0.01, exampleTrack.Blocks[0].TimeRange.Start );
		Assert.AreEqual( exampleTrack.Blocks[0].TimeRange.End, exampleTrack.Blocks[1].TimeRange.Start );
		Assert.AreEqual( 9.99, exampleTrack.Blocks[1].TimeRange.End );
	}

	/// <summary>
	/// Make sure the last keyframe is included in the compiled block!
	/// </summary>
	[TestMethod]
	public void IncludeLastKeyframe()
	{
		var source = new PropertyBlock<float>( PropertySignal.FromKeyframes( [
			new Keyframe<float>( 0.0, 0f, KeyframeInterpolation.Step ),
			new Keyframe<float>( 1.0, 100f, KeyframeInterpolation.Step )
		] ), (0.0, 1.0) );

		var compiled = source.Compile().ToArray();

		Assert.AreEqual( 2, compiled.Length );
		Assert.IsInstanceOfType<CompiledConstantBlock<float>>( compiled[0], out var block0 );
		Assert.IsInstanceOfType<CompiledConstantBlock<float>>( compiled[1], out var block1 );

		Assert.AreEqual( 0f, block0.GetValue( 0.0 ) );
		Assert.AreEqual( 100f, block1.GetValue( 1.0 ) );
	}
}
