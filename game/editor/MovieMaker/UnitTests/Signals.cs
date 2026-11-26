using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker.Test;

[TestClass]
public class Signals
{
	/// <summary>
	/// Calling Reduce() on a transformed signal would throw a stack overflow.
	/// </summary>
	[TestMethod]
	public void ReduceTransformedSignal()
	{
		var signal = PropertySignal.FromSamples( 0d, 1, [0f, 0f, 1f, 1f, 2f, 1f, 0f, 1f, 1f, 0f] );

		var transformed = new MovieTransform( 1d ) * signal;

		Assert.AreNotSame( transformed, signal );

		var reduced = transformed.Reduce( 0, 10 );

		Assert.AreSame( transformed, reduced );
	}

	private static PropertySignal<int> GenerateKeyframeSignal( KeyframeInterpolation interpolation )
	{
		return PropertySignal.FromKeyframes( [
			new Keyframe<int>( 0.0, 100, interpolation ),
			new Keyframe<int>( 1.0, 200, interpolation ),
			new Keyframe<int>( 2.0, 300, interpolation ),
			new Keyframe<int>( 3.0, 400, interpolation )
		] );
	}

	/// <summary>
	/// We can snip out keyframes when reducing to a time range.
	/// If the range exactly ends on a keyframe, that should be the last one (unless it's cubic).
	/// </summary>
	[TestMethod]
	public void ReduceKeyframesSimpleExact()
	{
		var signal = GenerateKeyframeSignal( KeyframeInterpolation.Linear );
		var reduced = signal.Reduce( 0.0, 1.0 );

		Assert.IsInstanceOfType<IKeyframeSignal>( reduced );
		Assert.AreEqual( 2, reduced.Keyframes.Count );
		Assert.AreEqual( 0.0, reduced.Keyframes[0].Time );
		Assert.AreEqual( 1.0, reduced.Keyframes[1].Time );
	}

	/// <summary>
	/// We can snip out keyframes when reducing to a time range.
	/// If the range ends between keyframes, we need to keep the next one.
	/// </summary>
	[TestMethod]
	public void ReduceKeyframesSimpleBetween()
	{
		var signal = GenerateKeyframeSignal( KeyframeInterpolation.Linear );
		var reduced = signal.Reduce( 0.0, 1.5 );

		Assert.IsInstanceOfType<IKeyframeSignal>( reduced );
		Assert.AreEqual( 3, reduced.Keyframes.Count );
		Assert.AreEqual( 0.0, reduced.Keyframes[0].Time );
		Assert.AreEqual( 1.0, reduced.Keyframes[1].Time );
		Assert.AreEqual( 2.0, reduced.Keyframes[2].Time );
	}

	/// <summary>
	/// We have to keep one keyframe outside the reduced time range when using cubic interpolation,
	/// if the range ends exactly on a keyframe.
	/// </summary>
	[TestMethod]
	public void ReduceKeyframesCubicExact()
	{
		var signal = GenerateKeyframeSignal( KeyframeInterpolation.Cubic );
		var reduced = signal.Reduce( 0.0, 1.0 );

		Assert.IsInstanceOfType<IKeyframeSignal>( reduced );
		Assert.AreEqual( 3, reduced.Keyframes.Count );
		Assert.AreEqual( 0.0, reduced.Keyframes[0].Time );
		Assert.AreEqual( 1.0, reduced.Keyframes[1].Time );
		Assert.AreEqual( 2.0, reduced.Keyframes[2].Time );
	}

	/// <summary>
	/// We have to keep two keyframes outside the reduced time range when using cubic interpolation,
	/// if the range ends between two keyframes.
	/// </summary>
	[TestMethod]
	public void ReduceKeyframesCubicBetween()
	{
		var signal = GenerateKeyframeSignal( KeyframeInterpolation.Cubic );
		var reduced = signal.Reduce( 0.0, 1.5 );

		// No reduction possible.

		Assert.AreSame( signal, reduced );
	}

	/// <summary>
	/// If two keyframes overlap, we want the latter's value when querying exactly at their time.
	/// </summary>
	[TestMethod]
	public void SplitKeyframeGetValue()
	{
		var signal = PropertySignal.FromKeyframes( [
			new Keyframe<float>( 0.0, 0f, KeyframeInterpolation.Linear ),
			new Keyframe<float>( 1.0, 1f, KeyframeInterpolation.Linear, KeyframeConnection.EndBlock ),
			new Keyframe<float>( 1.0, 2f, KeyframeInterpolation.Linear, KeyframeConnection.StartBlock ),
			new Keyframe<float>( 2.0, 3f, KeyframeInterpolation.Linear )
		] );

		Assert.AreEqual( 2f, signal.GetValue( 1.0 ) );
	}
}
