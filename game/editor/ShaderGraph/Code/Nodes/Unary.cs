
namespace Editor.ShaderGraph.Nodes;

public enum ExponentBase
{
	BaseE,
	Base2,
}

public enum LogBase
{
	BaseE,
	Base2,
	Base10,
}

public enum DerivativePrecision
{
	Standard,
	Course,
	Fine
}

public abstract class Unary : ShaderNode
{
	[Input]
	[Hide]
	public virtual NodeInput Input { get; set; }

	protected virtual string Op { get; }

	[Hide]
	protected virtual int? Components { get; } = null;

	public Unary() : base()
	{
		ExpandSize = new Vector2( -50, 0 );
	}

	[Output]
	[Hide]
	public virtual NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var result = compiler.ResultOrDefault( Input, 0.0f );

		NodeResultType resultType = result.ResultType;

		if ( Components is not null )
		{
			switch ( Components )
			{
				case 1: resultType = NodeResultType.Float; break;
				case 2: resultType = NodeResultType.Vector2; break;
				case 3: resultType = NodeResultType.Vector3; break;
				case 4: resultType = NodeResultType.Color; break;
			}
		}

		return result.IsValid ? new NodeResult( resultType, $"{Op}( {result} )" ) : default;
	};
}

public abstract class UnaryCurve : Unary
{
	[Input, Title( "" ), Hide]
	public override NodeInput Input { get => base.Input; set => base.Input = value; }

	[Output, Title( "" ), Hide]
	public override NodeResult.Func Result => base.Result;

	protected virtual float Evaluate( float x ) => 0.0f;

	public UnaryCurve() : base()
	{
		ExpandSize = new Vector2( -12 * 6, 12 );
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Shrink( 20, 28, 20, 6 );
		Paint.SetBrush( Theme.TextControl );
		Paint.SetPen( Theme.TextControl, 2 );
		var inc = 1f / 16f;
		List<Vector2> points = new List<Vector2>();
		for ( var i = 0f; i < 1f; i += inc )
		{
			var x = rect.BottomLeft.x + rect.Width * i;
			var y = rect.BottomLeft.y - rect.Height * Evaluate( i );
			points.Add( new Vector2( x, y ) );
		}
		for ( int i = points.Count - 1; i >= 0; i-- )
		{
			points.Add( points[i] );
		}
		Paint.DrawPolygon( points.ToArray() );
	}
}

[Title( "Cosine" ), Category( "Unary" )]
public sealed class Cosine : UnaryCurve
{
	protected override float Evaluate( float x )
	{
		return MathF.Cos( x * MathF.PI * 2 ) / 2 + 0.5f;
	}

	[Hide]
	protected override string Op => "cos";
}

[Title( "Abs" ), Category( "Unary" )]
public sealed class Abs : Unary
{
	[Input, Title( "" ), Hide]
	public override NodeInput Input { get => base.Input; set => base.Input = value; }

	[Output, Title( "" ), Hide]
	public override NodeResult.Func Result => base.Result;

	public Abs() : base()
	{
		ExpandSize = new Vector2( -12 * 8, 12 );
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Shrink( 0, 20, 0, 0 );
		Paint.SetPen( Theme.TextControl );
		Paint.SetFont( "Poppins Bold", 12 );
		Paint.DrawText( rect, "| x |" );
	}

	[Hide]
	protected override string Op => "abs";
}

[Title( "Dot Product" ), Category( "Unary" )]
public sealed class DotProduct : ShaderNode
{
	[Input]
	[Hide]
	public NodeInput InputA { get; set; }

	[Input]
	[Hide]
	public NodeInput InputB { get; set; }

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var resultA = compiler.ResultOrDefault( InputA, 0.0f );
		var resultB = compiler.ResultOrDefault( InputB, 0.0f ).Cast( resultA.Components );

		return new NodeResult( NodeResultType.Float, $"dot( {resultA}, {resultB} )" );
	};
}

[Title( "DDX" ), Category( "Unary" )]
public sealed class DDX : Unary
{
	public DerivativePrecision Precision { get; set; }

