using NativeEngine;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox
{
	public partial class Model
	{
		Dictionary<string, object> DataCache = new();

		internal static readonly JsonSerializerOptions DataJsonOptions = new()
		{
			ReadCommentHandling = JsonCommentHandling.Skip,
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			IncludeFields = true,
			Converters =
			{
				new JsonStringEnumConverter()
			}
		};

		internal string GetJson( string keyname )
		{
			if ( string.IsNullOrEmpty( Name ) ) Log.Warning( $"Warning: Trying to access Model Key Value '{keyname}' on an empty model!" );

			var key = native.FindModelSubKey( keyname );
			if ( key.IsNull ) return null;

			return EngineGlue.KeyValues3ToJson( key );
		}

		internal static string DeduceKeyName( Type type )
		{
			var outputType = type;
			var dataType = type;
			if ( dataType.IsArray )
			{
				dataType = dataType.GetElementType();
			}

			// Try to warn if the data types are mismatched
			var gdAttr = dataType.GetCustomAttribute<ModelEditor.GameDataAttribute>();
			if ( gdAttr == null ) throw new ArgumentException( $"{type.Name} does not have ModelDoc.GameDataAttribute" );

			if ( !gdAttr.AllowMultiple && outputType.IsArray ) throw new ArgumentException( $"{type.Name} is not a list, but an array generic parameter was given" );
			if ( gdAttr.AllowMultiple && !outputType.IsArray ) throw new ArgumentException( $"{type.Name} is a list, but non array generic parameter was given" );

			var keyname = gdAttr.Name;
			if ( gdAttr.AllowMultiple ) keyname = gdAttr.ListName ?? $"{keyname}_list";

			return keyname;
		}

		/// <summary>
		/// Tries to extract data from model based on the given type's <see cref="ModelEditor.GameDataAttribute">ModelDoc.GameDataAttribute</see>.
		/// </summary>
		/// <param name="data">The extracted data, or default on failure.</param>
		/// <returns>true if data was extracted successfully, false otherwise.</returns>
		public bool TryGetData<T>( out T data )
		{
			bool ret = TryGetData( typeof( T ), out object dat );
			data = (T)dat;

			return ret;
		}

		/// <summary>
		/// Tries to extract data from model based on the given type's <see cref="ModelEditor.GameDataAttribute">ModelDoc.GameDataAttribute</see>.
		/// </summary>
		/// <param name="data">The extracted data, or default on failure.</param>
		/// <param name="t">The class with <see cref="ModelEditor.GameDataAttribute">ModelDoc.GameDataAttribute</see>.</param>
		/// <returns>true if data was extracted successfully, false otherwise.</returns>
		public bool TryGetData( Type t, out object data )
		{
			string keyname = DeduceKeyName( t );

			string json;

			// Try to get data from cache
			var cacheKey = $"{keyname}~{t.Name}";
			if ( DataCache != null && DataCache.TryGetValue( cacheKey, out object cacheValue ) )
			{
				json = (string)cacheValue;
			}
			else
			{
				json = GetJson( keyname );
				if ( string.IsNullOrWhiteSpace( json ) )
				{
					data = default;
					return false;
				}

				// Special case of nodes that have no properties
				if ( json == "null" ) json = "{}";

				// Add to cache
				if ( DataCache == null ) DataCache = new Dictionary<string, object>();
				DataCache.Add( cacheKey, json );
			}

			try
			{
				var obj = JsonSerializer.Deserialize( json, t, DataJsonOptions );

				if ( obj == null )
				{
					data = default;
					return false;
				}

				data = obj;
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Failed to deserialize '{keyname}' to {t.Name}" );
			}

			data = default;
			return false;
		}

		/// <summary>
		/// Tests if this model has generic data based on given type's <see cref="ModelEditor.GameDataAttribute">ModelDoc.GameDataAttribute</see>.
		/// This will be faster than testing this via GetData<![CDATA[<>]]>()
		/// </summary>
		public bool HasData<T>()
		{
			var keyname = DeduceKeyName( typeof( T ) );
			return native.FindModelSubKey( keyname ).IsValid;
		}

		/// <summary>
		/// Extracts data from model based on the given type's <see cref="ModelEditor.GameDataAttribute">ModelDoc.GameDataAttribute</see>.
		/// </summary>
		public T GetData<T>()
		{
			if ( TryGetData( out T data ) )
			{
				return data;
			}

			return default;
		}

		#region BreakCommands

		/// <summary>
		/// Internal function used to get a list of break commands the model has.
		/// </summary>
		public Dictionary<string, string[]> GetBreakCommands()
		{
			var cacheKey = $"break_command_list~{Name}";
			if ( DataCache != null && DataCache.TryGetValue( cacheKey, out object cacheValue ) )
			{
				return (Dictionary<string, string[]>)cacheValue;
			}

			Dictionary<string, string[]> output = new();

			var jsonString = GetJson( "break_command_list" );
			if ( jsonString == null ) return output;

			using ( JsonDocument document = JsonDocument.Parse( jsonString ) )
			{
				JsonElement root = document.RootElement;
				if ( root.ValueKind != JsonValueKind.Array ) return output;

				foreach ( JsonElement commandData in root.EnumerateArray() )
				{
					if ( commandData.TryGetProperty( "break_command", out JsonElement command ) )
					{
						var cmd = command.GetString();
						List<string> arr = output.ContainsKey( cmd ) ? output[cmd].Cast<string>().ToList() : new();
						arr.Add( commandData.GetRawText() );
						output[cmd] = arr.ToArray();
					}
				}
			}

			if ( DataCache == null ) DataCache = new Dictionary<string, object>();
			DataCache.Add( cacheKey, output );

			return output;
		}

		#endregion
	}
}
