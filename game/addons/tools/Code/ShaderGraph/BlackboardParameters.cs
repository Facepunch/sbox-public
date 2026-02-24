using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

/// <summary>
/// Bool value material parameter
/// </summary>
[Title( "Bool" ), Icon( "check_box" ), Order( 0 )]
public sealed class BoolParameter : BlackboardMaterialParameter<bool, BoolParameterUI>
{
	public BoolParameter() : base()
	{
		UI = new BoolParameterUI();
		Value = false;
	}

	public BoolParameter( string name, bool value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		UI = new BoolParameterUI();
	}
}

/// <summary>
/// Int value material parameter
/// </summary>
[Title( "Int" ), Icon( "looks_one" ), Order( 1 )]
public sealed class IntParameter : BlackboardMaterialParameter<int, IntParameterUI>
{
	[Group( "Range" )] public int Min { get; set; }
	[Group( "Range" )] public int Max { get; set; }

	public IntParameter() : base()
	{
		Value = 1;
		Min = 0;
		Max = 1;
		UI = new IntParameterUI();
	}

	public IntParameter( string name, int value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		Min = 0;
		Max = 1;
		UI = new IntParameterUI();
	}
}

/// <summary>
/// Float value material parameter
/// </summary>
[Title( "Float" ), Icon( "looks_one" ), Order( 2 )]
public sealed class FloatParameter : BlackboardMaterialParameter<float, FloatParameterUI>
{
	[Group( "Range" )] public float Min { get; set; }
	[Group( "Range" )] public float Max { get; set; }

	public FloatParameter() : base()
	{
		Value = 1.0f;
		Min = 0.0f;
		Max = 1.0f;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public FloatParameter( string name, float value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		Min = 0.0f;
		Max = 1.0f;
		UI = new FloatParameterUI { Type = UIType.Default };
	}
}

/// <summary>
/// Float2 value material parameter
/// </summary>
[Title( "Float2" ), Icon( "looks_two" ), Order( 3 )]
public sealed class Float2Parameter : BlackboardMaterialParameter<Vector2, FloatParameterUI>
{
	[Group( "Range" )] public Vector2 Min { get; set; }
	[Group( "Range" )] public Vector2 Max { get; set; }

	public Float2Parameter() : base()
	{
		Value = Vector2.One;
		Min = Vector2.Zero;
		Max = Vector2.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public Float2Parameter( string name, Vector2 value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		Min = Vector2.Zero;
		Max = Vector2.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}
}

/// <summary>
/// Float3 value material parameter
/// </summary>
[Title( "Float3" ), Icon( "looks_3" ), Order( 4 )]
public sealed class Float3Parameter : BlackboardMaterialParameter<Vector3, FloatParameterUI>
{
	[Group( "Range" )] public Vector3 Min { get; set; }
	[Group( "Range" )] public Vector3 Max { get; set; }

	public Float3Parameter() : base()
	{
		Value = Vector3.One;
		Min = Vector3.Zero;
		Max = Vector3.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public Float3Parameter( string name, Vector3 value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		Min = Vector3.Zero;
		Max = Vector3.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}
}

/// <summary>
/// Float4 value material parameter
/// </summary>
[Title( "Float4" ), Icon( "looks_4" ), Order( 5 )]
public sealed class Float4Parameter : BlackboardMaterialParameter<Vector4, FloatParameterUI>
{
	[Group( "Range" )] public Vector4 Min { get; set; }
	[Group( "Range" )] public Vector4 Max { get; set; }

	public Float4Parameter() : base()
	{
		Value = Vector4.One;
		Min = Vector4.Zero;
		Max = Vector4.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public Float4Parameter( string name, Vector4 value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		Min = Vector4.Zero;
		Max = Vector4.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}
}

/// <summary>
/// Color value material parameter
/// </summary>
[Title( "Color" ), Icon( "palette" ), Order( 6 )]
public sealed class ColorParameter : BlackboardMaterialParameter<Color, ColorParameterUI>
{
	public ColorParameter() : base()
	{
		Value = Color.White;
		UI = new ColorParameterUI();
	}

