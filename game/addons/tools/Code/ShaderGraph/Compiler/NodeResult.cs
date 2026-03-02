namespace Editor.ShaderGraph;

public enum NodeResultType
{
	Bool,
	Int,
	Float,
	Vector2,
	Vector3,
	Vector4,
	Color,
	Texture2D,
	TextureCube,
	Invalid
}

public struct NodeResult : IValid
{
	public delegate NodeResult Func( GraphCompiler compiler );

	public string Code { get; private set; }
	public int Components { get; private set; }
	public NodeResultType ResultType { get; private set; }
	public bool Constant { get; set; }
	public string[] Errors { get; private init; }

	public readonly bool IsValid => ResultType != NodeResultType.Invalid && !string.IsNullOrWhiteSpace( Code );

	public readonly string TypeName => ResultType switch
	{
		NodeResultType.Bool => "bool",
		NodeResultType.Int => "int",
		NodeResultType.Float => "float",
		NodeResultType.Vector2 => "float2",
		NodeResultType.Vector3 => "float3",
		NodeResultType.Vector4 => "float4",
		NodeResultType.Color => "float4",
		NodeResultType.Texture2D => "Texture2D",
		NodeResultType.TextureCube => "TextureCube",
		_ => null,
	};

	public readonly Type ComponentType => ResultType switch
	{
		NodeResultType.Bool => typeof( bool ),
		NodeResultType.Int => typeof( int ),
		NodeResultType.Float => typeof( float ),
		NodeResultType.Vector2 => typeof( Vector2 ),
		NodeResultType.Vector3 => typeof( Vector3 ),
		NodeResultType.Vector4 => typeof( Vector4 ),
		NodeResultType.Color => typeof( Color ),
		NodeResultType.Texture2D => typeof( Texture ),
		NodeResultType.TextureCube => typeof( Texture ),
		_ => null,
	};

	public NodeResult( NodeResultType resultType, string code, bool constant = false )
	{
		ResultType = resultType;
		Components = resultType switch
		{
			NodeResultType.Bool => 1,
			NodeResultType.Int => 1,
			NodeResultType.Float => 1,
			NodeResultType.Vector2 => 2,
			NodeResultType.Vector3 => 3,
			NodeResultType.Vector4 => 4,
			NodeResultType.Color => 4,
			_ => 0
		};
		Code = code;
		Constant = constant;
	}

	public static NodeResult Error( params string[] errors ) => new() { Errors = errors };

	public static NodeResult MissingInput( string name ) => Error( $"Missing required input '{name}'." );

	/// <summary>
	/// "Cast" this result to different float types
	/// </summary>
	public string Cast( int components, float defaultValue = 0.0f )
	{
		if ( components > 4 )
		{
			throw new Exception( $"There is no float type with a component count of `{components}`" );
		}

		if ( ResultType == NodeResultType.Bool || ResultType == NodeResultType.Texture2D || ResultType == NodeResultType.TextureCube || ResultType == NodeResultType.Invalid )
		{
			throw new Exception( $"ResultType `{ResultType}` cannot be cast." );
		}

		if ( ResultType == NodeResultType.Int )
		{
			if ( Components == components )
			{
				return $"{Code}";
			}

			return $"float{components}( {string.Join( ", ", Enumerable.Repeat( Code, components ) )} )";
		}

		if ( Components == components )
			return Code;

		if ( Components > components )
		{
			return $"{Code}.{"xyzw"[..components]}";
		}
		else if ( Components == 1 )
		{
			return $"float{components}( {string.Join( ", ", Enumerable.Repeat( Code, components ) )} )";
		}
		else
		{
			if ( !string.IsNullOrWhiteSpace( Code ) )
				return $"float{components}( {Code}, {string.Join( ", ", Enumerable.Repeat( $"{defaultValue}", components - Components ) )} )";

			return $"float{components}( {string.Join( ", ", Enumerable.Repeat( $"{defaultValue}", components ) )} )";
		}
	}

	public override readonly string ToString()
	{
		return Code;
	}
}
