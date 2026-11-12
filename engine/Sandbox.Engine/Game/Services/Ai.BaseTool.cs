using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace Sandbox.Services;

public static partial class Ai
{
	/// <summary>
	/// Base for tools registered through Config. Derive from BaseTool&lt;TResult&gt;.
	/// </summary>
	public abstract class BaseTool
	{
		/// <summary>
		/// The game-owned context from Config. It is never part of the model's arguments.
		/// </summary>
		[JsonIgnore]
		public object Context { get; internal set; }

		internal abstract Task<AiToolResult> Invoke( CancellationToken cancellationToken );
	}

	/// <summary>
	/// A tool whose public input properties are populated by the AI. XML summaries describe its
	/// purpose and inputs. Validate game rules and collection contents before performing actions.
	/// </summary>
	/// <typeparam name="TResult">
	/// The JSON-serializable result type, also described to the AI. Return Bitmap or Texture for a single image,
	/// or Query for text and images. A returned query's configuration is ignored; it does not start another AI request.
	/// </typeparam>
	public abstract class BaseTool<TResult> : BaseTool
	{
		/// <summary>
		/// Executes the tool and returns its result. Forward cancellation to asynchronous work.
		/// Returned bitmaps and textures remain caller-owned and must stay alive until the chat finishes preparing them.
		/// </summary>
		public abstract Task<TResult> Run( CancellationToken cancellationToken );

		internal override async Task<AiToolResult> Invoke( CancellationToken cancellationToken )
		{
			var value = await Run( cancellationToken );
			var query = value switch
			{
				Query result => result,
				Bitmap bitmap => new Query().AddImage( bitmap ),
				Texture texture => new Query().AddImage( texture ),
				null when AiToolRegistration.ReturnsImages( typeof( TResult ) ) => throw new InvalidOperationException( "The tool returned no result." ),
				_ => null
			};

			if ( query is not null )
			{
				var text = query.Input;
				var images = await query.PrepareImages( cancellationToken );
				if ( string.IsNullOrWhiteSpace( text ) && images.Count == 0 )
					throw new InvalidOperationException( "Supply tool text or an image." );

				// Snapshot the payload before retaining it for retries. The chat supplies the call ID.
				return new AiToolResult { Content = text, Images = images.Count > 0 ? images : null };
			}

			return new AiToolResult { Content = JsonSerializer.Serialize( value, Ai.StructuredJson ) };
		}
	}

	internal sealed class AiToolRegistration
	{
		readonly Type type;
		internal AiToolDefinition Definition { get; }

		AiToolRegistration( Type type, Type resultType )
		{
			this.type = type;
			Definition = new AiToolDefinition
			{
				Name = type.Name,
				Description = ReturnsImages( resultType )
					? $"{Ai.GetSchemaDescription( type )}\nReturns text and/or images."
					: $"{Ai.GetSchemaDescription( type )}\nReturns JSON matching this schema: {Ai.CreateSchema( resultType ).GetRawText()}",
				Parameters = Ai.CreateSchema( type, ToolJson )
			};
		}

		// Image results are attachments, so don't describe their implementation as a JSON schema.
		internal static bool ReturnsImages( Type type ) => type == typeof( Query )
			|| typeof( Bitmap ).IsAssignableFrom( type ) || typeof( Texture ).IsAssignableFrom( type );

		internal static AiToolRegistration Create<TTool>() where TTool : BaseTool, new()
			=> new( typeof( TTool ), typeof( TTool ).GetInheritedConstructedGenericType( typeof( BaseTool<> ) ).GetGenericArguments()[0] );

		internal async Task<AiToolResult> Invoke( string json, object context, CancellationToken cancellationToken )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var tool = (BaseTool)JsonSerializer.Deserialize( json, type, ToolJson );
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
}