	public ColorParameter( string name, Color value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		UI = new ColorParameterUI();
	}
}

/// <summary>
/// Texture2D material parameter
/// </summary>
[Title( "Texture2D" ), Icon( "image" ), Order( 7 )]
public sealed class Texture2DParameter : BlackboardTextureMaterialParameter
{
	public Texture2DParameter() : base()
	{
		Value = new TextureInput()
		{
			ImageFormat = TextureFormat.DXT5,
			Type = TextureType.Tex2D,
			SrgbRead = true,
			Default = Color.White,
		};
	}

	public Texture2DParameter( string name, TextureInput value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Bool subgraph input parameter
/// </summary>
[Title( "Bool" ), Icon( "check_box" ), Order( 0 )]
public sealed class BoolSubgraphInputParameter : BlackboardSubgraphinputParameter<bool>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Bool;

	public BoolSubgraphInputParameter() : base()
	{
		Value = false;
	}

	public BoolSubgraphInputParameter( string name, bool value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Int subgraph input parameter
/// </summary>
[Title( "Int" ), Icon( "looks_one" ), Order( 1 )]
public sealed class IntSubgraphInputParameter : BlackboardSubgraphinputParameter<int>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Int;

	public IntSubgraphInputParameter() : base()
	{
		Value = 1;
	}

	public IntSubgraphInputParameter( string name, int value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float subgraph input parameter
/// </summary>
[Title( "Float" ), Icon( "looks_one" ), Order( 2 )]
public sealed class FloatSubgraphInputParameter : BlackboardSubgraphinputParameter<float>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float;

	public FloatSubgraphInputParameter() : base()
	{
		Value = 1.0f;
	}

	public FloatSubgraphInputParameter( string name, float value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float2 subgraph input parameter
/// </summary>
[Title( "Float2" ), Icon( "looks_two" ), Order( 3 )]
public sealed class Float2SubgraphInputParameter : BlackboardSubgraphinputParameter<Vector2>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float2;

	public Float2SubgraphInputParameter() : base()
	{
		Value = Vector2.One;
	}

	public Float2SubgraphInputParameter( string name, Vector2 value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float3 subgraph input parameter
/// </summary>
[Title( "Float3" ), Icon( "looks_3" ), Order( 4 )]
public sealed class Float3SubgraphInputParameter : BlackboardSubgraphinputParameter<Vector3>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float3;

	public Float3SubgraphInputParameter() : base()
	{
		Value = Vector3.One;
	}

	public Float3SubgraphInputParameter( string name, Vector3 value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float4 subgraph input parameter
/// </summary>
[Title( "Float4" ), Icon( "looks_4" ), Order( 5 )]
public sealed class Float4SubgraphInputParameter : BlackboardSubgraphinputParameter<Vector4>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float4;

	public Float4SubgraphInputParameter() : base()
	{
		Value = Vector4.One;
	}

	public Float4SubgraphInputParameter( string name, Vector4 value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Color subgraph input parameter
/// </summary>
[Title( "Color" ), Icon( "palette" ), Order( 6 )]
public sealed class ColorSubgraphInputParameter : BlackboardSubgraphinputParameter<Color>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Color;

	public ColorSubgraphInputParameter() : base()
	{
		Value = Color.White;
	}

	public ColorSubgraphInputParameter( string name, Color value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Texture2D subgraph input parameter
/// </summary>
[Title( "Texture2D" ), Icon( "image" ), Order( 7 )]
public sealed class Texture2DSubgraphInputParameter : BlackboardSubgraphinputParameter<TextureInput>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Texture2D;

	public Texture2DSubgraphInputParameter() : base()
	{
		Value = new TextureInput()
		{
			ImageFormat = TextureFormat.DXT5,
			Type = TextureType.Tex2D,
			SrgbRead = true,
			Default = Color.White,
		};
	}

	public Texture2DSubgraphInputParameter( string name, TextureInput value )
		: base( name, value )
	{
	}
}
