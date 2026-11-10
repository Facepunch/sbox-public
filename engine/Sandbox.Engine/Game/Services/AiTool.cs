using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace Sandbox.Services;

/// <summary>Base for tools registered through AiConfig. Derive from AiTool&lt;TResult&gt;.</summary>
public abstract class AiTool
{
	/// <summary>The game-owned context from AiConfig. It is never part of the model's arguments.</summary>
	[JsonIgnore]
	public object Context { get; internal set; }

	internal abstract Type ResultType { get; }
	internal abstract Task<JsonElement> Invoke( CancellationToken cancellationToken );
}

/// <summary>
/// A tool whose public input properties are populated by the AI. XML summaries describe its
/// purpose and inputs. Validate game rules and collection contents before performing actions.
/// </summary>
/// <typeparam name="TResult">The JSON-serializable result type, also described to the AI.</typeparam>
public abstract class AiTool<TResult> : AiTool
{
	/// <summary>Executes the tool and returns its result. Forward cancellation to asynchronous work.</summary>
	public abstract Task<TResult> Run( CancellationToken cancellationToken );

	internal override Type ResultType => typeof( TResult );
	internal override async Task<JsonElement> Invoke( CancellationToken cancellationToken )
		=> JsonSerializer.SerializeToElement( await Run( cancellationToken ), Ai.StructuredJson );
}

sealed class AiToolRegistration
{
	readonly Type type;
	internal AiToolDefinition Definition { get; }

	AiToolRegistration( Type type, Type resultType )
	{
		this.type = type;
		Definition = new AiToolDefinition
		{
			Name = type.Name,
			Description = $"{Ai.GetSchemaDescription( type )}\nReturns JSON matching this schema: {Ai.CreateSchema( resultType ).GetRawText()}",
			Parameters = Ai.CreateSchema( type, ToolJson )
		};
	}

	internal static AiToolRegistration Create<TTool>() where TTool : AiTool, new()
		=> new( typeof( TTool ), new TTool().ResultType );

	internal async Task<JsonElement> Invoke( string json, object context, CancellationToken cancellationToken )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var tool = (AiTool)JsonSerializer.Deserialize( json, type, ToolJson );
		if ( tool is null ) throw new JsonException( "Tool arguments cannot be null." );
		tool.Context = context;
		return await tool.Invoke( cancellationToken );
	}

	static readonly JsonSerializerOptions ToolJson = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		RespectNullableAnnotations = true,
		RespectRequiredConstructorParameters = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RequireProperties } },
		Converters = { new JsonStringEnumConverter( allowIntegerValues: false ) }
	};

	static void RequireProperties( JsonTypeInfo type )
	{
		if ( type.Kind != JsonTypeInfoKind.Object ) return;
		var nullability = new NullabilityInfoContext();
		foreach ( var property in type.Properties.ToArray() )
		{
			if ( property.Get is null && property.Set is null ) continue;
			if ( property.Set is null && property.AssociatedParameter is null )
			{
				type.Properties.Remove( property );
				continue;
			}
			if ( !property.IsExtensionData && property.Set is not null ) property.IsRequired = true;
			var state = property.AttributeProvider switch
			{
				PropertyInfo member => nullability.Create( member ).WriteState,
				FieldInfo member => nullability.Create( member ).WriteState,
				_ => NullabilityState.Unknown
			};
			if ( state == NullabilityState.Unknown && !property.PropertyType.IsValueType )
			{
				var parameter = property.AssociatedParameter?.AttributeProvider as ParameterInfo;
				property.IsSetNullable = parameter is not null && nullability.Create( parameter ).ReadState == NullabilityState.Nullable;
			}
		}
	}
}
