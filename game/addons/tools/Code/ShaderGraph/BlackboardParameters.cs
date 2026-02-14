using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

/// <summary>
/// Bool value material parameter
/// </summary>
[Title( "Bool" ), Icon( "check_box" ), Order( 0 )]
public sealed class BoolBlackboardParameter : BlackboardMaterialParameter<bool, BoolParameterUI>
{
	public BoolBlackboardParameter() : base()
	{
		UI = new BoolParameterUI();
		Value = false;
	}

	public BoolBlackboardParameter( string name, bool value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		UI = new BoolParameterUI();
	}
}

/// <summary>
/// Int value material parameter
/// </summary>
[Title( "Int" ), Icon( "looks_one" ), Order( 1 )]
public sealed class IntBlackboardParameter : BlackboardMaterialParameter<int, IntParameterUI>
{
	[Group( "Range" )] public int Min { get; set; }
	[Group( "Range" )] public int Max { get; set; }

	public IntBlackboardParameter() : base()
	{
		Value = 1;
		Min = 0;
		Max = 1;
		UI = new IntParameterUI();
	}

	public IntBlackboardParameter( string name, int value, bool isAttribute )
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
public sealed class FloatBlackboardParameter : BlackboardMaterialParameter<float, FloatParameterUI>
{
	[Group( "Range" )] public float Min { get; set; }
	[Group( "Range" )] public float Max { get; set; }

	public FloatBlackboardParameter() : base()
	{
		Value = 1.0f;
		Min = 0.0f;
		Max = 1.0f;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public FloatBlackboardParameter( string name, float value, bool isAttribute )
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
public sealed class Float2BlackboardParameter : BlackboardMaterialParameter<Vector2, FloatParameterUI>
{
	[Group( "Range" )] public Vector2 Min { get; set; }
	[Group( "Range" )] public Vector2 Max { get; set; }

	public Float2BlackboardParameter() : base()
	{
		Value = Vector2.One;
		Min = Vector2.Zero;
		Max = Vector2.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public Float2BlackboardParameter( string name, Vector2 value, bool isAttribute )
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
public sealed class Float3BlackboardParameter : BlackboardMaterialParameter<Vector3, FloatParameterUI>
{
	[Group( "Range" )] public Vector3 Min { get; set; }
	[Group( "Range" )] public Vector3 Max { get; set; }

	public Float3BlackboardParameter() : base()
	{
		Value = Vector3.One;
		Min = Vector3.Zero;
		Max = Vector3.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public Float3BlackboardParameter( string name, Vector3 value, bool isAttribute )
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
public sealed class Float4BlackboardParameter : BlackboardMaterialParameter<Vector4, FloatParameterUI>
{
	[Group( "Range" )] public Vector4 Min { get; set; }
	[Group( "Range" )] public Vector4 Max { get; set; }

	public Float4BlackboardParameter() : base()
	{
		Value = Vector4.One;
		Min = Vector4.Zero;
		Max = Vector4.One;
		UI = new FloatParameterUI { Type = UIType.Default };
	}

	public Float4BlackboardParameter( string name, Vector4 value, bool isAttribute )
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
public sealed class ColorBlackboardParameter : BlackboardMaterialParameter<Color, ColorParameterUI>
{
	public ColorBlackboardParameter() : base()
	{
		Value = Color.White;
		UI = new ColorParameterUI();
	}

	public ColorBlackboardParameter( string name, Color value, bool isAttribute )
		: base( name, value, isAttribute )
	{
		UI = new ColorParameterUI();
	}
}

/// <summary>
/// Texture2D material parameter
/// </summary>
[Title( "Texture2D" ), Icon( "image" ), Order( 7 )]
public sealed class Texture2DBlackboardParameter : BlackboardTextureMaterialParameter
{
	public Texture2DBlackboardParameter() : base()
	{
		Value = new TextureInput()
		{
			ImageFormat = TextureFormat.DXT5,
			Type = TextureType.Tex2D,
			SrgbRead = true,
			Default = Color.White,
		};
	}

	public Texture2DBlackboardParameter( string name, TextureInput value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Bool subgraph input parameter
/// </summary>
[Title( "Bool" ), Icon( "check_box" ), Order( 0 )]
public sealed class BoolSubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<bool>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Bool;

	public BoolSubgraphInputBlackboardParameter() : base()
	{
		Value = false;
	}

	public BoolSubgraphInputBlackboardParameter( string name, bool value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Int subgraph input parameter
/// </summary>
[Title( "Int" ), Icon( "looks_one" ), Order( 1 )]
public sealed class IntSubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<int>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Int;

	public IntSubgraphInputBlackboardParameter() : base()
	{
		Value = 1;
	}

	public IntSubgraphInputBlackboardParameter( string name, int value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float subgraph input parameter
/// </summary>
[Title( "Float" ), Icon( "looks_one" ), Order( 2 )]
public sealed class FloatSubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<float>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float;

	public FloatSubgraphInputBlackboardParameter() : base()
	{
		Value = 1.0f;
	}

	public FloatSubgraphInputBlackboardParameter( string name, float value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float2 subgraph input parameter
/// </summary>
[Title( "Float2" ), Icon( "looks_two" ), Order( 3 )]
public sealed class Float2SubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<Vector2>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float2;

	public Float2SubgraphInputBlackboardParameter() : base()
	{
		Value = Vector2.One;
	}

	public Float2SubgraphInputBlackboardParameter( string name, Vector2 value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float3 subgraph input parameter
/// </summary>
[Title( "Float3" ), Icon( "looks_3" ), Order( 4 )]
public sealed class Float3SubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<Vector3>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float3;

	public Float3SubgraphInputBlackboardParameter() : base()
	{
		Value = Vector3.One;
	}

	public Float3SubgraphInputBlackboardParameter( string name, Vector3 value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Float4 subgraph input parameter
/// </summary>
[Title( "Float4" ), Icon( "looks_4" ), Order( 5 )]
public sealed class Float4SubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<Vector4>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Float4;

	public Float4SubgraphInputBlackboardParameter() : base()
	{
		Value = Vector4.One;
	}

	public Float4SubgraphInputBlackboardParameter( string name, Vector4 value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Color subgraph input parameter
/// </summary>
[Title( "Color" ), Icon( "palette" ), Order( 6 )]
public sealed class ColorSubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<Color>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Color;

	public ColorSubgraphInputBlackboardParameter() : base()
	{
		Value = Color.White;
	}

	public ColorSubgraphInputBlackboardParameter( string name, Color value )
		: base( name, value )
	{
	}
}

/// <summary>
/// Texture2D subgraph input parameter
/// </summary>
[Title( "Texture2D" ), Icon( "image" ), Order( 7 )]
public sealed class Texture2DSubgraphInputBlackboardParameter : BlackboardSubgraphinputParameter<TextureInput>
{
	[Hide, JsonIgnore]
	public override InputType InputType => InputType.Texture2D;

	//[InlineEditor( Label = false ), Group( "Value" )]
	//[ImageAssetPath]
	//public override string Value { get; set; }

	public Texture2DSubgraphInputBlackboardParameter() : base()
	{
		Value = new TextureInput()
		{
			ImageFormat = TextureFormat.DXT5,
			Type = TextureType.Tex2D,
			SrgbRead = true,
			Default = Color.White,
		};
	}

	public Texture2DSubgraphInputBlackboardParameter( string name, TextureInput value )
		: base( name, value )
	{
	}
}
