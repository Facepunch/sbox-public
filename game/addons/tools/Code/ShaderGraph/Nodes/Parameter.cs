using System.Text.Json.Serialization;

namespace Editor.ShaderGraph.Nodes;

/// <summary>
/// Bool value
/// </summary>
[Title( "Bool" ), Category( "Parameters" ), Icon( "check_box" ), Order( 0 )]
[Hide]
public sealed class BoolParameterNode : ParameterNode<bool, BoolParameter>
{
	[Output( typeof( bool ) ), Title( "Value" )]
	[Hide, ValueEditor( nameof( Value ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, default, default, false, GetParameter().IsAttribute, GetParameter().UI );
	};

	public BoolParameterNode()
	{
	}
}

/// <summary>
/// Single int value
/// </summary>
[Title( "Int" ), Category( "Parameters" ), Icon( "looks_one" ), Order( 1 )]
[Hide]
public sealed class IntParameterNode : ParameterNode<int, IntParameter>
{
	[Hide] public float Step => 1;

	[Output( typeof( int ) ), Title( "Value" )]
	[Hide, ValueEditor( nameof( Value ) ), Range( nameof( Min ), nameof( Max ), nameof( Step ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, Min, Max, Min != Max, GetParameter().IsAttribute, GetParameter().UI );
	};

	[Hide] public int Min => GetParameter().Min;
	[Hide] public int Max => GetParameter().Max;

	public IntParameterNode()
	{
	}
}

/// <summary>
/// Single float value
/// </summary>
[Title( "Float" ), Category( "Parameters" ), Icon( "looks_one" ), Order( 2 )]
[Hide]
public sealed class FloatParameterNode : ParameterNode<float, FloatParameter>
{
	[Hide] public float Step => ((FloatParameterUI)GetParameter().UI).Step; //UI.Step;

	[Output( typeof( float ) ), Title( "Value" )]
	[Hide, ValueEditor( nameof( Value ) ), Range( nameof( Min ), nameof( Max ), nameof( Step ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, Min, Max, Min != Max, GetParameter().IsAttribute, GetParameter().UI );
	};

	[Hide] public float Min => GetParameter().Min;
	[Hide] public float Max => GetParameter().Max;

	public FloatParameterNode()
	{
	}
}

/// <summary>
/// 2 float values
/// </summary>
[Title( "Float2" ), Category( "Parameters" ), Icon( "looks_two" ), Order( 3 )]
[Hide]
public sealed class Float2ParameterNode : ParameterNode<Vector2, Float2Parameter>
{
	[Output( typeof( Vector2 ) ), Title( "XY" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, Min, Max, Min != Max, GetParameter().IsAttribute, GetParameter().UI );
	};

	[Hide] public Vector2 Min => GetParameter().Min;
	[Hide] public Vector2 Max => GetParameter().Max;

	public Float2ParameterNode()
	{
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

	[Hide] public float Step => GetParameter().UI.Step;

	/// <summary>
	/// X component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueX ) ), Title( "X" )]
	[Range( nameof( MinX ), nameof( MaxX ), nameof( Step ) )]
	public NodeResult.Func X => ( GraphCompiler compiler ) => Component( "x", ValueX, compiler );

	/// <summary>
	/// Y component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueY ) ), Title( "Y" )]
	[Range( nameof( MinY ), nameof( MaxY ), nameof( Step ) )]
	public NodeResult.Func Y => ( GraphCompiler compiler ) => Component( "y", ValueY, compiler );
}

/// <summary>
/// 3 float values
/// </summary>
[Title( "Float3" ), Category( "Parameters" ), Icon( "looks_3" ), Order( 4 )]
[Hide]
public sealed class Float3ParameterNode : ParameterNode<Vector3, Float3Parameter>
{
	[Output( typeof( Vector3 ) ), Title( "XYZ" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, Min, Max, Min != Max, GetParameter().IsAttribute, GetParameter().UI );
	};

	[Hide] public Vector3 Min => GetParameter().Min;
	[Hide] public Vector3 Max => GetParameter().Max;

	public Float3ParameterNode()
	{
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

	[Hide] public float Step => GetParameter().UI.Step;

	/// <summary>
	/// X component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueX ) ), Title( "X" )]
	[Range( nameof( MinX ), nameof( MaxX ), nameof( Step ) )]
	public NodeResult.Func X => ( GraphCompiler compiler ) => Component( "x", ValueX, compiler );

	/// <summary>
	/// Y component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueY ) ), Title( "Y" )]
	[Range( nameof( MinY ), nameof( MaxY ), nameof( Step ) )]
	public NodeResult.Func Y => ( GraphCompiler compiler ) => Component( "y", ValueY, compiler );

	/// <summary>
	/// Z component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueZ ) ), Title( "Z" )]
	[Range( nameof( MinZ ), nameof( MaxZ ), nameof( Step ) )]
	public NodeResult.Func Z => ( GraphCompiler compiler ) => Component( "z", ValueZ, compiler );
}

/// <summary>
/// 4 float values
/// </summary>
[Title( "Float4" ), Category( "Parameters" ), Icon( "palette" ), Order( 5 )]
[Hide]
public sealed class Float4ParameterNode : ParameterNode<Vector4, Float4Parameter>
{
	[Output( typeof( Vector4 ) ), Title( "XYZW" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, default, default, false, GetParameter().IsAttribute, GetParameter().UI );
	};