	[Hide]
	protected override string Op
	{
		get
		{
			return Precision switch
			{
				DerivativePrecision.Course => "ddx_course",
				DerivativePrecision.Fine => "ddx_fine",
				_ => "ddx",
			};
		}
	}
}

[Title( "DDY" ), Category( "Unary" )]
public sealed class DDY : Unary
{
	public DerivativePrecision Precision { get; set; }

	[Hide]
	protected override string Op
	{
		get
		{
			return Precision switch
			{
				DerivativePrecision.Course => "ddy_course",
				DerivativePrecision.Fine => "ddy_fine",
				_ => "ddy",
			};
		}
	}
}

[Title( "DDXY" ), Category( "Unary" )]
public sealed class DDXY : Unary
{
	[Hide]
	protected override string Op => "fwidth";
}

[Title( "Exponential" ), Category( "Unary" )]
public sealed class Exponential : Unary
{
	public ExponentBase Base { get; set; }

	[Input, Title( "" ), Hide]
	public override NodeInput Input { get => base.Input; set => base.Input = value; }

	[Output, Title( "" ), Hide]
	public override NodeResult.Func Result => base.Result;

	public Exponential() : base()
	{
		ExpandSize = new Vector2( -12 * 6, 12 );
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Shrink( 0, 20, 0, 0 );
		Paint.SetPen( Theme.TextControl );
		Paint.SetFont( "Poppins Bold", 16 );
		Paint.DrawText( rect, (Base == ExponentBase.BaseE) ? "e" : "2" );
		Paint.SetFont( "Poppins Bold", 8 );
		Paint.DrawText( rect.Shrink( 20, 0, 0, 16 ), "x" );
	}

	[Hide]
	protected override string Op => Base == ExponentBase.BaseE ? "exp" : "exp2";
}

[Title( "Frac" ), Category( "Unary" )]
public sealed class Frac : Unary
{
	[Hide]
	protected override string Op => "frac";
}

[Title( "Floor" ), Category( "Unary" )]
public sealed class Floor : Unary
{
	[Hide]
	protected override string Op => "floor";
}

[Title( "Length" ), Category( "Unary" )]
public sealed class Length : Unary
{
	[Hide]
	protected override int? Components => 1;

	[Hide]
	protected override string Op => "length";
}

[Title( "Log" ), Category( "Unary" )]
public sealed class BaseLog : Unary
{
	public LogBase Base { get; set; }

	[Hide]
	protected override string Op
	{
		get
		{
			return Base switch
			{
				LogBase.Base2 => "log2",
				LogBase.Base10 => "log10",
				_ => "log",
			};
		}
	}
}

[Title( "Min" ), Category( "Unary" )]
public sealed class Min : ShaderNode
{
	[Input( typeof( float ) )]
	[Hide]
	public NodeInput InputA { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput InputB { get; set; }

	[InputDefault( nameof( InputA ) )]
	public float DefaultA { get; set; } = 0.0f;
	[InputDefault( nameof( InputB ) )]
	public float DefaultB { get; set; } = 0.0f;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var a = compiler.ResultOrDefault( InputA, DefaultA );
		var b = compiler.ResultOrDefault( InputB, DefaultB );

		int maxComponents = Math.Max( a.IsValid ? a.Components : 1, b.IsValid ? b.Components : 1 );

		NodeResultType resultType = maxComponents switch
		{
			1 => NodeResultType.Float,
			2 => NodeResultType.Vector2,
			3 => NodeResultType.Vector3,
			4 => NodeResultType.Color,
			_ => throw new NotImplementedException(),
		};

		return new NodeResult( resultType, $"min( {(a.IsValid ? a : "0.0f")}, {(b.IsValid ? b : "0.0f")} )" );
	};
}

