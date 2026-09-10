using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;

namespace Editor.Mcp;

/// <summary>
/// Finds and invokes <see cref="McpToolAttribute"/> methods. Discovery goes through
/// EditorTypeLibrary, so tools defined in addons and hotloaded assemblies appear and
/// disappear automatically - nothing registers by hand.
/// </summary>
internal static class ToolRegistry
{
	static readonly Logger log = new( "MCP" );

	/// <summary>
	/// How long a queued call waits for the main thread to pick it up before reporting the
	/// editor as blocked. Only applies before the tool starts - running tools never time out.
	/// </summary>
	static readonly TimeSpan PickupTimeout = TimeSpan.FromSeconds( 30 );

	/// <summary>
	/// Every available tool, sorted by name. Duplicate names get skipped with a warning.
	/// </summary>
	public static IEnumerable<(string Name, MethodDescription Method)> All()
	{
		var found = new SortedDictionary<string, MethodDescription>( StringComparer.Ordinal );

		foreach ( var (method, attribute) in EditorTypeLibrary.GetMethodsWithAttribute<McpToolAttribute>() )
		{
			var name = attribute.Name ?? ToSnakeCase( method.Name );

			if ( !found.TryAdd( name, method ) )
			{
				log.Warning( $"Duplicate MCP tool name '{name}' ({method.TypeDescription?.FullName}.{method.Name}) - skipping" );
			}
		}

		return found.Select( x => (x.Key, x.Value) );
	}

	/// <summary>
	/// The tools/list result - only <see cref="McpListedAttribute"/> tools appear here. The
	/// rest of the registry is reached through search_tools and call_tool, so the list never
	/// goes stale as code hotloads.
	/// </summary>
	public static JsonNode ListJson()
	{
		var tools = new JsonArray();

		foreach ( var (name, method) in All() )
		{
			if ( method.GetCustomAttribute<McpListedAttribute>() is null )
				continue;

			tools.Add( ToolJson( name, method ) );
		}

		return new JsonObject { ["tools"] = tools };
	}

	/// <summary>
	/// The toolset a tool belongs to - its class's <see cref="McpToolsetAttribute"/>, or a
	/// name derived from the class name when there isn't one (AssetTools becomes "asset").
	/// </summary>
	internal static (string Name, string Description) ToolsetOf( MethodDescription method )
	{
		if ( method.TypeDescription?.TargetType?.GetCustomAttribute<McpToolsetAttribute>() is { } attribute )
			return (attribute.Name, attribute.Description);

		var name = method.TypeDescription?.Name ?? "other";

		if ( name.EndsWith( "Tools", StringComparison.Ordinal ) && name.Length > 5 )
			name = name[..^5];

		return (ToSnakeCase( name ), null);
	}

	/// <summary>
	/// One tool as it appears in tools/list - name, toolset, description and a json schema
	/// built from its parameters.
	/// </summary>
	public static JsonObject ToolJson( string name, MethodDescription method )
	{
		var properties = new JsonObject();
		var required = new JsonArray();

		foreach ( var parameter in method.Parameters )
		{
			properties[parameter.Name] = SchemaFor( parameter );

			if ( !parameter.HasDefaultValue )
			{
				required.Add( parameter.Name );
			}
		}

		var json = new JsonObject
		{
			["name"] = name,
			["title"] = method.Title,
			["toolset"] = ToolsetOf( method ).Name,
			["description"] = method.Description ?? method.Title,
			["inputSchema"] = new JsonObject
			{
				["type"] = "object",
				["properties"] = properties,
				["required"] = required
			}
		};

		var hints = method.GetCustomAttribute<McpToolAttribute>()?.Hints ?? McpToolHints.None;

		// No hints means no annotations - the protocol's defaults already assume a
		// destructive write, which is the right way for clients to treat unknown tools
		if ( hints.HasFlag( McpToolHints.ReadOnly ) )
		{
			json["annotations"] = new JsonObject { ["readOnlyHint"] = true };
		}

		if ( DeclaredResultType( method ) is { } resultType )
		{
			json["outputSchema"] = SchemaForProperties( resultType, 0 );
		}

		return json;
	}

