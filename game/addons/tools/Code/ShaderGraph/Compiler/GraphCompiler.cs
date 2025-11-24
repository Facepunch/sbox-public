using Editor.NodeEditor;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Editor.ShaderGraph;

public sealed partial class GraphCompiler
{
	public struct Error
	{
		public BaseNode Node;
		public string Message;
	}

	public static Dictionary<Type, string> ValueTypes => new()
	{
		{typeof(Color), "float4" },
		{typeof(Vector4), "float4" },
		{typeof(Vector3), "float3" },
		{typeof(Vector2), "float2" },
		{typeof(float), "float" },
		{typeof(bool), "bool" }
	};

	/// <summary>
	/// Current graph we're compiling
	/// </summary>
	public ShaderGraph Graph { get; private set; }

	private ShaderGraph Subgraph = null;
	private SubgraphNode SubgraphNode = null;
	private List<(SubgraphNode, ShaderGraph)> SubgraphStack = new();

	/// <summary>
	/// The loaded sub-graphs
	/// </summary>
	public List<ShaderGraph> Subgraphs { get; private set; }

	public HashSet<string> PixelIncludes { get; private set; } = new();
	public HashSet<string> VertexIncludes { get; private set; } = new();

	/// <summary>
	/// Is this compile for just the preview or not, preview uses attributes for constant values
	/// </summary>
	public bool IsPreview { get; private set; }
	public bool IsNotPreview => !IsPreview;

	private class CompileResult
	{
		public List<(NodeResult, NodeResult)> Results = new();
		public Dictionary<NodeInput, NodeResult> InputResults = new();
		public List<Sampler> SamplerStates = new();
		public Dictionary<string, TextureInput> TextureInputs = new();
		public Dictionary<string, (string Options, NodeResult Result)> Parameters = new();
		public Dictionary<string, string> Globals { get; private set; } = new();
		public Dictionary<string, object> Attributes { get; private set; } = new();
		public HashSet<string> Functions { get; private set; } = new();
		public string RepresentativeTexture { get; set; }
	}

	public enum ShaderStage
	{
		Vertex,
		Pixel,
	}

	public ShaderStage Stage { get; private set; }
	public bool IsVs => Stage == ShaderStage.Vertex;
	public bool IsPs => Stage == ShaderStage.Pixel;
	private string StageName => Stage == ShaderStage.Vertex ? "vs" : "ps";

	private string CurrentResultInput;

	private readonly CompileResult VertexResult = new();
	private readonly CompileResult PixelResult = new();
	private CompileResult ShaderResult => Stage == ShaderStage.Vertex ? VertexResult : PixelResult;

	public Action<string, object> OnAttribute { get; set; }

	public List<BaseNode> Nodes { get; private set; } = new();
	private List<NodeInput> InputStack = new();

	private readonly Dictionary<BaseNode, List<string>> NodeErrors = new();

	/// <summary>
	/// Error list, doesn't give you much information currently
	/// </summary>
	public IEnumerable<Error> Errors => NodeErrors
		.Select( x => new Error { Node = x.Key, Message = x.Value.FirstOrDefault() } );

	public GraphCompiler( ShaderGraph graph, bool preview )
	{
		Graph = graph;
		IsPreview = preview;
		Stage = ShaderStage.Pixel;
		Subgraphs = new();

		AddSubgraphs( Graph );
	}

	private void AddSubgraphs( ShaderGraph graph )
	{
		if ( graph != Graph )
		{
			if ( Subgraphs.Contains( graph ) )
				return;
			Subgraphs.Add( graph );
		}
		foreach ( var node in graph.Nodes )
		{
			if ( node is SubgraphNode subgraphNode )
			{
				AddSubgraphs( subgraphNode.Subgraph );
			}
		}
	}

	private static string CleanName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return "";

		name = name.Trim();
		name = new string( name.Where( x => char.IsLetter( x ) || char.IsNumber( x ) || x == '_' ).ToArray() );