[Title( "Max" ), Category( "Unary" )]
public sealed class Max : ShaderNode
{
	[Input( typeof( float ) )]
	[Hide]
	public NodeInput InputA { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput InputB { get; set; }

	[InputDefault( nameof( InputA ) )]
	public float DefaultA { get; set; } = 0.0f;
	[InputDefault( nameof( InputB ) )]
	public float DefaultB { get; set; } = 0.0f;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var a = compiler.ResultOrDefault( InputA, DefaultA );
		var b = compiler.ResultOrDefault( InputB, DefaultB );

		int maxComponents = Math.Max( a.IsValid ? a.Components : 1, b.IsValid ? b.Components : 1 );

		NodeResultType resultType = maxComponents switch
		{
			1 => NodeResultType.Float,
			2 => NodeResultType.Vector2,
			3 => NodeResultType.Vector3,
			4 => NodeResultType.Color,
			_ => throw new NotImplementedException(),
		};

		return new NodeResult( resultType, $"max( {(a.IsValid ? a : "0.0f")}, {(b.IsValid ? b : "0.0f")} )" );
	};
}

/// <summary>
/// Clamps the specified value within the range of 0 to 1
/// </summary>
[Title( "Saturate" ), Category( "Transform" ), Icon( "opacity" )]
public sealed class Saturate : Unary
{
	[Hide]
	protected override string Op => "saturate";
}

[Title( "Sine" ), Category( "Unary" )]
public sealed class Sine : UnaryCurve
{
	protected override float Evaluate( float x )
	{
		return MathF.Sin( x * MathF.PI * 2 ) / 2 + 0.5f;
	}

	[Hide]
	protected override string Op => "sin";
}

[Title( "Step" ), Category( "Unary" )]
public sealed class Step : ShaderNode
{
	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Input { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Edge { get; set; }

	[InputDefault( nameof( Input ) )]
	public float DefaultInput { get; set; } = 0.0f;
	[InputDefault( nameof( Edge ) )]
	public float DefaultEdge { get; set; } = 0.0f;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var edge = compiler.ResultOrDefault( Edge, DefaultEdge );
		var input = compiler.ResultOrDefault( Input, DefaultInput );

		int maxComponents = Math.Max( edge.IsValid ? edge.Components : 1, input.IsValid ? input.Components : 1 );

		NodeResultType resultType = maxComponents switch
		{
			1 => NodeResultType.Float,
			2 => NodeResultType.Vector2,
			3 => NodeResultType.Vector3,
			4 => NodeResultType.Color,
			_ => throw new NotImplementedException(),
		};

		return new NodeResult( resultType, $"step( {(edge.IsValid ? edge : "0.0f")}, {(input.IsValid ? input : "0.0f")} )" );
	};
}

[Title( "Smooth Step" ), Category( "Unary" )]
public sealed class SmoothStep : ShaderNode
{
	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Input { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Edge1 { get; set; }

	[Input( typeof( float ) )]
	[Hide]
	public NodeInput Edge2 { get; set; }

	[InputDefault( nameof( Input ) )]
	public float DefaultInput { get; set; } = 0.0f;

	[InputDefault( nameof( Edge1 ) )]
	public float DefaultEdge1 { get; set; } = 0.0f;

	[InputDefault( nameof( Edge2 ) )]
	public float DefaultEdge2 { get; set; } = 0.0f;

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var edge1 = compiler.Result( Edge1 );
		var edge2 = compiler.Result( Edge2 );
		var input = compiler.Result( Input );

		int maxComponents = Math.Max( edge1.IsValid ? edge1.Components : 1, input.IsValid ? input.Components : 1 );
		maxComponents = Math.Max( edge2.IsValid ? edge2.Components : 1, maxComponents );

		var edge1String = edge1.IsValid ? edge1.ToString() : compiler.ResultValue( DefaultEdge1 ).ToString();
		var edge2String = edge2.IsValid ? edge2.ToString() : compiler.ResultValue( DefaultEdge2 ).ToString();
		var inputString = input.IsValid ? input.ToString() : compiler.ResultValue( DefaultInput ).ToString();

		NodeResultType resultType = maxComponents switch
		{
			1 => NodeResultType.Float,
			2 => NodeResultType.Vector2,
			3 => NodeResultType.Vector3,
			4 => NodeResultType.Color,
			_ => throw new NotImplementedException(),
		};

		return new NodeResult( resultType, $"smoothstep( {edge1String}, {edge2String}, {inputString} )" );
	};
}

