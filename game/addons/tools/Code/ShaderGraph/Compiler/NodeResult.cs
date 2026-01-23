namespace Editor.ShaderGraph;

public enum NodeResultType
{
	Bool,
	Int,
	Float,
	Vector2,
	Vector3,
	Vector4,
	Color
}

public struct NodeResult : IValid
{
	public delegate NodeResult Func( GraphCompiler compiler );

	public string Code { get; private set; }
	public int Components { get; private set; }
	public bool Constant { get; set; }
	public string[] Errors { get; private init; }

	public readonly bool IsValid => Components > 0 && !string.IsNullOrWhiteSpace( Code );

	public readonly string TypeName => Components > 1 ? $"float{Components}" : Components == 0 ? "bool" : "float";

	public readonly Type ComponentType => Components switch
	{
		1 => typeof( float ),
		2 => typeof( Vector2 ),
		3 => typeof( Vector3 ),
		4 => typeof( Color ),
		_ => null,
	};

	public NodeResult( int components, string code, bool constant = false )
	{
		Components = components;
		Code = code;
		Constant = constant;
	}

	public NodeResult( NodeResultType type, string code, bool constant = false )
	{
		Components = type switch
		{
			NodeResultType.Bool => 1,
			NodeResultType.Float => 1,
			NodeResultType.Vector2 => 2,
			NodeResultType.Vector3 => 3,
			NodeResultType.Color => 4,
			_ => 1
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
