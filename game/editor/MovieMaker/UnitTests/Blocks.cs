namespace Editor.MovieMaker.Test;

[TestClass]
public class Blocks
{
	/// <summary>
	/// Two adjacent blocks with keyframes should get merged into one.
	/// </summary>
	[TestMethod]
	public void MergeKeyframes()
	{
		var blocks = new List<PropertyBlock<int>>
		{
			new( PropertySignal.FromKeyframes( [
				new Keyframe<int>( 0.0, 100, KeyframeInterpolation.Linear ),
				new Keyframe<int>( 1.0, 200, KeyframeInterpolation.Linear )
			] ), (0.0, 1.0) ),
			new( PropertySignal.FromKeyframes( [
				new Keyframe<int>( 1.0, 300, KeyframeInterpolation.Linear ),
				new Keyframe<int>( 2.0, 400, KeyframeInterpolation.Linear )
			] ), (1.0, 2.0) )
		};

		blocks.Merge();

		Assert.AreEqual( 1, blocks.Count );
		Assert.IsInstanceOfType<IKeyframeSignal>( blocks[0].Signal );

		var keyframes = blocks[0].Signal.Keyframes;

		Assert.AreEqual( 4, keyframes.Count );

		Assert.AreEqual( 100, keyframes[0].Value );
		Assert.AreEqual( 200, keyframes[1].Value );
		Assert.AreEqual( 300, keyframes[2].Value );
		Assert.AreEqual( 400, keyframes[3].Value );

		Assert.AreEqual( 0.0, keyframes[0].Time );
		Assert.AreEqual( 1.0, keyframes[1].Time );
		Assert.AreEqual( 1.0, keyframes[2].Time );
		Assert.AreEqual( 2.0, keyframes[3].Time );

		Assert.AreEqual( KeyframeConnection.EndBlock, keyframes[1].Connection );
		Assert.AreEqual( KeyframeConnection.StartBlock, keyframes[2].Connection );
	}
}