/// <summary>
/// Computes the tangent of a specified angle (in radians).
/// </summary>
[Title( "Tangent" ), Category( "Unary" )]
public sealed class Tan : Unary
{
	[Hide]
	protected override string Op => "tan";
}

/// <summary>
/// Computes the angle (in radians) whose sine is the specified number.
/// </summary>
[Title( "Arcsin" ), Category( "Unary" )]
public sealed class Arcsin : Unary
{
	[Hide]
	protected override string Op => "asin";
}

/// <summary>
/// Computes the angle (in radians) whose cosine is the specified number.
/// </summary>
[Title( "Arccos" ), Category( "Unary" )]
public sealed class Arccos : Unary
{
	[Hide]
	protected override string Op => "acos";
}

/// <summary>
/// Round to the nearest integer.
/// </summary>
[Title( "Round" ), Category( "Unary" )]
public sealed class Round : Unary
{
	[Hide]
	protected override string Op => "round";
}

/// <summary>
/// Returns the smallest integer value that is greater than or equal to the specified value.
/// </summary>
[Title( "Ceil" ), Category( "Unary" )]
public sealed class Ceil : Unary
{
	[Hide]
	protected override string Op => "ceil";
}

[Title( "One Minus" ), Category( "Unary" )]
public sealed class OneMinus : ShaderNode
{
	[Input( typeof( float ) ), Hide, Title( "" )]
	public NodeInput In { get; set; }

	[Output, Hide, Title( "" )]
	public NodeResult.Func Out => ( GraphCompiler compiler ) =>
	{
		var result = compiler.ResultOrDefault( In, 0.0f );
		return new NodeResult( result.ResultType, $"1 - {result}" );
	};

	public OneMinus() : base()
	{
		ExpandSize = new Vector2( -85, 0 );
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Shrink( 0, 20, 0, 0 );
		Paint.SetPen( Theme.TextControl );
		Paint.SetFont( "Poppins Bold", 12 );
		Paint.DrawText( rect, "1 - x" );
	}
}

[Title( "Sqrt" ), Category( "Unary" )]
public sealed class Sqrt : Unary
{
	[Input, Title( "" ), Hide]
	public override NodeInput Input { get => base.Input; set => base.Input = value; }

	[Output, Title( "" ), Hide]
	public override NodeResult.Func Result => base.Result;

	public Sqrt() : base()
	{
		ExpandSize = new Vector2( -12 * 8, 12 );
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Shrink( 2, 20, 0, 0 );
		Paint.SetPen( Theme.TextControl );
		Paint.SetFont( "Poppins Bold", 12 );
		Paint.DrawText( rect, " x" );
		List<Vector2> points = new()
		{
			rect.TopLeft + new Vector2(10, 20),
			rect.TopLeft + new Vector2(14, 20),
			rect.TopLeft + new Vector2(18, 30),
			rect.TopLeft + new Vector2(22, 12),
			rect.TopLeft + new Vector2(40, 12)
		};
		for ( int i = points.Count - 1; i >= 0; i-- )
		{
			points.Add( points[i] );
		}
		Paint.DrawPolygon( points.ToArray() );
	}

	[Hide]
	protected override string Op => "sqrt";
}

/// <summary>
/// Returns a distance scalar between two vectors.
/// </summary>
[Title( "Distance" ), Category( "Unary" )]
public sealed class Distance : ShaderNode
{
	[Input]
	[Hide]
	public NodeInput A { get; set; }

	[Input]
	[Hide]
	public NodeInput B { get; set; }

	[Output]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var resultA = compiler.ResultOrDefault( A, 0.0f );
		var resultB = compiler.ResultOrDefault( B, 0.0f ).Cast( resultA.Components );

		return new NodeResult( NodeResultType.Float, $"distance( {resultA}, {resultB} )" );
	};
}
