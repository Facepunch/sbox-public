
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Bool value
/// </summary>
[Title( "Bool" ), Category( "Parameters" ), Icon( "check_box" ), Order( 0 )]
[Hide]
public sealed class BoolParameter : ParameterNode<bool, BoolParameterUI, BoolBlackboardParameter>
{
	[Output( typeof( bool ) ), Title( "Value" )]
	[Hide, Editor( nameof( Value ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, default, default, false, IsAttribute, UI );
	};

	public BoolParameter()
	{
		UI = new BoolParameterUI();
	}

	protected override void UpdateFromBlackboardParameter( BoolBlackboardParameter parameter )
	{
		
	}
}

/// <summary>
/// Single int value
/// </summary>
[Title( "Int" ), Category( "Parameters" ), Icon( "looks_one" ), Order( 1 )]
[Hide]
public sealed class IntParameter : ParameterNode<int, IntParameterUI, IntBlackboardParameter>
{
	[Hide] public float Step => 1;

	[Output( typeof( int ) ), Title( "Value" )]
	[Hide, Editor( nameof( Value ) ), Range( nameof( Min ), nameof( Max ), nameof( Step ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, Min, Max, Min != Max, IsAttribute, UI );
	};

	[Group( "Range" )] public int Min { get; set; }
	[Group( "Range" )] public int Max { get; set; }

	public IntParameter()
	{
		Min = 0;
		Max = 1;
		UI = new IntParameterUI();
	}

	protected override void UpdateFromBlackboardParameter( IntBlackboardParameter parameter )
	{
		
	}
}

/// <summary>
/// Single float value
/// </summary>
[Title( "Float" ), Category( "Parameters" ), Icon( "looks_one" ), Order( 2 )]
[Hide]
public sealed class FloatParameter : ParameterNode<float, FloatParameterUI, FloatBlackboardParameter>
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

	public FloatParameter()
	{
		Min = 0;
		Max = 1;
		UI = new FloatParameterUI();
	}

	public override Vector4 GetRangeMin()
	{
		return new( Min );
	}

	public override Vector4 GetRangeMax()
	{
		return new( Max );
	}

	protected override void UpdateFromBlackboardParameter( FloatBlackboardParameter parameter )
	{
		
	}
}

/// <summary>
/// 2 float values
/// </summary>
[Title( "Float2" ), Category( "Parameters" ), Icon( "looks_two" ), Order( 3 )]
[Hide]
public sealed class Float2Parameter : ParameterNode<Vector2, FloatParameterUI, Float2BlackboardParameter>
{
	[Output( typeof( Vector2 ) ), Title( "XY" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, Min, Max, Min != Max, IsAttribute, UI );
	};

	[Group( "Range" )] public Vector2 Min { get; set; }
	[Group( "Range" )] public Vector2 Max { get; set; }

	public Float2Parameter()
	{
		Min = 0;
		Max = 1;
		UI = new FloatParameterUI();
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

	protected override void UpdateFromBlackboardParameter( Float2BlackboardParameter parameter )
	{
		
	}
}

/// <summary>
/// 3 float values
/// </summary>
[Title( "Float3" ), Category( "Parameters" ), Icon( "looks_3" ), Order( 4 )]
[Hide]
public sealed class Float3Parameter : ParameterNode<Vector3, FloatParameterUI, Float3BlackboardParameter>
{
	[Output( typeof( Vector3 ) ), Title( "XYZ" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, Min, Max, Min != Max, IsAttribute, UI );
	};

	[Group( "Range" )] public Vector3 Min { get; set; }
	[Group( "Range" )] public Vector3 Max { get; set; }

	public Float3Parameter()
	{
		Min = 0;
		Max = 1;
		UI = new FloatParameterUI();
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

	protected override void UpdateFromBlackboardParameter( Float3BlackboardParameter parameter )
	{
		
	}
}

/// <summary>
/// 4 float values
/// </summary>
[Title( "Float4" ), Category( "Parameters" ), Icon( "palette" ), Order( 5 )]
[Hide]
public sealed class Float4Parameter : ParameterNode<Vector4, FloatParameterUI, Float4BlackboardParameter>
{
	[Output( typeof( Vector4 ) ), Title( "XYZ" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( Name, Value, default, default, false, IsAttribute, UI );
	};

	[Group( "Range" )] public Vector4 Min { get; set; }
	[Group( "Range" )] public Vector4 Max { get; set; }

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

	/// <summary>
	/// W component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Editor( nameof( ValueW ) ), Title( "W" )]
	[Range( nameof( MinW ), nameof( MaxW ), nameof( Step ) )]
	public NodeResult.Func W => ( GraphCompiler compiler ) => Component( "w", ValueW, compiler );

	public Float4Parameter()
	{
		Min = 0;
		Max = 1;
		UI = new FloatParameterUI();
	}

	protected override void UpdateFromBlackboardParameter( Float4BlackboardParameter parameter )
	{
		
	}
}

/// <summary>
/// 4 float values, normally used as a color
/// </summary>
[Title( "Color" ), Category( "Parameters" ), Icon( "palette" ), Order( 6 )]
[Hide]
public sealed class ColorParameter : ParameterNode<Color, ColorParameterUI, ColorBlackboardParameter>
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

	public ColorParameter()
	{
		Value = Color.White;
		UI = new ColorParameterUI();
	}

	protected override void UpdateFromBlackboardParameter( ColorBlackboardParameter parameter )
	{
		
	}
}
