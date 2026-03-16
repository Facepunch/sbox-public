
namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Single float value
/// </summary>
[Title( "Float" ), Category( "Constants" ), Icon( "looks_one" )]
public sealed class Float : ParameterNode<float>
{
	[Hide] public float Step => UI.Step;

	[Output( typeof( float ) ), Title( "Value" )]
	[Hide, Editor( nameof( Value ) ), Range( nameof( Min ), nameof( Max ), nameof( Step ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, Min, Max, Min != Max, IsAttribute, UI );
	};

	[Group( "Range" )] public float Min { get; set; }
	[Group( "Range" )] public float Max { get; set; }

	public Float()
	{
		Min = 0;
		Max = 1;
	}

	public override Vector4 GetRangeMin()
	{
		return new( Min );
	}

	public override Vector4 GetRangeMax()
	{
		return new( Max );
	}
}

/// <summary>
/// 2 float values
/// </summary>
[Title( "Float2" ), Category( "Constants" ), Icon( "looks_two" )]
public sealed class Float2 : ParameterNode<Vector2>
{
	[Output( typeof( Vector2 ) ), Title( "XY" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, Min, Max, Min != Max, IsAttribute, UI );
	};

	[Group( "Range" )] public Vector2 Min { get; set; }
	[Group( "Range" )] public Vector2 Max { get; set; }

	public Float2()
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

	[Hide] public float Step => UI.Step;

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

	public override Vector4 GetRangeMin()
	{
		return new( Min.x, Min.y, 0, 0 );
	}

	public override Vector4 GetRangeMax()
	{
		return new( Max.x, Max.y, 0, 0 );
	}
}

/// <summary>
/// 3 float values
/// </summary>
[Title( "Float3" ), Category( "Constants" ), Icon( "looks_3" )]
public sealed class Float3 : ParameterNode<Vector3>
{
	[Output( typeof( Vector3 ) ), Title( "XYZ" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, Min, Max, Min != Max, IsAttribute, UI );
	};

	[Group( "Range" )] public Vector3 Min { get; set; }
	[Group( "Range" )] public Vector3 Max { get; set; }

	public Float3()
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

	[Hide] public float Step => UI.Step;

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

	public override Vector4 GetRangeMin()
	{
		return new( Min.x, Min.y, Min.z, 0 );
	}

	public override Vector4 GetRangeMax()
	{
		return new( Max.x, Max.y, Max.z, 0 );
	}
}

/// <summary>
/// 4 float values, normally used as a color
/// </summary>
[Title( "Color" ), Category( "Constants" ), Icon( "palette" )]
public sealed class Float4 : ParameterNode<Color>
{
	[Output( typeof( Color ) ), Title( "RGBA" )]
	[Hide, Editor( nameof( Value ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, default, default, false, IsAttribute, UI );
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

	public Float4()
	{
		Value = Color.White;
		UI = new ParameterUI { Type = UIType.Color };
	}
}
