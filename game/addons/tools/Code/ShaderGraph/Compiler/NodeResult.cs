namespace Editor.ShaderGraph;

public enum NodeResultType
{
	Bool,
	Float,
	Vector2,
	Vector3,
	Vector4,
	[Obsolete( "Use NodeResultType.Vector4 instead", false )]
	Color,
	Invalid,
}

public struct NodeResult : IValid
{
	public delegate NodeResult Func( GraphCompiler compiler );
	public NodeResultType ResultType { get; private set; } = NodeResultType.Invalid;
	public string Code { get; private set; }
	public bool Constant { get; set; }
	public string[] Errors { get; private init; }
	public readonly bool IsValid => ResultType != NodeResultType.Invalid && !string.IsNullOrWhiteSpace( Code );

	public readonly string TypeName => ResultType switch
	{
		NodeResultType.Bool => "bool",
		NodeResultType.Float => "float",
		NodeResultType.Vector2 => "float2",
		NodeResultType.Vector3 => "float3",
		NodeResultType.Vector4 => "float4",
		_ => null
	};

	public int Components => ResultType switch
	{
		NodeResultType.Bool => 1,
		NodeResultType.Float => 1,
		NodeResultType.Vector2 => 2,
		NodeResultType.Vector3 => 3,
		NodeResultType.Vector4 => 4,
		_ => 1
	};

	public readonly Type ComponentType => ResultType switch
	{
		NodeResultType.Float => typeof( float ),
		NodeResultType.Vector2 => typeof( Vector2 ),
		NodeResultType.Vector3 => typeof( Vector3 ),
		NodeResultType.Vector4 => typeof( Vector4 ),
		_ => null,
	};

	[Obsolete( "Use NodeResult( NodeResultType resultType, string code, bool constant = false ) instead", false )]
	public NodeResult( int components, string code, bool constant = false )
	{
		ResultType = components switch
		{
			1 => NodeResultType.Float,
			2 => NodeResultType.Vector2,
			3 => NodeResultType.Vector3,
			4 => NodeResultType.Vector4,
			_ => NodeResultType.Invalid,
		};

		Code = code;
		Constant = constant;
	}

	public NodeResult( NodeResultType resultType, string code, bool constant = false )
	{
		ResultType = resultType;
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
		if ( ResultType == NodeResultType.Bool || ResultType == NodeResultType.Invalid )
		{
			throw new Exception( $"ResultType `{ResultType}` cannot be cast." );
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
