using Sandbox.Animation;
using System;

namespace ResourceTests;

[TestClass]
public class GraphBuilderValidationTests
{
	[TestMethod]
	public void BlendSpaceRejectsRangeOutsideSources()
	{
		var builder = Graph.Builder;
		var speed = builder.FloatParameter( "speed" );
		var sources = new[] { builder.Clip( "a.vanim" ), builder.Clip( "b.vanim" ) };

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			builder.BlendSpace( speed, sources, [(0, 5, 0f, 1f)], true ) );
	}

	[TestMethod]
	public void BlendSpace2DRejectsIndicesOutsideSources()
	{
		var builder = Graph.Builder;
		var x = builder.FloatParameter( "x" );
		var y = builder.FloatParameter( "y" );
		var sources = new[] { builder.Clip( "a.vanim" ), builder.Clip( "b.vanim" ), builder.Clip( "c.vanim" ) };
		var positions = new[] { new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ) };

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			builder.BlendSpace2D( x, y, new GraphBuilder.BlendSpace2DSettings { Sources = sources, Positions = positions, Indices = [0, 1, 7], HullIndices = [0, 1, 2] } ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			builder.BlendSpace2D( x, y, new GraphBuilder.BlendSpace2DSettings { Sources = sources, Positions = positions, Indices = [0, 1, 2], HullIndices = [-1] } ) );
		Assert.ThrowsException<ArgumentException>( () =>
			builder.BlendSpace2D( x, y, new GraphBuilder.BlendSpace2DSettings { Sources = sources, Positions = positions[..2], Indices = [0, 1, 2] } ) );
	}

	[TestMethod]
	public void ParameterizedSelectorRequiresWeightPerOption()
	{
		var builder = Graph.Builder;
		var seed = builder.FloatParameter( "seed" );
		var options = new[] { builder.Clip( "a.vanim" ), builder.Clip( "b.vanim" ) };

		Assert.ThrowsException<ArgumentException>( () =>
			builder.ParameterizedSelector( seed, options, new GraphBuilder.ParameterizedSelectorSettings { HasWeights = true, Weights = [1] } ) );
	}
}
