using Facepunch.ActionGraphs;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public abstract partial class Component : BytePack.ISerializer
{
	public JsonNode Serialize( GameObject.SerializeOptions options = null )
	{
		var isSceneForNetwork = options?.SceneForNetwork ?? false;
		var isSingleNetworkObject = options?.SingleNetworkObject ?? false;
		var isNetworked = isSceneForNetwork || isSingleNetworkObject;

		if ( isNetworked && Flags.Contains( ComponentFlags.NotNetworked ) )
			return null;

		if ( Flags.Contains( ComponentFlags.NotCloned ) )
			return null;

		if ( !isNetworked && Flags.Contains( ComponentFlags.NotSaved ) )
			return null;

		var t = Game.TypeLibrary.GetType( GetType() );
		if ( t is null )
		{
			Log.Warning( $"TypeLibrary could not find {GetType()}" );
			return null;
		}

		using var sceneScope = Scene.Push();

		// Will omit serializing the target of embedded Action Graphs
		using var targetScope = ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ) ) );

		var json = new JsonObject
		{
			{ JsonKeys.Type, t.SerializedName },
			{ JsonKeys.Id, Id },
			{ JsonKeys.Enabled, Enabled }
		};

		if ( (isSceneForNetwork || isSingleNetworkObject) && this is INetworkSnapshot sw )
		{
			var writer = ByteStream.Create( 8 );
			sw.WriteSnapshot( ref writer );

			if ( writer.Length > 0 )
			{
				json[JsonKeys.Snapshot] = Json.ToNode( writer.ToArray() );
			}

			writer.Dispose();
		}

		if ( ComponentVersion != 0 ) json[JsonKeys.Version] = ComponentVersion;

		foreach ( var member in ReflectionQueryCache.OrderedSerializableMembers( GetType() ) )
		{
			if ( member is FieldDescription field )
			{
				var value = field.GetValue( this );
				try
				{
					json.Add( field.Name, Json.ToNode( value, field.FieldType ) );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Error when serializing {this}.{field.Name} ({e.Message})\n{value}" );
				}
			}
			else if ( member is PropertyDescription prop )
			{
				var value = prop.GetValue( this );
				try
				{
					json.Add( prop.Name, Json.ToNode( value, prop.PropertyType ) );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Error when serializing {this}.{prop.Name} ({e.Message})\n{value}" );
				}
			}
		}

		return json;
	}

	JsonObject jsonData;

	public void Deserialize( JsonObject node )
	{
		var serializedVersion = (int)(node[JsonKeys.Version] ?? 0);
		if ( serializedVersion < ComponentVersion )
		{
			//Log.Warning( $"{this} needs an API update, running upgraders" );
			JsonUpgrader.Upgrade( serializedVersion, node, GetType() );
		}

		if ( node.TryGetPropertyValue( JsonKeys.Id, out var id ) )
		{
			Id = (Guid)id;
		}

		if ( node.TryGetPropertyValue( JsonKeys.Snapshot, out var snapshotNode ) && this is INetworkSnapshot sw )
		{
			var data = snapshotNode.Deserialize<byte[]>();
			var reader = ByteStream.CreateReader( data );
			sw.ReadSnapshot( ref reader );
			reader.Dispose();
		}

		jsonData = node;

		InitializeComponent();

		Enabled = (bool)(jsonData[JsonKeys.Enabled] ?? true);
	}

	internal void PostDeserialize()
	{
		if ( jsonData is null )
			return;

		using var sceneScope = Scene.Push();

		// Inject the host object into embedded Action Graphs
		using var targetScope = ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ), GameObject ) );

		try
		{
			foreach ( var field in ReflectionQueryCache.OrderedSerializableMembers( GetType() ) )
			{
				// Skip fields that are not PRESENT in json data
				// Those should stay code defined defaults.
				if ( !jsonData.ContainsKey( field.Name ) )
				{
					continue;
				}

				// This also includes fields that are null
				var v = jsonData[field.Name];

				try
				{
					DeserializeProperty( field, v );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Error when deserializing {this}.{field.Name} ({e.Message})\n{v}" );
				}
			}
		}
		finally
		{
			jsonData = null;
		}

		CheckRequireComponent();
	}

	/// <summary>
	/// Deserialize this component as per <see cref="Deserialize"/> but update <see cref="GameObject"/> and <see cref="Component"/> property
	/// references immediately instead of having them deferred.
	/// </summary>
	public void DeserializeImmediately( JsonObject node )
	{
		Deserialize( node );
		PostDeserialize();
		OnValidateInternal();
	}

	[ConVar( "serialization_warn_time", ConVarFlags.Protected, Help = "Warn if deserializing a component property takes longer than this number of milliseconds." )]
	internal static int DeserializeTimeWarnThreshold { get; set; }

	private void StopTiming( MemberDescription member, Type type, FastTimer timer )
	{
		if ( DeserializeTimeWarnThreshold <= 0 ) return;
		if ( Scene.IsLoading ) return;

		var duration = timer.ElapsedMilliSeconds;

		if ( duration > DeserializeTimeWarnThreshold )
		{
			Log.Warning( $"Deserializing {type.Name} {member.TypeDescription.Name}.{member.Name} took {duration:F1}ms!" );
		}
	}

	internal void DeserializeProperty( MemberDescription member, JsonNode node )
	{
		var startTime = FastTimer.StartNew();

		if ( member is PropertyDescription prop )
		{
			if ( !prop.IsSetMethodPublic )
			{
				// By getting the property from the declaring class, we can allow setting
				// properties with a private setter.
				var declaringType = Game.TypeLibrary.GetType( prop.MemberInfo.DeclaringType );
				prop = declaringType.GetProperty( member.Name ) ?? prop;
			}

			if ( prop.PropertyType.IsAssignableTo( typeof( IJsonPopulator ) ) )
			{
				var value = prop.GetValue( this );
				if ( value == null ) value = Activator.CreateInstance( prop.PropertyType );

				if ( value is IJsonPopulator jsonConvert )
				{
					jsonConvert.Deserialize( node );
				}

				if ( prop.PropertyType.IsValueType )
				{
					prop.SetValue( this, value );
				}
			}
			else
			{
				prop.SetValue( this, Json.FromNode( node, prop.PropertyType ) );
			}

			StopTiming( prop, prop.PropertyType, startTime );
			return;
		}

		if ( member is FieldDescription field )
		{
			if ( !field.IsPublic )
			{
				// By getting the field from the declaring class, we can allow setting
				// private fields.
				var declaringType = Game.TypeLibrary.GetType( field.MemberInfo.DeclaringType );
				field = declaringType.GetField( member.Name ) ?? field;
			}

			if ( field.FieldType.IsAssignableTo( typeof( IJsonPopulator ) ) )
			{
				var value = field.GetValue( this );
				if ( value == null ) value = Activator.CreateInstance( field.FieldType );

				if ( value is IJsonPopulator jsonConvert )
				{
					jsonConvert.Deserialize( node );
				}

				if ( field.FieldType.IsValueType )
				{
					field.SetValue( this, value );
				}
			}
			else
			{
				field.SetValue( this, Json.FromNode( node, field.FieldType ) );
			}

			StopTiming( field, field.FieldType, startTime );
			return;
		}
	}

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var id = bs.Read<Guid>();

		if ( !Game.ActiveScene.IsValid() ) return default;
		return Game.ActiveScene.Directory.FindComponentByGuid( id );
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		if ( value is not Component component )
		{
			bs.Write( Guid.Empty );
			return;
		}

		bs.Write( component.Id );
	}

	public static object JsonRead( ref Utf8JsonReader reader, Type targetType )
	{
		if ( reader.TokenType == JsonTokenType.StartObject )
		{
			var compRef = JsonSerializer.Deserialize<ComponentReference>( ref reader );

			return compRef.Resolve( Game.ActiveScene, targetType, warn: true );
		}

		//
		// Legacy way
		//
		if ( reader.TryGetGuid( out Guid guid ) )
		{
			if ( !Game.ActiveScene.IsValid() )
			{
				Log.Warning( "Tried to read component - but active scene was null!" );
				return null;
			}

			var go = Game.ActiveScene.Directory.FindByGuid( guid );

			if ( go is null )
				throw new( $"GameObject {guid} was not found" );

			var component = go.Components.Get( targetType, FindMode.EverythingInSelf );
			if ( component is null )
				throw new( $"Component {targetType} was not found on {go}" );

			return component;
		}

		return null;
	}

	public static void JsonWrite( object value, Utf8JsonWriter writer )
	{
		if ( value is not Component component )
			throw new NotImplementedException();

		if ( !component.IsValid )
		{
			writer.WriteNullValue();
			return;
		}

		JsonSerializer.Serialize( writer, ComponentReference.FromInstance( component ), Json.options );
	}

	/// <summary>
	/// Json Keys used for serialization and deserialization of Components.
	/// Kept here so they are easier to change, and we are less susceptible to typos.
	/// </summary>
	internal static class JsonKeys
	{
		internal const string Id = "__guid";
		internal const string Flags = "Flags";
		internal const string Version = "__version";
		internal const string Type = "__type";
		internal const string Snapshot = "__snapshot";
		internal const string Enabled = "__enabled";
	}

}
