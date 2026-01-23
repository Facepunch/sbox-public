using System.Text.Json.Serialization;

namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Bool value
/// </summary>
[Title( "Bool Constant" ), Category( "Constants" ), Icon( "check_box" ), Order( 0 )]
public sealed class BoolConstant : ConstantNode<int>
{
	[Output( typeof( bool ) ), Title( "Value" )]
	[Hide, Editor( nameof( Value ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	public BoolConstant()
	{
	}
}

/// <summary>
/// Single int value
/// </summary>
[Title( "Int Constant" ), Category( "Constants" ), Icon( "looks_one" ), Order( 1 )]
public sealed class ConstantInt : ConstantNode<float>
{
	[Hide] public float Step => 1;

	[Output( typeof( float ) ), Title( "Value" )]
	[Hide, Editor( nameof( Value ) ), Range( nameof( Min ), nameof( Max ), nameof( Step ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	[Group( "Range" )] public int Min { get; set; }
	[Group( "Range" )] public int Max { get; set; }

	public ConstantInt()
	{
		Min = 0;
		Max = 1;
	}
}

/// <summary>
/// Single float value
/// </summary>
[Title( "Float Constant" ), Category( "Constants" ), Icon( "looks_one" ), Order( 2 )]
public sealed class ConstantFloat : ConstantNode<float>
{
	public float Step { get; set; } = 0.0f;

	[Output( typeof( float ) ), Title( "Value" )]
	[Hide, Editor( nameof( Value ) ), Range( nameof( Min ), nameof( Max ), nameof( Step ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	[Group( "Range" )] public float Min { get; set; }
	[Group( "Range" )] public float Max { get; set; }

	public ConstantFloat()
	{
		Min = 0;
		Max = 1;
	}
}

/// <summary>
/// 2 float values
/// </summary>
[Title( "Float2 Constant" ), Category( "Constants" ), Icon( "looks_two" ), Order( 3 )]
public sealed class ConstantFloat2 : ConstantNode<Vector2>
{
	[Output( typeof( Vector2 ) ), Title( "XY" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	[Group( "Range" )] public Vector2 Min { get; set; }
	[Group( "Range" )] public Vector2 Max { get; set; }
	public float Step { get; set; } = 0.0f;

	public ConstantFloat2()
	{
		Min = 0;
		Max = 1;
	}

	[JsonIgnore, Hide]
	public float ValueX
	{
		get => Value.x;
		set => Value = Value.WithX( value );
	}

	[JsonIgnore, Hide]
	public float ValueY
	{
		get => Value.y;
		set => Value = Value.WithY( value );
	}

	[Hide] public float MinX => Min.x;
	[Hide] public float MinY => Min.y;
	[Hide] public float MaxX => Max.x;
	[Hide] public float MaxY => Max.y;

	/// <summary>
	/// X component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueX ) ), Title( "X" )]
	[Range( nameof( MinX ), nameof( MaxX ), nameof( Step ) )]
	public NodeResult.Func X => ( GraphCompiler compiler ) => Component( "x", ValueX, compiler );

	/// <summary>
	/// Y component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueY ) ), Title( "Y" )]
	[Range( nameof( MinY ), nameof( MaxY ), nameof( Step ) )]
	public NodeResult.Func Y => ( GraphCompiler compiler ) => Component( "y", ValueY, compiler );
}

/// <summary>
/// 3 float values
/// </summary>
[Title( "Float3 Constant" ), Category( "Constants" ), Icon( "looks_3" ), Order( 4 )]
public sealed class ConstantFloat3 : ConstantNode<Vector3>
{
	[Output( typeof( Vector3 ) ), Title( "XYZ" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	[Group( "Range" )] public Vector3 Min { get; set; }
	[Group( "Range" )] public Vector3 Max { get; set; }
	public float Step { get; set; } = 0.0f;

	public ConstantFloat3()
	{
		Min = 0;
		Max = 1;
	}

	[JsonIgnore, Hide]
	public float ValueX
	{
		get => Value.x;
		set => Value = Value.WithX( value );
	}

	[JsonIgnore, Hide]
	public float ValueY
	{
		get => Value.y;
		set => Value = Value.WithY( value );
	}

	[JsonIgnore, Hide]
	public float ValueZ
	{
		get => Value.z;
		set => Value = Value.WithZ( value );
	}

	[Hide] public float MinX => Min.x;
	[Hide] public float MinY => Min.y;
	[Hide] public float MinZ => Min.z;
	[Hide] public float MaxX => Max.x;
	[Hide] public float MaxY => Max.y;
	[Hide] public float MaxZ => Max.z;

	/// <summary>
	/// X component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueX ) ), Title( "X" )]
	[Range( nameof( MinX ), nameof( MaxX ), nameof( Step ) )]
	public NodeResult.Func X => ( GraphCompiler compiler ) => Component( "x", ValueX, compiler );

	/// <summary>
	/// Y component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueY ) ), Title( "Y" )]
	[Range( nameof( MinY ), nameof( MaxY ), nameof( Step ) )]
	public NodeResult.Func Y => ( GraphCompiler compiler ) => Component( "y", ValueY, compiler );

	/// <summary>
	/// Z component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueZ ) ), Title( "Z" )]
	[Range( nameof( MinZ ), nameof( MaxZ ), nameof( Step ) )]
	public NodeResult.Func Z => ( GraphCompiler compiler ) => Component( "z", ValueZ, compiler );
}

/// <summary>
/// 3 float values
/// </summary>
[Title( "Float4 Constant" ), Category( "Constants" ), Icon( "looks_4" ), Order( 5 )]
public sealed class ConstantFloat4 : ConstantNode<Vector4>
{
	[Output( typeof( Vector4 ) ), Title( "XYZW" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	[Group( "Range" )] public Vector4 Min { get; set; }
	[Group( "Range" )] public Vector4 Max { get; set; }
	public float Step { get; set; } = 0.0f;

	public ConstantFloat4()
	{
		Min = 0;
		Max = 1;
	}

	[JsonIgnore, Hide]
	public float ValueX
	{
		get => Value.x;
		set => Value = Value.WithX( value );
	}

	[JsonIgnore, Hide]
	public float ValueY
	{
		get => Value.y;
		set => Value = Value.WithY( value );
	}

	[JsonIgnore, Hide]
	public float ValueZ
	{
		get => Value.z;
		set => Value = Value.WithZ( value );
	}

	[JsonIgnore, Hide]
	public float ValueW
	{
		get => Value.w;
		set => Value = Value.WithW( value );
	}

	[Hide] public float MinX => Min.x;
	[Hide] public float MinY => Min.y;
	[Hide] public float MinZ => Min.z;
	[Hide] public float MinW => Min.w;
	[Hide] public float MaxX => Max.x;
	[Hide] public float MaxY => Max.y;
	[Hide] public float MaxZ => Max.z;
	[Hide] public float MaxW => Max.w;

	/// <summary>
	/// X component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueX ) ), Title( "X" )]
	[Range( nameof( MinX ), nameof( MaxX ), nameof( Step ) )]
	public NodeResult.Func X => ( GraphCompiler compiler ) => Component( "x", ValueX, compiler );

	/// <summary>
	/// Y component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueY ) ), Title( "Y" )]
	[Range( nameof( MinY ), nameof( MaxY ), nameof( Step ) )]
	public NodeResult.Func Y => ( GraphCompiler compiler ) => Component( "y", ValueY, compiler );

	/// <summary>
	/// Z component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueZ ) ), Title( "Z" )]
	[Range( nameof( MinZ ), nameof( MaxZ ), nameof( Step ) )]
	public NodeResult.Func Z => ( GraphCompiler compiler ) => Component( "z", ValueZ, compiler );

	/// <summary>
	/// W component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueW ) ), Title( "W" )]
	[Range( nameof( MinW ), nameof( MaxW ), nameof( Step ) )]
	public NodeResult.Func W => ( GraphCompiler compiler ) => Component( "W", ValueW, compiler );
}

/// <summary>
/// Color value
/// </summary>
[Title( "Color Constant" ), Category( "Constants" ), Icon( "palette" ), Order( 6 )]
public sealed class ConstantColor : ConstantNode<Color>
{
	[Output( typeof( Color ) ), Title( "RGBA" )]
	[Hide, Editor( nameof( Value ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( "", Value, default, default, false, false, default );
	};

	[JsonIgnore, Hide]
	public float ValueR
	{
		get => Value.r;
		set => Value = Value.WithRed( value );
	}

	[JsonIgnore, Hide]
	public float ValueG
	{
		get => Value.g;
		set => Value = Value.WithGreen( value );
	}

	[JsonIgnore, Hide]
	public float ValueB
	{
		get => Value.b;
		set => Value = Value.WithBlue( value );
	}

	[JsonIgnore, Hide]
	public float ValueA
	{
		get => Value.a;
		set => Value = Value.WithAlpha( value );
	}

	/// <summary>
	/// Red component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueR ) ), Title( "Red" )]
	public NodeResult.Func R => ( GraphCompiler compiler ) => Component( "r", ValueR, compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueG ) ), Title( "Green" )]
	public NodeResult.Func G => ( GraphCompiler compiler ) => Component( "g", ValueG, compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueB ) ), Title( "Blue" )]
	public NodeResult.Func B => ( GraphCompiler compiler ) => Component( "b", ValueB, compiler );

	/// <summary>
	/// Alpha component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueA ) ), Title( "Alpha" )]
	public NodeResult.Func A => ( GraphCompiler compiler ) => Component( "a", ValueA, compiler );

	public ConstantColor()
	{
		Value = Color.White;
	}
}