	[Hide] public Vector4 Min => GetParameter().Min;
	[Hide] public Vector4 Max => GetParameter().Max;

	public Float4ParameterNode()
	{
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

	[Hide] public float Step => GetParameter().UI.Step;

	/// <summary>
	/// X component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueX ) ), Title( "X" )]
	[Range( nameof( MinX ), nameof( MaxX ), nameof( Step ) )]
	public NodeResult.Func X => ( GraphCompiler compiler ) => Component( "x", ValueX, compiler );

	/// <summary>
	/// Y component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueY ) ), Title( "Y" )]
	[Range( nameof( MinY ), nameof( MaxY ), nameof( Step ) )]
	public NodeResult.Func Y => ( GraphCompiler compiler ) => Component( "y", ValueY, compiler );

	/// <summary>
	/// Z component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueZ ) ), Title( "Z" )]
	[Range( nameof( MinZ ), nameof( MaxZ ), nameof( Step ) )]
	public NodeResult.Func Z => ( GraphCompiler compiler ) => Component( "z", ValueZ, compiler );

	/// <summary>
	/// W component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueW ) ), Title( "W" )]
	[Range( nameof( MinW ), nameof( MaxW ), nameof( Step ) )]
	public NodeResult.Func W => ( GraphCompiler compiler ) => Component( "w", ValueW, compiler );
}

/// <summary>
/// 4 float values, normally used as a color
/// </summary>
[Title( "Color" ), Category( "Parameters" ), Icon( "palette" ), Order( 6 )]
[Hide]
public sealed class ColorParameterNode : ParameterNode<Color, ColorParameter>
{
	[Output( typeof( Color ) ), Title( "RGBA" )]
	[Hide, ValueEditor( nameof( Value ) )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		return compiler.ResultParameter( GetParameter().Name, Value, default, default, false, GetParameter().IsAttribute, GetParameter().UI );
	};

	public ColorParameterNode()
	{
	}

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
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueR ) ), Title( "Red" )]
	public NodeResult.Func R => ( GraphCompiler compiler ) => Component( "r", ValueR, compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueG ) ), Title( "Green" )]
	public NodeResult.Func G => ( GraphCompiler compiler ) => Component( "g", ValueG, compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueB ) ), Title( "Blue" )]
	public NodeResult.Func B => ( GraphCompiler compiler ) => Component( "b", ValueB, compiler );

	/// <summary>
	/// Alpha component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, ValueEditor( nameof( ValueA ) ), Title( "Alpha" )]
	public NodeResult.Func A => ( GraphCompiler compiler ) => Component( "a", ValueA, compiler );
}

/// <summary>
/// Texture2D
/// </summary>
[Title( "Texture 2D" ), Category( "Parameters" ), Icon( "image" ), Order( 7 )]
[Hide]
public sealed class Texture2DParameterNode : ShaderNode, IParameterNode
{
	[JsonIgnore, Hide]
	public override string Title => string.IsNullOrWhiteSpace( Name ) ?
		$"{DisplayInfo.For( this ).Name}" :
		$"{Name}";

	[Hide]
	public string Name => UI.Name;

	[Hide]
	public Guid ParameterIdentifier { get; set; }

	[JsonIgnore, Hide]
	public TextureInput UI => GetParameter().Value;

	private Texture2DParameter GetParameter()
	{
		if ( Graph is ShaderGraph graph )
		{
			return graph.FindParameter<Texture2DParameter>( ParameterIdentifier );
		}

		return null;
	}

	public Texture2DParameterNode()
	{
	}

	[Output( typeof( Texture ) ), Title( "Texture2D" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var input = UI;
		input.Type = TextureType.Tex2D;

		var textureGlobal = compiler.ResultTexture( input, null, true );

		return new NodeResult( NodeResultType.Texture2D, textureGlobal, true );
	};
}

/// <summary>
/// TextureCube
/// </summary>
[Title( "Texture Cube" ), Category( "Parameters" ), Icon( "image" ), Order( 8 )]
[Hide]
public sealed class TextureCubeParameterNode : ShaderNode, IParameterNode
{
	[JsonIgnore, Hide]
	public override string Title => string.IsNullOrWhiteSpace( Name ) ?
		$"{DisplayInfo.For( this ).Name}" :
		$"{Name}";

	[Hide]
	public string Name => UI.Name;

	[Hide]
	public Guid ParameterIdentifier { get; set; }

	[JsonIgnore, Hide]
	public TextureInput UI => GetParameter().Value;

	private TextureCubeParameter GetParameter()
	{
		if ( Graph is ShaderGraph graph )
		{
			return graph.FindParameter<TextureCubeParameter>( ParameterIdentifier );
		}

		return null;
	}

	public TextureCubeParameterNode()
	{
	}

	[Output( typeof( Texture ) ), Title( "TextureCube" )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var input = UI;
		input.Type = TextureType.TexCube;

		var textureGlobal = compiler.ResultTexture( input, null, true );

		return new NodeResult( NodeResultType.TextureCube, textureGlobal, true );
	};
}
