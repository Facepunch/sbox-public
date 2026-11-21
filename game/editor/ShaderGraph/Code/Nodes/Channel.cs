
namespace Editor.ShaderGraph.Nodes;

public enum SwizzleChannel
{
	Red = 0,
	Green = 1,
	Blue = 2,
	Alpha = 3,
}

/// <summary>
/// Split value into individual components
/// </summary>
[Title( "Split" ), Category( "Channel" ), Icon( "call_split" )]
public sealed class SplitVector : ShaderNode
{
	[Input, Hide]
	public NodeInput Input { get; set; }

	[Output( typeof( float ) ), Hide]
	public NodeResult.Func X => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Input );
		if ( result.IsValid && result.Components > 0 ) return new NodeResult( NodeResultType.Float, $"{result}.x" );
		return new NodeResult( NodeResultType.Float, "0.0f" );
	};

	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Y => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Input );
		if ( result.IsValid && result.Components > 1 ) return new NodeResult( NodeResultType.Float, $"{result}.y" );
		return new NodeResult( NodeResultType.Float, "0.0f" );
	};

	[Output( typeof( float ) ), Hide]
	public NodeResult.Func Z => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Input );
		if ( result.IsValid && result.Components > 2 ) return new NodeResult( NodeResultType.Float, $"{result}.z" );
		return new NodeResult( NodeResultType.Float, "0.0f" );
	};

	[Output( typeof( float ) ), Hide]
	public NodeResult.Func W => ( GraphCompiler compiler ) =>
	{
		var result = compiler.Result( Input );
		if ( result.IsValid && result.Components > 3 ) return new NodeResult( NodeResultType.Float, $"{result}.w" );
		return new NodeResult( NodeResultType.Float, "0.0f" );
	};
}

/// <summary>
/// Combine input values into 3 separate vectors
/// </summary>
[Title( "Combine" ), Category( "Channel" ), Icon( "call_merge" )]
public sealed class CombineVector : ShaderNode
{
	[Input( typeof( float ) )]
	[Hide]
	public NodeInput X { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Y { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Z { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput W { get; set; }

	[InputDefault( nameof( X ) )]
	public float DefaultX { get; set; } = 0.0f;
	[InputDefault( nameof( Y ) )]
	public float DefaultY { get; set; } = 0.0f;
	[InputDefault( nameof( Z ) )]
	public float DefaultZ { get; set; } = 0.0f;
	[InputDefault( nameof( W ) )]
	public float DefaultW { get; set; } = 0.0f;

	[Output( typeof( Vector4 ) )]
	[Alias( "Vector" )]
	[Hide]
	public NodeResult.Func XYZW => ( GraphCompiler compiler ) =>
	{
		var x = compiler.ResultOrDefault( X, DefaultX ).Cast( 1 );
		var y = compiler.ResultOrDefault( Y, DefaultY ).Cast( 1 );
		var z = compiler.ResultOrDefault( Z, DefaultZ ).Cast( 1 );
		var w = compiler.ResultOrDefault( W, DefaultW ).Cast( 1 );

		return new NodeResult( NodeResultType.Vector4, $"float4( {x}, {y}, {z}, {w} )" );
	};

	[Output( typeof( Vector3 ) )]
	[Hide]
	public NodeResult.Func XYZ => ( GraphCompiler compiler ) =>
	{
		var x = compiler.ResultOrDefault( X, DefaultX ).Cast( 1 );
		var y = compiler.ResultOrDefault( Y, DefaultY ).Cast( 1 );
		var z = compiler.ResultOrDefault( Z, DefaultZ ).Cast( 1 );

		return new NodeResult( NodeResultType.Vector3, $"float3( {x}, {y}, {z} )" );
	};

	[Output( typeof( Vector2 ) )]
	[Hide]
	public NodeResult.Func XY => ( GraphCompiler compiler ) =>
	{
		var x = compiler.ResultOrDefault( X, DefaultX ).Cast( 1 );
		var y = compiler.ResultOrDefault( Y, DefaultY ).Cast( 1 );

		return new NodeResult( NodeResultType.Vector2, $"float2( {x}, {y})" );
	};
}

/// <summary>
/// Swap components of a color around
/// </summary>
[Title( "Swizzle" ), Category( "Channel" ), Icon( "swap_horiz" )]
public sealed class SwizzleVector : ShaderNode
{
	[Input, Hide]
	public NodeInput Input { get; set; }

	public SwizzleChannel RedOut { get; set; } = SwizzleChannel.Red;
	public SwizzleChannel GreenOut { get; set; } = SwizzleChannel.Green;
	public SwizzleChannel BlueOut { get; set; } = SwizzleChannel.Blue;
	public SwizzleChannel AlphaOut { get; set; } = SwizzleChannel.Alpha;

	private static char SwizzleToChannel( SwizzleChannel channel )
	{
		return channel switch
		{
			SwizzleChannel.Green => 'y',
			SwizzleChannel.Blue => 'z',
			SwizzleChannel.Alpha => 'w',
			_ => 'x',
		};
	}

	[Output( typeof( Vector4 ) ), Hide]
	public NodeResult.Func Output => ( GraphCompiler compiler ) =>
	{
		var input = compiler.Result( Input );
		if ( !input.IsValid )
			return default;

		var swizzle = $".";
		swizzle += SwizzleToChannel( RedOut );
		swizzle += SwizzleToChannel( GreenOut );
		swizzle += SwizzleToChannel( BlueOut );
		swizzle += SwizzleToChannel( AlphaOut );

		return new NodeResult( NodeResultType.Vector4, $"{input.Cast( 4 )}{swizzle}" );
	};
}

/// <summary>
/// Append constants to change number of channels
/// </summary>
[Title( "Append" ), Category( "Channel" )]
public sealed class AppendVector : ShaderNode
{
	[Input, Hide]
	public NodeInput A { get; set; }

	[Input, Hide]
	public NodeInput B { get; set; }

	[Output, Hide]
	public NodeResult.Func Output => ( GraphCompiler compiler ) =>
	{
		var resultA = compiler.ResultOrDefault( A, 0.0f );
		var resultB = compiler.ResultOrDefault( B, 0.0f );

		var components = resultB.Components + resultA.Components;
		if ( components < 1 || components > 4 )
			return NodeResult.Error( $"Can't append {resultB.TypeName} to {resultA.TypeName}" );

		NodeResultType resultType = components switch
		{
			1 => NodeResultType.Float,
			2 => NodeResultType.Vector2,
			3 => NodeResultType.Vector3,
			4 => NodeResultType.Vector4,
			_ => NodeResultType.Invalid,
		};

		return new NodeResult( resultType, $"float{components}( {resultA}, {resultB} )" );
	};
}