		return name;
	}

	public void RegisterInclude( string path )
	{
		var list = IsVs ? VertexIncludes : PixelIncludes;
		if ( list.Contains( path ) )
			return;
		list.Add( path );
	}

	public string ResultFunction( string name, params string[] args )
	{
		if ( !ShaderTemplate.HasFunction( name ) )
			return null;

		var result = ShaderResult;
		if ( !result.Functions.Contains( name ) )
			result.Functions.Add( name );

		return $"{name}( {string.Join( ", ", args )} )";
	}

	public string RegisterFunction( string code, [CallerArgumentExpression( "code" )] string propertyName = "" )
	{
		if ( !ShaderTemplate.HasFunction( propertyName ) )
		{
			ShaderTemplate.RegisterFunction( propertyName, code );
		}
		return propertyName;
	}

	public void RegisterGlobal( string name, string global )
	{
		var result = ShaderResult;
		if ( result.Globals.ContainsKey( name ) )
			return;

		result.Globals.Add( name, global );
	}

	/// <summary>
	/// Register a texture and return the name of it
	/// </summary>
	public (string, int) ResultTexture( Sampler sampler, TextureInput input, Texture texture )
	{
		var name = CleanName( input.Name );
		name = string.IsNullOrWhiteSpace( name ) ? $"Texture_{StageName}_{ShaderResult.TextureInputs.Count}" : name;

		var id = name;
		int count = 0;

		var result = ShaderResult;

		while ( result.TextureInputs.ContainsKey( id ) )
		{
			id = $"{name}_{count++}";
		}

		if ( !result.SamplerStates.Contains( sampler ) )
			result.SamplerStates.Add( sampler );

		OnAttribute?.Invoke( id, texture );

		result.TextureInputs.Add( id, input );

		if ( CurrentResultInput == "Albedo" )
		{
			result.RepresentativeTexture = $"g_t{id}";
		}

		return new( $"g_t{id}", result.SamplerStates.IndexOf( sampler ) );
	}

	/// <summary>
	/// Get result of an input with an optional default value if it failed to resolve
	/// </summary>
	public NodeResult ResultOrDefault<T>( NodeInput input, T defaultValue )
	{
		var result = Result( input );
		return result.IsValid ? result : ResultValue( defaultValue );
	}

	/// <summary>
	/// Get result of an input
	/// </summary>
	public NodeResult Result( NodeInput input )
	{
		if ( !input.IsValid )
			return default;

		BaseNode node = null;
		if ( string.IsNullOrEmpty( input.Subgraph ) )
		{
			if ( Subgraph is not null )
			{
				var nodeId = string.Join( ',', SubgraphStack.Select( x => x.Item1.Identifier ) );
				return Result( new()
				{
					Identifier = input.Identifier,
					Output = input.Output,
					Subgraph = Subgraph.Path,
					SubgraphNode = nodeId
				} );
			}
			node = Graph.FindNode( input.Identifier );
		}
		else
		{
			var subgraph = Subgraphs.FirstOrDefault( x => x.Path == input.Subgraph );
			if ( subgraph is not null )
			{
				node = subgraph.FindNode( input.Identifier );
			}
		}
		if ( ShaderResult.InputResults.TryGetValue( input, out var result ) )
		{
			return result;
		}
		if ( node == null )
		{
			return default;
		}

		var nodeType = node.GetType();
		var property = nodeType.GetProperty( input.Output );
		if ( property == null )
		{
			// Search for alias
			var allProperties = nodeType.GetProperties();
			foreach ( var prop in allProperties )
			{
				var alias = prop.GetCustomAttribute<AliasAttribute>();
				if ( alias is null ) continue;
				foreach ( var al in alias.Value )
				{
					if ( al == input.Output )
					{
						property = prop;
						break;
					}
				}
				if ( property != null )
					break;
			}
		}

		object value = null;

		if ( node is not IRerouteNode && InputStack.Contains( input ) )
		{
			NodeErrors[node] = new List<string> { "Circular reference detected" };
			return default;
		}
		InputStack.Add( input );

		if ( Subgraph is not null && node.Graph != Subgraph )
		{
			if ( node.Graph != Graph )
			{
				Subgraph = node.Graph as ShaderGraph;
			}
			else
			{
				Subgraph = null;
			}
		}

		if ( node is SubgraphNode subgraphNode )
		{
			var newStack = (subgraphNode, Subgraph);
			var lastNode = SubgraphNode;
			SubgraphStack.Add( newStack );
			Subgraph = subgraphNode.Subgraph;
			SubgraphNode = subgraphNode;
			if ( !Subgraphs.Contains( Subgraph ) )
			{
				Subgraphs.Add( Subgraph );
			}

			var resultNode = Subgraph.Nodes.FirstOrDefault( x => x is FunctionResult ) as FunctionResult;
			var resultInput = resultNode.Inputs.FirstOrDefault( x => x.Identifier == input.Output );
			if ( resultInput?.ConnectedOutput is not null )
			{
				var nodeId = string.Join( ',', SubgraphStack.Select( x => x.Item1.Identifier ) );
				var newConnection = new NodeInput()
				{
					Identifier = resultInput.ConnectedOutput.Node.Identifier,
					Output = resultInput.ConnectedOutput.Identifier,
					Subgraph = Subgraph.Path,
					SubgraphNode = nodeId
				};
				var newResult = Result( newConnection );
				InputStack.Remove( input );
				SubgraphStack.RemoveAt( SubgraphStack.Count - 1 );
				Subgraph = newStack.Item2;
				SubgraphNode = lastNode;
				return newResult;
			}
			else
			{
				value = GetDefaultValue( subgraphNode, input.Output, resultInput.Type );
				SubgraphStack.RemoveAt( SubgraphStack.Count - 1 );
				Subgraph = newStack.Item2;
				SubgraphNode = lastNode;
			}

			//if ( value is null )
			//{
			//	value = GetDefaultValue( subgraphNode, input.Output, resultType );
			//}
		}
		else
		{
			if ( Subgraph is not null )
			{
				if ( node is SubgraphInput subgraphInput && !string.IsNullOrWhiteSpace( subgraphInput.InputName ) )
				{
					var newResult = ResolveSubgraphInput( subgraphInput, ref value );
					if ( newResult.IsValid )
					{
						InputStack.Remove( input );
						return newResult;
					}
				}
			}
			else if ( Graph.IsSubgraph )
			{
				if ( node is SubgraphInput subgraphInput )
				{
					if ( subgraphInput.PreviewInput.IsValid )
					{
						var paramResult = Result( subgraphInput.PreviewInput );
						InputStack.Remove( input );
						return paramResult;
					}
				}
			}

			if ( value is null )
			{
				if ( property == null )
				{
					InputStack.Remove( input );
					return default;
				}

				value = property.GetValue( node );
			}

			if ( value == null )
			{
				InputStack.Remove( input );
				return default;
			}
		}

		if ( value is NodeResult nodeResult )
		{
			InputStack.Remove( input );
			return nodeResult;
		}
		else if ( value is NodeInput nodeInput )
		{
			if ( nodeInput == input )
			{
				InputStack.Remove( input );
				return default;
			}
			var newResult = Result( nodeInput );
			InputStack.Remove( input );
			return newResult;
		}
		else if ( value is NodeResult.Func resultFunc )
		{
			var funcResult = resultFunc.Invoke( this );

			if ( !funcResult.IsValid )
			{
				if ( !NodeErrors.TryGetValue( node, out var errors ) )
				{
					errors = new();
					NodeErrors.Add( node, errors );
				}

				if ( funcResult.Errors is null || funcResult.Errors.Length == 0 )
				{
					errors.Add( $"Missing input" );
				}
				else
				{
					foreach ( var error in funcResult.Errors )
						errors.Add( error );
				}

				InputStack.Remove( input );
				return default;
			}

			// We can return this result without making it a local variable because it's constant
			if ( funcResult.Constant )
			{
				InputStack.Remove( input );
				return funcResult;
			}

			var id = ShaderResult.InputResults.Count;
			var varName = $"l_{id}";
			var localResult = new NodeResult( funcResult.Components, varName );

			ShaderResult.InputResults.Add( input, localResult );
			ShaderResult.Results.Add( (localResult, funcResult) );

			if ( IsPreview )
			{
				Nodes.Add( node );
			}

			InputStack.Remove( input );
			return localResult;
		}

		var resultVal = ResultValue( value );
		InputStack.Remove( input );
		return resultVal;
	}

	/// <summary>
	/// Get result of two inputs and cast to the largest component of the two (a float2 and float3 will both become float3 results)
	/// </summary>
	public (NodeResult, NodeResult) Result( NodeInput a, NodeInput b, float defaultA = 0.0f, float defaultB = 1.0f )
	{
		var resultA = ResultOrDefault( a, defaultA );
		var resultB = ResultOrDefault( b, defaultB );

		if ( resultA.Components == resultB.Components )
			return (resultA, resultB);

		if ( resultA.Components < resultB.Components )
			return (new( resultB.Components, resultA.Cast( resultB.Components ) ), resultB);

		return (resultA, new( resultA.Components, resultB.Cast( resultA.Components ) ));
	}

	/// <summary>
	/// Get result of a value that can be set in material editor
	/// </summary>
	public NodeResult ResultParameter<T>( string name, T value, T min = default, T max = default, bool isRange = false, bool isAttribute = false, ParameterUI ui = default )
	{
		if ( IsPreview || string.IsNullOrWhiteSpace( name ) || Subgraph is not null )
			return ResultValue( value );

		var attribName = name;
		name = CleanName( name );

		var prefix = (value is float) ? "g_fl" : (value is bool) ? "g_b" : "g_v";
		if ( !name.StartsWith( prefix ) )
			name = prefix + name;

		if ( ShaderResult.Parameters.TryGetValue( name, out var parameter ) )
			return parameter.Result;

		parameter.Result = ResultValue( value, name );

		var options = new StringWriter();

		// If we're an attribute, we don't care about the UI options
		if ( isAttribute )
		{
			options.Write( $"Attribute( \"{attribName}\" ); " );

			if ( value is bool boolValue )
			{
				options.Write( $"Default( {(boolValue ? 1 : 0)} ); " );
			}
			else
			{
				options.Write( $"Default{parameter.Result.Components}( {value} ); " );
			}
		}
		else
		{
			if ( ui.Type != UIType.Default )
			{
				options.Write( $"UiType( {ui.Type} ); " );
			}

			if ( ui.Step > 0.0f )
			{
				options.Write( $"UiStep( {ui.Step} ); " );
			}

			options.Write( $"UiGroup( \"{ui.UIGroup}\" ); " );

			if ( value is bool boolValue )
			{
				options.Write( $"Default( {(boolValue ? 1 : 0)} ); " );
			}
			else
			{
				options.Write( $"Default{parameter.Result.Components}( {value} ); " );
			}

			if ( parameter.Result.Components > 0 && isRange )
			{
				options.Write( $"Range{parameter.Result.Components}( {min}, {max} ); " );
			}
		}

		parameter.Options = options.ToString().Trim();

		ShaderResult.Parameters.Add( name, parameter );

		return parameter.Result;
	}

	/// <summary>
	/// Get result of a value, in preview mode an attribute will be registered and returned
	/// Only supports float, Vector2, Vector3, Vector4, Color, bool
	/// </summary>
	public NodeResult ResultValue<T>( T value, string name = null, bool previewOverride = false )
	{
		if ( value is NodeInput nodeInput ) return Result( nodeInput );

		bool isConstant = IsPreview && !previewOverride;
		bool isNamed = isConstant || !string.IsNullOrWhiteSpace( name );
		name = isConstant ? $"g_{StageName}_{ShaderResult.Attributes.Count}" : name;

		if ( isConstant )
		{
			OnAttribute?.Invoke( name, value );
			ShaderResult.Attributes[name] = value;
		}

		return value switch
		{
			float v => isNamed ? new NodeResult( 1, $"{name}" ) : new NodeResult( 1, $"{v}", true ),
			Vector2 v => isNamed ? new NodeResult( 2, $"{name}" ) : new NodeResult( 2, $"float2( {v.x}, {v.y} )" ),
			Vector3 v => isNamed ? new NodeResult( 3, $"{name}" ) : new NodeResult( 3, $"float3( {v.x}, {v.y}, {v.z} )" ),
			Vector4 v => isNamed ? new NodeResult( 4, $"{name}" ) : new NodeResult( 4, $"float4( {v.x}, {v.y}, {v.z}, {v.w} )" ),
			Color v => isNamed ? new NodeResult( 4, $"{name}" ) : new NodeResult( 4, $"float4( {v.r}, {v.g}, {v.b}, {v.a} )" ),
			bool v => isNamed ? new NodeResult( 0, $"{name}" ) : new NodeResult( 0, $"{v}" ),
			_ => throw new ArgumentException( "Unsupported attribute type", nameof( value ) )
		};
	}

	private static object GetDefaultValue( SubgraphNode node, string name, Type type )
	{
		if ( !node.DefaultValues.TryGetValue( name, out var value ) )
		{
			switch ( type )
			{
				case Type t when t == typeof( Vector2 ):
					return Vector2.Zero;
				case Type t when t == typeof( Vector3 ):
					return Vector3.Zero;
				case Type t when t == typeof( Vector4 ):
					return Vector4.Zero;
				case Type t when t == typeof( Color ):
					return Color.White;
				case Type t when t == typeof( int ):
					return 0;
				case Type t when t == typeof( float ):
					return 0.0f;
				case Type t when t == typeof( bool ):
					return false;
			}
		}
		if ( value is JsonElement el )
		{
			if ( type == typeof( float ) )
			{
				value = el.GetSingle();
			}
			else if ( type == typeof( Vector2 ) )
			{
				value = Vector2.Parse( el.GetString() );
			}
			else if ( type == typeof( Vector3 ) )
			{
				value = Vector3.Parse( el.GetString() );
			}
			else if ( type == typeof( Vector4 ) )
			{
				value = Vector4.Parse( el.GetString() );
			}
			else if ( type == typeof( Color ) )
			{
				value = Color.Parse( el.GetString() ) ?? Color.White;
			}
			else if ( type == typeof( bool ) )
			{
				value = el.GetBoolean();
			}
		}

		return value;
	}

	private NodeResult ResolveSubgraphInput( SubgraphInput node, ref object value )
	{
		var lastStack = SubgraphStack.LastOrDefault();
		var lastNodeEntered = lastStack.Item1;
		if ( lastNodeEntered is not null )
		{
			var parentInput = lastNodeEntered.InputReferences.FirstOrDefault( x => x.Key.Identifier == node.InputName );
			if ( parentInput.Key is not null )
			{
				var lastSubgraph = Subgraph;
				var lastNode = SubgraphNode;
				Subgraph = lastStack.Item2;
				SubgraphNode = (Subgraph is null) ? null : lastNodeEntered;
				SubgraphStack.RemoveAt( SubgraphStack.Count - 1 );

				var connectedPlug = parentInput.Key.ConnectedOutput;
				if ( connectedPlug is not null )
				{
					var nodeId = string.Join( ',', SubgraphStack.Select( x => x.Item1.Identifier ) );
					var newResult = Result( new()
					{
						Identifier = connectedPlug.Node.Identifier,
						Output = connectedPlug.Identifier,
						Subgraph = Subgraph?.Path,
						SubgraphNode = nodeId
					} );
					SubgraphStack.Add( lastStack );
					Subgraph = lastSubgraph;
					SubgraphNode = lastNode;
					return newResult;
				}
				else
				{
					value = GetDefaultValue( lastNodeEntered, node.InputName, parentInput.Value.Item2 );
					SubgraphStack.Add( lastStack );
					Subgraph = lastSubgraph;
					SubgraphNode = lastNode;
				}
			}
		}
		return new();
	}

	private NodeResult ResolveParameterNode( IParameterNode node, ref object value )
	{
		var lastStack = SubgraphStack.LastOrDefault();
		var lastNodeEntered = lastStack.Item1;
		if ( lastNodeEntered is not null )
		{
			var parentInput = lastNodeEntered.InputReferences.FirstOrDefault( x => x.Key.Identifier == node.Name );
			if ( parentInput.Key is not null )
			{
				var lastSubgraph = Subgraph;
				var lastNode = SubgraphNode;
				Subgraph = lastStack.Item2;
				SubgraphNode = (Subgraph is null) ? null : lastNodeEntered;
				SubgraphStack.RemoveAt( SubgraphStack.Count - 1 );

				var connectedPlug = parentInput.Key.ConnectedOutput;
				if ( connectedPlug is not null )
				{
					var nodeId = string.Join( ',', SubgraphStack.Select( x => x.Item1.Identifier ) );
					var newResult = Result( new()
					{
						Identifier = connectedPlug.Node.Identifier,
						Output = connectedPlug.Identifier,
						Subgraph = Subgraph?.Path,
						SubgraphNode = nodeId
					} );
					SubgraphStack.Add( lastStack );
					Subgraph = lastSubgraph;
					SubgraphNode = lastNode;
					return newResult;
				}
				else
				{
					value = GetDefaultValue( lastNodeEntered, node.Name, parentInput.Value.Item2 );
					SubgraphStack.Add( lastStack );
					Subgraph = lastSubgraph;
					SubgraphNode = lastNode;
				}
			}
		}
		return new();
	}

	private static int GetComponentCount( Type inputType )
	{
		return inputType switch
		{
			Type t when t == typeof( Vector4 ) || t == typeof( Color ) => 4,
			Type t when t == typeof( Vector3 ) => 3,
			Type t when t == typeof( Vector2 ) => 2,
			Type t when t == typeof( float ) => 1,
			_ => 0
		};
	}

	private static IEnumerable<PropertyInfo> GetNodeInputProperties( Type type )
	{
		return type.GetProperties( BindingFlags.Instance | BindingFlags.Public )
			.Where( property => property.GetSetMethod() != null &&
			property.PropertyType == typeof( NodeInput ) &&
			property.IsDefined( typeof( BaseNode.InputAttribute ), false ) );
	}

	private string GenerateCommon()
	{
		var sb = new StringBuilder();

		var blendMode = Graph.BlendMode;
		var alphaTest = blendMode == BlendMode.Masked ? 1 : 0;
		var translucent = blendMode == BlendMode.Translucent ? 1 : 0;

		sb.AppendLine( $"#ifndef S_ALPHA_TEST" );
		sb.AppendLine( $"#define S_ALPHA_TEST {alphaTest}" );
		sb.AppendLine( $"#endif" );

		sb.AppendLine( $"#ifndef S_TRANSLUCENT" );
		sb.AppendLine( $"#define S_TRANSLUCENT {translucent}" );
		sb.AppendLine( $"#endif" );

		return sb.ToString();
	}

	/// <summary>
	/// Generate shader code, will evaluate the graph if it hasn't already.
	/// Different code is generated for preview and not preview.
	/// </summary>
	public string Generate()
	{
		// May have already evaluated and there's errors
		if ( Errors.Any() )
			return null;

		var material = GenerateMaterial();
		var pixelOutput = GeneratePixelOutput();

		// If we have any errors after evaluating, no point going further
		if ( Errors.Any() )
			return null;

		return string.Format( ShaderTemplate.Code,
			Graph.Description,
			IndentString( GenerateCommon(), 1 ),
			IndentString( GenerateGlobals(), 1 ),
			IndentString( GenerateLocals(), 2 ),
			IndentString( material, 2 ),
			IndentString( GenerateVertex(), 2 ),
			IndentString( GenerateGlobals(), 1 ),
			IndentString( GenerateVertexComboRules(), 1 ),
			IndentString( GeneratePixelComboRules(), 1 ),
			IndentString( GenerateFunctions( PixelResult ), 1 ),
			IndentString( GenerateFunctions( VertexResult ), 1 ),
			IndentString( GeneratePixelInit(), 2 ),
			IndentString( pixelOutput, 2 )
		);
	}

	private static string GenerateFunctions( CompileResult result )
	{
		if ( !result.Functions.Any() )
			return null;

		var sb = new StringBuilder();
		foreach ( var function in result.Functions )
		{
			if ( ShaderTemplate.TryGetFunction( function, out var code ) )
			{
				sb.Append( code );
			}
		}

		return sb.ToString();
	}

	private static string IndentString( string input, int tabCount )
	{
		if ( string.IsNullOrWhiteSpace( input ) )
			return input;

		var tabs = new string( '\t', tabCount );
		var lines = input.Split( '\n' );

		for ( int i = 0; i < lines.Length; i++ )
		{
			lines[i] = tabs + lines[i];
		}

		return string.Join( "\n", lines );
	}

	private string GenerateVertexComboRules()
	{
		var sb = new StringBuilder();

		foreach ( var include in VertexIncludes )
		{
			sb.AppendLine( $"#include \"{include}\"" );
		}

		if ( IsNotPreview )
			return sb.ToString();

		sb.AppendLine();
		return sb.ToString();
	}

	private string GeneratePixelComboRules()
	{
		var sb = new StringBuilder();
		var pixelIncludes = new HashSet<string>( PixelIncludes );
		if ( Graph.Domain == ShaderDomain.PostProcess )
		{
			pixelIncludes.Add( "postprocess/functions.hlsl" );
			pixelIncludes.Add( "postprocess/common.hlsl" );
		}
		foreach ( var include in pixelIncludes )
		{
			sb.AppendLine( $"#include \"{include}\"" );
		}

		if ( !IsNotPreview )
		{
			sb.AppendLine();
			sb.AppendLine( "DynamicCombo( D_RENDER_BACKFACES, 0..1, Sys( ALL ) );" );
			sb.AppendLine( "RenderState( CullMode, D_RENDER_BACKFACES ? NONE : BACK );" );
		}
		else
		{
			sb.AppendLine( "RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );" );
		}

		return sb.ToString();
	}

	private string GeneratePixelInit()
	{
		Stage = ShaderStage.Pixel;
		if ( Graph.ShadingModel == ShadingModel.Lit && Graph.Domain != ShaderDomain.PostProcess )
			return ShaderTemplate.Material_init;
		return "";
	}

	private string GeneratePixelOutput()
	{
		Stage = ShaderStage.Pixel;
		Subgraph = null;
		SubgraphStack.Clear();

		if ( Graph.ShadingModel == ShadingModel.Unlit || Graph.Domain == ShaderDomain.PostProcess )
		{
			var resultNode = Graph.Nodes.OfType<BaseResult>().FirstOrDefault();
			if ( resultNode == null )
				return null;
			var albedoResult = resultNode.GetAlbedoResult( this );
			string albedo = albedoResult.Cast( GetComponentCount( typeof( Vector3 ) ) ) ?? "float3(1.00,1.00,1.00)";
			var opacityResult = resultNode.GetOpacityResult( this );
			string opacity = opacityResult.Cast( 1 ) ?? "1.00";
			return $"return float4( {albedo}, {opacity} );";
		}
		else if ( Graph.ShadingModel == ShadingModel.Lit )
		{

			return ShaderTemplate.Material_output;
		}
		return null;
	}

	private string GetPropertyValue( PropertyInfo property, Result resultNode )
	{
		NodeResult result;
		var inputAttribute = property.GetCustomAttribute<BaseNode.InputAttribute>();
		var componentCount = GetComponentCount( inputAttribute.Type );

		if ( property.GetValue( resultNode ) is NodeInput connection && connection.IsValid() )
		{
			result = Result( connection );
		}
		else
		{
			var editorAttribute = property.GetCustomAttribute<BaseNode.EditorAttribute>();
			if ( editorAttribute == null )
				return null;

			var valueProperty = resultNode.GetType().GetProperty( editorAttribute.ValueName );
			if ( valueProperty == null )
				return null;

			result = ResultValue( valueProperty.GetValue( resultNode ), previewOverride: true );
		}

		if ( Errors.Any() )
			return null;

		if ( !result.IsValid() )
			return null;

		if ( string.IsNullOrWhiteSpace( result.Code ) )
			return null;

		return $"{result.Cast( componentCount )}";
	}

	private string GenerateGlobals()
	{
		var sb = new StringBuilder();

		foreach ( var global in ShaderResult.Globals )
		{
			sb.AppendLine( global.Value );
		}

		for ( int i = 0; i < ShaderResult.SamplerStates.Count; ++i )
		{
			var sampler = ShaderResult.SamplerStates[i];
			sb.Append( $"SamplerState g_sSampler{i} <" )
			  .Append( $" Filter( {sampler.Filter.ToString().ToUpper()} );" )
			  .Append( $" AddressU( {sampler.AddressU.ToString().ToUpper()} );" )
			  .Append( $" AddressV( {sampler.AddressV.ToString().ToUpper()} ); >;" )
			  .AppendLine();
		}

		// Support for color buffer in post-process shaders
		if ( IsPs && Graph.Domain is ShaderDomain.PostProcess )
		{
			sb.AppendLine( "Texture2D g_tColorBuffer < Attribute( \"ColorBuffer\" ); SrgbRead ( true ); >;" );
		}

		if ( IsPreview )
		{
			foreach ( var result in ShaderResult.TextureInputs )
			{
				sb.Append( $"{result.Value.CreateTexture( result.Key )} <" )
				  .Append( $" Attribute( \"{result.Key}\" );" )
				  .Append( $" SrgbRead( {result.Value.SrgbRead} ); >;" )
				  .AppendLine();
			}

			foreach ( var result in ShaderResult.Attributes )
			{
				var typeName = result.Value switch
				{
					Color _ => "float4",
					Vector4 _ => "float4",
					Vector3 _ => "float3",
					Vector2 _ => "float2",
					float _ => "float",
					bool _ => "bool",
					_ => null
				};

				sb.AppendLine( $"{typeName} {result.Key} < Attribute( \"{result.Key}\" ); >;" );
			}

			sb.AppendLine( "float g_flPreviewTime < Attribute( \"g_flPreviewTime\" ); >;" );
			sb.AppendLine( $"int g_iStageId < Attribute( \"g_iStageId\" ); >;" );
		}
		else
		{
			foreach ( var result in ShaderResult.TextureInputs )
			{
				// If we're an attribute, we don't care about texture inputs
				if ( result.Value.IsAttribute )
					continue;

				var defaultTex = result.Value.DefaultTexture;
				sb.Append( $"{result.Value.CreateInput}( {result.Key}, {result.Value.ColorSpace}, 8," )
				  .Append( $" \"{result.Value.Processor.ToString()}\"," )
				  .Append( $" \"_{result.Value.ExtensionString.ToLower()}\"," )
				  .Append( $" \"{result.Value.UIGroup}\"," )
				  .Append( string.IsNullOrEmpty( defaultTex )
							? $" Default4( {result.Value.Default} ) );"
							: $" DefaultFile( \"{defaultTex}\" ) );" )
				  .AppendLine();
			}

			foreach ( var result in ShaderResult.TextureInputs )
			{
				// If we're an attribute, we don't care about the UI options
				if ( result.Value.IsAttribute )
				{
					sb.AppendLine( $"{result.Value.CreateTexture( result.Key )} < Attribute( \"{result.Key}\" ); >;" );
				}
				else
				{
					sb.Append( $"{result.Value.CreateTexture( result.Key )} < Channel( RGBA, Box( {result.Key} ), {(result.Value.SrgbRead ? "Srgb" : "Linear")} );" )
					  .Append( $" OutputFormat( {result.Value.ImageFormat} );" )
					  .Append( $" SrgbRead( {result.Value.SrgbRead} ); >;" )
					  .AppendLine();
				}
			}

			if ( !string.IsNullOrWhiteSpace( ShaderResult.RepresentativeTexture ) )
			{
				sb.AppendLine( $"TextureAttribute( LightSim_DiffuseAlbedoTexture, {ShaderResult.RepresentativeTexture} )" );
				sb.AppendLine( $"TextureAttribute( RepresentativeTexture, {ShaderResult.RepresentativeTexture} )" );
			}

			foreach ( var parameter in ShaderResult.Parameters )
			{
				sb.AppendLine( $"{parameter.Value.Result.TypeName} {parameter.Key} < {parameter.Value.Options} >;" );
			}
		}

		if ( sb.Length > 0 )
		{
			sb.Insert( 0, "\n" );
		}

		return sb.ToString();
	}

	private string GenerateLocals()
	{
		var sb = new StringBuilder();

		if ( ShaderResult.Results.Any() )
		{
			sb.AppendLine();
		}

		if ( IsPreview )
		{
			int localId = 1;

			foreach ( var result in ShaderResult.Results )
			{
				sb.AppendLine( $"{result.Item2.TypeName} {result.Item1} = {result.Item2.Code};" );
				sb.AppendLine( $"if ( g_iStageId == {localId++} ) return {result.Item1.Cast( 4, 1.0f )};" );
			}
		}
		else
		{
			foreach ( var result in ShaderResult.Results )
			{
				sb.AppendLine( $"{result.Item2.TypeName} {result.Item1} = {result.Item2.Code};" );
			}
		}

		return sb.ToString();
	}

	private string GenerateMaterial()
	{
		Stage = ShaderStage.Pixel;
		Subgraph = null;
		SubgraphStack.Clear();
		if ( Graph.ShadingModel != ShadingModel.Lit || Graph.Domain == ShaderDomain.PostProcess ) return "";

		var resultNode = Graph.Nodes.OfType<BaseResult>().FirstOrDefault();
		if ( resultNode == null )
			return null;

		var sb = new StringBuilder();
		var visited = new HashSet<string>();

		foreach ( var property in GetNodeInputProperties( resultNode.GetType() ) )
		{
			if ( property.Name == "PositionOffset" )
				continue;

			CurrentResultInput = property.Name;
			visited.Add( property.Name );

			NodeResult result;

			if ( property.GetValue( resultNode ) is NodeInput connection && connection.IsValid() )
			{
				result = Result( connection );
			}
			else
			{
				var editorAttribute = property.GetCustomAttribute<BaseNode.EditorAttribute>();
				if ( editorAttribute == null )
					continue;

				var valueProperty = resultNode.GetType().GetProperty( editorAttribute.ValueName );
				if ( valueProperty == null )
					continue;

				result = ResultValue( valueProperty.GetValue( resultNode ) );
			}

			if ( Errors.Any() )
				return null;

			if ( !result.IsValid() )
				continue;

			if ( string.IsNullOrWhiteSpace( result.Code ) )
				continue;

			var inputAttribute = property.GetCustomAttribute<BaseNode.InputAttribute>();
			var componentCount = GetComponentCount( inputAttribute.Type );

			sb.AppendLine( $"m.{property.Name} = {result.Cast( componentCount )};" );
		}

		if ( resultNode is FunctionResult functionResult )
		{
			functionResult.AddMaterialOutputs( this, sb, visited );
		}

		visited.Clear();

		CurrentResultInput = null;

		return sb.ToString();
	}

	private string GenerateVertex()
	{
		Stage = ShaderStage.Vertex;

		var resultNode = Graph.Nodes.OfType<BaseResult>().FirstOrDefault();
		if ( resultNode == null )
			return null;

		var positionOffsetInput = resultNode.GetPositionOffset();

		var sb = new StringBuilder();

		switch ( Graph.Domain )
		{
			case ShaderDomain.Surface:
				sb.AppendLine( @"
PixelInput i = ProcessVertex( v );
i.vPositionOs = v.vPositionOs.xyz;
i.vColor = v.vColor;

ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
i.vTintColor = extraShaderData.vTint;

VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );" );
				break;
			case ShaderDomain.PostProcess:
				sb.AppendLine( @"
PixelInput i;
i.vPositionPs = float4(v.vPositionOs.xy, 0.0f, 1.0f );
i.vPositionWs = float3(v.vTexCoord, 0.0f);
" );
				break;
		}

		NodeResult result;

		if ( positionOffsetInput is NodeInput connection && connection.IsValid() )
		{
			result = Result( connection );

			if ( !Errors.Any() && result.IsValid() && !string.IsNullOrWhiteSpace( result.Code ) )
			{
				var componentCount = GetComponentCount( typeof( Vector3 ) );

				sb.AppendLine();

				foreach ( var local in ShaderResult.Results )
				{
					sb.AppendLine( $"{local.Item2.TypeName} {local.Item1} = {local.Item2.Code};" );
				}

				sb.AppendLine( $"i.vPositionWs.xyz += {result.Cast( componentCount )};" );
				sb.AppendLine( "i.vPositionPs.xyzw = Position3WsToPs( i.vPositionWs.xyz );" );
			}
		}

		switch ( Graph.Domain )
		{
			case ShaderDomain.Surface:
				sb.AppendLine( "return FinalizeVertex( i );" );
				break;
			case ShaderDomain.PostProcess:
				sb.AppendLine( "return i;" );
				break;
		}

		return sb.ToString();
	}
}