	/// <summary>
	/// The data type a tool declares it returns - its Task unwrapped, null when the return is
	/// untyped (object, McpResult, Bitmap) and no shape can be promised. Declaring a data type
	/// gets a tool an output schema and structured content for free.
	/// </summary>
	static Type DeclaredResultType( MethodDescription method )
	{
		var type = method.ReturnType;

		if ( type is null || type == typeof( Task ) )
			return null;

		if ( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( Task<> ) )
			type = type.GetGenericArguments()[0];

		if ( typeof( McpResult ).IsAssignableFrom( type ) || type == typeof( Bitmap ) )
			return null;

		// The protocol requires outputSchema to be an object - wrap lists in a result type
		if ( type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof( List<> )) )
			return null;

		return IsReflectable( type ) ? type : null;
	}

	/// <summary>
	/// Invoke a tool on the main thread and shape whatever it returns into an MCP tool result.
	/// </summary>
	public static async Task<JsonNode> Call( string name, JsonObject arguments )
	{
		try
		{
			return Result( await Invoke( name, arguments ) );
		}
		catch ( Exception e )
		{
			// Everything from tool lookup onward goes in-band - unknown tools, arguments that
			// don't bind, execution failures. The agent reads what went wrong and self corrects,
			// which is what protocol revision 2025-11-25 asks for validation errors too.
			return Result( Describe( e ), isError: true );
		}
	}

	/// <summary>
	/// Find a tool, bind its arguments and run it on the main thread, returning the method's
	/// raw return value.
	/// </summary>
	public static async Task<object> Invoke( string name, JsonObject arguments )
	{
		name = name?.Trim() ?? "";

		var tools = All().ToList();

		var method = tools.FirstOrDefault( x => x.Name == name ).Method
			?? tools.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) ).Method
			?? throw new McpException( JsonRpcError.InvalidParams, UnknownToolMessage( name, tools ) );

		var args = BindArguments( method, arguments );

		var value = await InvokeOnMainThread( name, method, args );

		// Tools declaring a data return type promised the shape in their output schema,
		// so the client gets the value as structured content it can bind against it
		if ( value is not null && DeclaredResultType( method ) is not null )
			return McpResult.Structured( value );

		return value;
	}

	static string UnknownToolMessage( string name, List<(string Name, MethodDescription Method)> tools )
	{
		var suggestions = tools
			.Select( x => x.Name )
			.Where( x => LevenshteinDistance( x, name ) <= 2
				|| (name.Length >= 3 && x.Contains( name, StringComparison.OrdinalIgnoreCase ))
				|| (x.Length >= 3 && name.Contains( x, StringComparison.OrdinalIgnoreCase )) )
			.Take( 5 )
			.ToArray();

		var suggest = suggestions.Length > 0 ? $" Did you mean {string.Join( " or ", suggestions )}?" : "";

		return $"Unknown tool '{name}'.{suggest} Find tools with search_tools - the registry changes as code hotloads.";
	}

	static int LevenshteinDistance( string a, string b )
	{
		if ( Math.Abs( a.Length - b.Length ) > 3 )
			return int.MaxValue;

		var previous = new int[b.Length + 1];
		var current = new int[b.Length + 1];

		for ( int j = 0; j <= b.Length; j++ ) previous[j] = j;

		for ( int i = 1; i <= a.Length; i++ )
		{
			current[0] = i;

			for ( int j = 1; j <= b.Length; j++ )
			{
				var cost = char.ToLowerInvariant( a[i - 1] ) == char.ToLowerInvariant( b[j - 1] ) ? 0 : 1;
				current[j] = Math.Min( Math.Min( current[j - 1] + 1, previous[j] + 1 ), previous[j - 1] + cost );
			}

			(previous, current) = (current, previous);
		}

		return previous[b.Length];
	}

	static object[] BindArguments( MethodDescription method, JsonObject arguments )
	{
		// A name we don't know is a typo or a schema misread - failing loudly beats the
		// silent wrong behaviour of ignoring it
		if ( arguments is not null )
		{
			foreach ( var (key, _) in arguments )
			{
				if ( !method.Parameters.Any( x => string.Equals( x.Name, key, StringComparison.OrdinalIgnoreCase ) ) )
				{
					throw new McpException( JsonRpcError.InvalidParams, $"Unknown argument '{key}' - this tool takes {DescribeParameters( method )}" );
				}
			}
		}

		var args = new object[method.Parameters.Length];

		for ( int i = 0; i < method.Parameters.Length; i++ )
		{
			var parameter = method.Parameters[i];

			if ( TryGetArgument( arguments, parameter.Name, out var node ) )
			{
				args[i] = ApplyRange( parameter, ConvertArgument( method, parameter, node ) );
			}
			else if ( parameter.HasDefaultValue )
			{
				args[i] = parameter.DefaultValue;
			}
			else
			{
				throw new McpException( JsonRpcError.InvalidParams, $"Missing required argument '{parameter.Name}' - this tool takes {DescribeParameters( method )}" );
			}
		}

		return args;
	}

	/// <summary>
	/// A [Range] on a parameter clamps out of range values instead of erroring - agents
	/// overshoot limits, and that shouldn't fail the call.
	/// </summary>
	static object ApplyRange( ParameterInfo parameter, object value )
	{
		if ( parameter.GetCustomAttribute<RangeAttribute>() is not { } range )
			return value;

		return value switch
		{
			int i => (int)Math.Clamp( i, range.Min, range.Max ),
			long l => (long)Math.Clamp( l, range.Min, range.Max ),
			float f => Math.Clamp( f, range.Min, range.Max ),
			double d => (double)Math.Clamp( (float)d, range.Min, range.Max ),
			_ => value
		};
	}

	static bool TryGetArgument( JsonObject arguments, string name, out JsonNode node )
	{
		node = null;

		if ( arguments is null )
			return false;

		if ( arguments.TryGetPropertyValue( name, out node ) )
			return true;

		foreach ( var (key, value) in arguments )
		{
			if ( string.Equals( key, name, StringComparison.OrdinalIgnoreCase ) )
			{
				node = value;
				return true;
			}
		}

		return false;
	}

	static object ConvertArgument( MethodDescription method, ParameterInfo parameter, JsonNode node )
	{
		var type = parameter.ParameterType;

		if ( node is null )
		{
			if ( !type.IsValueType || Nullable.GetUnderlyingType( type ) is not null )
				return null;

			throw new McpException( JsonRpcError.InvalidParams, $"Argument '{parameter.Name}' is null but must be {TypeLabel( type )}" );
		}

		// Node typed parameters take the value as sent - unwrapping the common client bug
		// of json arriving encoded inside a string
		if ( typeof( JsonNode ).IsAssignableFrom( type ) )
		{
			if ( !type.IsInstanceOfType( node ) && TryParseJson( node, out var unwrapped ) && type.IsInstanceOfType( unwrapped ) )
			{
				node = unwrapped;
			}

			if ( type.IsInstanceOfType( node ) )
				return node;

			throw ConversionError( method, parameter, node );
		}

		string detail = null;

		try
		{
			return Json.FromNode( node, type );
		}
		catch ( Exception e )
		{
			detail = e.Message;
		}

		// Json encoded inside a string - "5", "true", "[1,2]" - gets unwrapped and retried
		if ( TryParseJson( node, out var parsed ) )
		{
			try
			{
				return Json.FromNode( parsed, type );
			}
			catch ( Exception )
			{
			}
		}

		// Enum names in the wrong case still count
		if ( type.IsEnum && node is JsonValue enumValue && enumValue.TryGetValue<string>( out var enumName )
			&& Enum.TryParse( type, enumName, ignoreCase: true, out var parsedEnum ) )
		{
			return parsedEnum;
		}

		// Anything can become a string parameter
		if ( type == typeof( string ) )
		{
			return node is JsonValue ? node.ToString() : node.ToJsonString();
		}

		throw ConversionError( method, parameter, node, detail );
	}

	static bool TryParseJson( JsonNode node, out JsonNode parsed )
	{
		parsed = null;

		if ( node is not JsonValue value || !value.TryGetValue<string>( out var text ) )
			return false;

		try
		{
			parsed = JsonNode.Parse( text );
			return parsed is not null;
		}
		catch ( Exception )
		{
			return false;
		}
	}

	static McpException ConversionError( MethodDescription method, ParameterInfo parameter, JsonNode node, string detail = null )
	{
		var received = node.ToJsonString();

		if ( received.Length > 100 )
			received = received[..100] + "...";

		// The serializer's message says what didn't bind, minus its "Path: $..." locator noise
		var reason = string.IsNullOrEmpty( detail ) ? "" : $" ({detail.Split( " Path: " )[0].TrimEnd()})";

		return new McpException( JsonRpcError.InvalidParams,
			$"Couldn't convert argument '{parameter.Name}' - expected {TypeLabel( parameter.ParameterType )}, got {received}.{reason} This tool takes {DescribeParameters( method )}" );
	}

	/// <summary>
	/// A tool's parameters as agents should read them in errors - "message (string), count (integer, optional)".
	/// </summary>
	static string DescribeParameters( MethodDescription method )
	{
		if ( method.Parameters.Length == 0 )
			return "no arguments";

		return string.Join( ", ", method.Parameters.Select( x =>
			$"{x.Name} ({TypeLabel( x.ParameterType )}{(x.HasDefaultValue ? ", optional" : "")})" ) );
	}

	static string TypeLabel( Type type )
	{
		type = Nullable.GetUnderlyingType( type ) ?? type;

		if ( type == typeof( string ) ) return "string";
		if ( type == typeof( bool ) ) return "boolean";
		if ( type.IsEnum ) return $"one of {string.Join( ", ", Enum.GetNames( type ) )}";

		if ( IsIntegerType( type ) ) return "integer";
		if ( IsNumberType( type ) ) return "number";

		if ( type == typeof( JsonObject ) ) return "json object";
		if ( type == typeof( JsonArray ) ) return "json array";
		if ( typeof( JsonNode ).IsAssignableFrom( type ) ) return "json";

		if ( type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof( List<> )) )
			return "array";

		return type.Name;
	}

	/// <summary>
	/// A node as a json object, tolerating objects that arrive encoded inside a json string.
	/// Null when it's neither.
	/// </summary>
	internal static JsonObject AsJsonObject( JsonNode node )
	{
		if ( node is JsonObject obj )
			return obj;

		if ( TryParseJson( node, out var parsed ) && parsed is JsonObject unwrapped )
			return unwrapped;

		return null;
	}

	/// <summary>
	/// Tool call arguments as a json object - null passes through, anything else must be an
	/// object or a string containing one.
	/// </summary>
	internal static JsonObject AsArguments( JsonNode node, string context )
	{
		if ( node is null )
			return null;

		return AsJsonObject( node )
			?? throw new McpException( JsonRpcError.InvalidParams, $"{context} must be a json object like {{\"key\": value}}" );
	}

	static async Task<object> InvokeOnMainThread( string name, MethodDescription method, object[] args )
	{
		var completion = new TaskCompletionSource<object>( TaskCreationOptions.RunContinuationsAsynchronously );
		var started = 0;

		MainThread.Queue( () =>
		{
			Interlocked.Exchange( ref started, 1 );

			try
			{
				completion.SetResult( method.Invoke( null, args ) );
			}
			catch ( TargetInvocationException e )
			{
				completion.SetException( e.InnerException ?? e );
			}
			catch ( Exception e )
			{
				completion.SetException( e );
			}
		} );

		// A call the main thread never picks up means the editor is stuck - probably a modal
		// dialog or a long blocking operation. Saying so beats letting the client time out silently.
		if ( await Task.WhenAny( completion.Task, Task.Delay( PickupTimeout ) ) != completion.Task
			&& Interlocked.CompareExchange( ref started, 0, 0 ) == 0 )
		{
			// The call still runs when the editor unblocks, but nobody is listening for the
			// result anymore - if it fails, put that in the console where read_console finds it
			_ = LogLateFailure( name, completion.Task );

			throw new TimeoutException( $"The editor main thread didn't pick this call up within {PickupTimeout.TotalSeconds:0} seconds - it's probably blocked by a modal dialog or a long operation. The call stays queued and runs when the editor unblocks, but its result won't be reported." );
		}

		var value = await completion.Task;

		// tools are allowed to be async
		if ( value is Task task )
		{
			await task;

			var result = task.GetType().GetProperty( "Result" );
			value = result?.PropertyType.Name == "VoidTaskResult" ? null : result?.GetValue( task );
		}

		return value;
	}

	static async Task LogLateFailure( string name, Task<object> pending )
	{
		try
		{
			// Async tools hand back a task - their failure happens inside it
			if ( await pending is Task inner )
				await inner;
		}
		catch ( Exception e )
		{
			log.Warning( $"'{name}' ran after the pickup timeout and failed: {e.Message}" );
		}
	}

	/// <summary>
	/// An exception as the agent should read it - type, message and enough stack to find the throw.
	/// </summary>
	internal static string Describe( Exception e )
	{
		// Our own errors are written for the agent already - the type name and stack are noise
		if ( e is McpException )
			return e.Message;

		var text = string.IsNullOrWhiteSpace( e.Message ) ? e.GetType().Name : $"{e.GetType().Name}: {e.Message}";

		// Our own timeout explains itself, a stack would just be async plumbing
		if ( e is TimeoutException )
			return text;

		var frames = e.StackTrace?.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).Take( 4 ).ToArray();

		if ( frames is { Length: > 0 } )
		{
			text += "\n" + string.Join( "\n", frames );
		}

		return text;
	}

	internal static JsonNode Result( object value, bool isError = false )
	{
		return Shape( value ).ToJson( isError );
	}

	/// <summary>
	/// Shape a tool's raw return value into an <see cref="McpResult"/>.
	/// </summary>
	internal static McpResult Shape( object value ) => value switch
	{
		McpResult r => r,
		null => McpResult.Text( "ok" ),
		Bitmap bitmap => McpResult.Image( bitmap ),
		_ => McpResult.Text( value )
	};

	static JsonObject SchemaFor( ParameterInfo parameter )
	{
		var schema = SchemaForType( parameter.ParameterType );

		if ( parameter.GetCustomAttribute<DescriptionAttribute>() is { Value: not null } description )
		{
			schema["description"] = description.Value;
		}

		if ( parameter.GetCustomAttribute<RangeAttribute>() is { } range )
		{
			schema["minimum"] = range.Min;
			schema["maximum"] = range.Max;
		}

		if ( parameter.HasDefaultValue && DefaultToNode( parameter.DefaultValue ) is { } fallback )
		{
			schema["default"] = fallback;
		}

		return schema;
	}

	/// <summary>
	/// A parameter's default value as a schema node. Null and empty strings would document
	/// nothing, so they emit nothing.
	/// </summary>
	static JsonNode DefaultToNode( object value ) => value switch
	{
		null or "" => null,
		Enum e => e.ToString(),
		string s => s,
		bool b => b,
		byte or sbyte or short or ushort or int or uint or long or ulong => JsonValue.Create( Convert.ToInt64( value ) ),
		float or double or decimal => JsonValue.Create( Convert.ToDouble( value ) ),
		_ => null
	};

	static JsonObject SchemaForType( Type type, int depth = 0 )
	{
		type = Nullable.GetUnderlyingType( type ) ?? type;

		if ( type == typeof( string ) ) return new JsonObject { ["type"] = "string" };
		if ( type == typeof( bool ) ) return new JsonObject { ["type"] = "boolean" };

		if ( type.IsEnum )
		{
			return new JsonObject
			{
				["type"] = "string",
				["enum"] = new JsonArray( Enum.GetNames( type ).Select( x => (JsonNode)x ).ToArray() )
			};
		}

		if ( IsIntegerType( type ) )
		{
			return new JsonObject { ["type"] = "integer" };
		}

		if ( IsNumberType( type ) )
		{
			return new JsonObject { ["type"] = "number" };
		}

		if ( type == typeof( JsonObject ) )
		{
			return new JsonObject { ["type"] = "object" };
		}

		if ( type == typeof( JsonArray ) )
		{
			return new JsonObject { ["type"] = "array" };
		}

		if ( typeof( JsonNode ).IsAssignableFrom( type ) )
		{
			// any shape
			return new JsonObject();
		}

		if ( type == typeof( Guid ) || type == typeof( DateTime ) || type == typeof( DateTimeOffset )
			|| type == typeof( TimeSpan ) || type == typeof( char ) )
		{
			return new JsonObject { ["type"] = "string" };
		}

		// Engine math types serialize through their json converters as comma strings - "x,y,z"
		if ( type == typeof( Vector2 ) || type == typeof( Vector3 ) || type == typeof( Vector4 )
			|| type == typeof( Vector2Int ) || type == typeof( Vector3Int )
			|| type == typeof( Angles ) || type == typeof( Rotation ) )
		{
			return new JsonObject { ["type"] = "string" };
		}

		if ( type.IsArray )
		{
			return new JsonObject { ["type"] = "array", ["items"] = SchemaForType( type.GetElementType(), depth + 1 ) };
		}

		if ( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( List<> ) )
		{
			return new JsonObject { ["type"] = "array", ["items"] = SchemaForType( type.GetGenericArguments()[0], depth + 1 ) };
		}

		if ( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( Dictionary<,> ) && type.GetGenericArguments()[0] == typeof( string ) )
		{
			return new JsonObject { ["type"] = "object", ["additionalProperties"] = SchemaForType( type.GetGenericArguments()[1], depth + 1 ) };
		}

		// Plain data types from tool code describe themselves - every public settable property
		// becomes a schema property. The depth cap stops self referencing types recursing forever.
		if ( depth < 4 && IsReflectable( type ) )
		{
			return SchemaForProperties( type, depth );
		}

		// Everything else goes through the engine's json converters, so we can't promise a shape
		return new JsonObject();
	}

	/// <summary>
	/// Whether a type is a plain data type whose properties describe its json shape. Engine and
	/// framework types don't count - they mostly serialize through custom converters, so a
	/// guessed shape would be wrong more often than right.
	/// </summary>
	static bool IsReflectable( Type type )
	{
		if ( type.IsAbstract || type.IsInterface || type.IsPrimitive || typeof( Delegate ).IsAssignableFrom( type ) )
			return false;

		if ( type.GetCustomAttribute<JsonConverterAttribute>() is not null )
			return false;

		if ( type.Assembly == typeof( Vector3 ).Assembly || type.Assembly == typeof( Json ).Assembly )
			return false;

		return type.Namespace?.StartsWith( "System", StringComparison.Ordinal ) != true;
	}

	static JsonObject SchemaForProperties( Type type, int depth )
	{
		var properties = new JsonObject();
		var required = new JsonArray();

		foreach ( var property in type.GetProperties( BindingFlags.Instance | BindingFlags.Public ) )
		{
			if ( property.SetMethod?.IsPublic != true || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null )
				continue;

			// Names exactly as the engine serializer writes them, so structured output matches
			// the schema. Binding reads case insensitively either way.
			var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
				?? property.Name;

			var schema = SchemaForType( property.PropertyType, depth + 1 );

			if ( property.GetCustomAttribute<DescriptionAttribute>() is { Value: not null } description )
			{
				schema["description"] = description.Value;
			}

			properties[name] = schema;

			// The C# required keyword means required here too - the serializer enforces it on bind
			if ( property.GetCustomAttribute<RequiredMemberAttribute>() is not null )
			{
				required.Add( name );
			}
		}

		return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required };
	}

	static bool IsIntegerType( Type type ) =>
		type == typeof( int ) || type == typeof( uint ) || type == typeof( long ) || type == typeof( ulong )
		|| type == typeof( short ) || type == typeof( ushort ) || type == typeof( byte ) || type == typeof( sbyte );

	static bool IsNumberType( Type type ) =>
		type == typeof( float ) || type == typeof( double ) || type == typeof( decimal );

	static string ToSnakeCase( string name )
	{
		var builder = new System.Text.StringBuilder( name.Length + 4 );

		foreach ( var c in name )
		{
			if ( char.IsUpper( c ) && builder.Length > 0 ) builder.Append( '_' );
			builder.Append( char.ToLowerInvariant( c ) );
		}

		return builder.ToString();
	}
}
