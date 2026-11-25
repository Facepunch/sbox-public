using Facepunch.ActionGraphs;
using Sandbox.Engine;
using System.Buffers;
using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Writes the JSON that <see cref="GameObject.Serialize"/> followed by ToJsonString gives for network options, as UTF-8
/// bytes without building a node tree. Create messages, refreshes and join snapshots send a lot of it.
/// </summary>
internal static class NetworkJson
{
	internal sealed class Writers
	{
		readonly ArrayBufferWriter<byte> _output = new( 4096 );
		readonly ArrayBufferWriter<byte> _value = new( 256 );

		public readonly Utf8JsonWriter Writer;
		public readonly Utf8JsonWriter ValueWriter;
		public readonly HashSet<string> Keys = new();
		public bool InUse;

		public Writers()
		{
			// Default options, like JsonNode.ToJsonString()
			Writer = new Utf8JsonWriter( _output );
			ValueWriter = new Utf8JsonWriter( _value );
		}

		public void Begin()
		{
			_output.ResetWrittenCount();
			Writer.Reset( _output );
		}

		public byte[] End()
		{
			Writer.Flush();
			return _output.WrittenSpan.ToArray();
		}

		/// <summary>
		/// Serialize one member value on its own, so a value that throws halfway doesn't break the object around it.
		/// </summary>
		public ReadOnlySpan<byte> WriteValue( object value, Type type )
		{
			_value.ResetWrittenCount();
			ValueWriter.Reset( _value );

			if ( value is IJsonPopulator populator )
			{
				var node = populator.Serialize();
				if ( node is null ) ValueWriter.WriteNullValue();
				else node.WriteTo( ValueWriter );
			}
			else
			{
				JsonSerializer.Serialize( ValueWriter, value, type, Json.options );
			}

			ValueWriter.Flush();
			return _value.WrittenSpan;
		}
	}

	// Omits the target of embedded Action Graphs, like Serialize
	internal static readonly InputDefinition Target = InputDefinition.Target( typeof( GameObject ) );

	[ThreadStatic] static Writers _cached;

	static Writers Rent()
	{
		var writers = _cached is { InUse: false } cached ? cached : new Writers();
		_cached ??= writers;
		writers.InUse = true;
		writers.Begin();
		return writers;
	}

	/// <summary>
	/// Null when the object isn't saved with these options, like <see cref="GameObject.Serialize"/>.
	/// </summary>
	public static byte[] Serialize( GameObject go, GameObject.SerializeOptions options )
	{
		if ( !options.ShouldSave( go ) ) return null;

		// A subclass may override Serialize, which only the node path runs
		if ( go.GetType() != typeof( GameObject ) )
			return go.Serialize( options ) is { } node ? JsonSerializer.SerializeToUtf8Bytes( node ) : null;

		var writers = Rent();

		try
		{
			go.WriteNetworkJson( writers, options );
			return writers.End();
		}
		catch ( Exception e )
		{
			// Something threw where the nodes would have skipped just that part, they know how
			Log.Warning( e, $"Exception when writing network json for {go}, serializing it the slow way" );
			return go.Serialize( options ) is { } json ? JsonSerializer.SerializeToUtf8Bytes( json ) : null;
		}
		finally
		{
			writers.InUse = false;
		}
	}

	/// <inheritdoc cref="Serialize(GameObject, GameObject.SerializeOptions)"/>
	public static byte[] Serialize( Component component, GameObject.SerializeOptions options )
	{
		var writers = Rent();

		try
		{
			return component.WriteNetworkJson( writers, options ) ? writers.End() : null;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Exception when writing network json for {component}, serializing it the slow way" );
			return component.Serialize( options ) is { } json ? JsonSerializer.SerializeToUtf8Bytes( json ) : null;
		}
		finally
		{
			writers.InUse = false;
		}
	}
}

public partial class GameObject
{
	/// <summary>
	/// <see cref="SerializeStandard"/> for network options, written instead of built.
	/// </summary>
	internal void WriteNetworkJson( NetworkJson.Writers writers, SerializeOptions options )
	{
		// Everything written is in this scene and omits the same target, push them once
		using var sceneScope = Scene.PushScope();
		using var targetScope = ActionGraph.PushTarget( NetworkJson.Target );

		WriteNetworkJsonObject( writers, options );
	}

	void WriteNetworkJsonObject( NetworkJson.Writers writers, SerializeOptions options )
	{

		var w = writers.Writer;
		w.WriteStartObject();

		w.WriteString( JsonKeys.Id, Id );
		if ( GameObjectVersion != 0 ) w.WriteNumber( JsonKeys.Version, GameObjectVersion );
		w.WriteNumber( JsonKeys.Flags, (long)Flags );
		w.WriteString( JsonKeys.Name, Name );

		WriteTransform( w );

		w.WriteString( JsonKeys.Tags, string.Join( ",", Tags.TryGetAll( false ) ) );
		w.WriteBoolean( JsonKeys.Enabled, Enabled );
		w.WriteNumber( JsonKeys.NetworkMode, (int)NetworkMode );
		w.WriteNumber( JsonKeys.NetworkFlags, (int)NetworkFlags );
		w.WriteNumber( JsonKeys.NetworkOrphaned, (int)NetworkOrphaned );
		w.WriteBoolean( JsonKeys.AlwaysTransmit, AlwaysTransmit );
		w.WriteNumber( JsonKeys.OwnerTransfer, (int)OwnerTransfer );

		if ( options.SingleNetworkObject && IsPrefabInstanceRoot )
		{
			w.WriteString( JsonKeys.NetworkedPrefabInstance, PrefabInstanceSource );
		}

		if ( !options.IgnoreComponents )
		{
			w.WriteStartArray( JsonKeys.Components );

			foreach ( var component in Components.GetAll() )
			{
				if ( component is null ) continue;

				if ( component is MissingComponent missing )
				{
					missing.GetJson().WriteTo( w );
					continue;
				}

				component.WriteNetworkJsonObject( writers, options );
			}

			w.WriteEndArray();
		}

		if ( !options.IgnoreChildren )
		{
			w.WriteStartArray( JsonKeys.Children );

			for ( int i = 0; i < Children.Count; i++ )
			{
				var child = Children[i];

				if ( child is null ) continue;
				if ( child.IsDestroyed ) continue;

				bool childIsNetworked = child.IsNetworkRoot || child.NetworkMode == NetworkMode.Object;
				if ( options.SingleNetworkObject && childIsNetworked ) continue;

				if ( !options.ShouldSave( child ) ) continue;

				// A subclass may override Serialize, which only the node path runs
				if ( child.GetType() != typeof( GameObject ) )
				{
					child.Serialize( options )?.WriteTo( w );
					continue;
				}

				child.WriteNetworkJsonObject( writers, options );
			}

			w.WriteEndArray();
		}

		w.WriteEndObject();
	}

	void WriteTransform( Utf8JsonWriter w )
	{
		Transform tx;

		if ( Flags.Contains( GameObjectFlags.PhysicsBone ) )
		{
			tx = Parent?.WorldTransform.ToLocal( WorldTransform ) ?? WorldTransform;
		}
		else if ( Flags.Contains( GameObjectFlags.Attachment ) || (Flags.Contains( GameObjectFlags.Bone ) && !Flags.Contains( GameObjectFlags.ProceduralBone )) )
		{
			tx = new Transform( Vector3.Zero, Rotation.Identity, Vector3.One );
		}
		else
		{
			tx = LocalTransform;
		}

		w.WritePropertyName( JsonKeys.Position );
		JsonSerializer.Serialize( w, tx.Position, Json.options );
		w.WritePropertyName( JsonKeys.Rotation );
		JsonSerializer.Serialize( w, tx.Rotation, Json.options );
		w.WritePropertyName( JsonKeys.Scale );
		JsonSerializer.Serialize( w, tx.Scale, Json.options );
	}
}

public abstract partial class Component
{
	/// <summary>
	/// <see cref="Serialize"/> for network options, written instead of built. False when it isn't sent.
	/// </summary>
	internal bool WriteNetworkJson( NetworkJson.Writers writers, GameObject.SerializeOptions options )
	{
		using var sceneScope = Scene.PushScope();
		using var targetScope = ActionGraph.PushTarget( NetworkJson.Target );

		return WriteNetworkJsonObject( writers, options );
	}

	internal bool WriteNetworkJsonObject( NetworkJson.Writers writers, GameObject.SerializeOptions options )
	{
		if ( Flags.Contains( ComponentFlags.NotNetworked ) )
			return false;

		if ( Flags.Contains( ComponentFlags.NotCloned ) )
			return false;

		var t = GlobalContext.Current.TypeLibrary.GetType( GetType() );
		if ( t is null )
		{
			Log.Warning( $"TypeLibrary could not find {GetType()}" );
			return false;
		}

		// Game code, run it before anything is written
		byte[] snapshot = null;
		if ( this is INetworkSnapshot sw )
		{
			var bs = ByteStream.Create( 8 );
			try
			{
				sw.WriteSnapshot( ref bs );
				if ( bs.Length > 0 ) snapshot = bs.ToArray();
			}
			finally
			{
				bs.Dispose();
			}
		}

		var w = writers.Writer;
		var keys = writers.Keys;
		keys.Clear();

		w.WriteStartObject();
		w.WriteString( JsonKeys.Type, t.SerializedName );
		w.WriteString( JsonKeys.Id, Id );
		w.WriteBoolean( JsonKeys.Enabled, Enabled );
		w.WriteNumber( JsonKeys.Flags, (long)Flags );
		keys.Add( JsonKeys.Type );
		keys.Add( JsonKeys.Id );
		keys.Add( JsonKeys.Enabled );
		keys.Add( JsonKeys.Flags );

		if ( snapshot is not null )
		{
			w.WritePropertyName( JsonKeys.Snapshot );
			JsonSerializer.Serialize( w, snapshot, Json.options );
			keys.Add( JsonKeys.Snapshot );
		}

		if ( ComponentVersion != 0 )
		{
			w.WriteNumber( JsonKeys.Version, ComponentVersion );
			keys.Add( JsonKeys.Version );
		}

		var skipNulls = options.SkipNulls;

		foreach ( var member in ReflectionQueryCache.OrderedSerializableMembers( GetType() ) )
		{
			object value;
			Type type;

			if ( member is FieldDescription field )
			{
				value = field.GetValue( this );
				type = field.FieldType;
			}
			else if ( member is PropertyDescription prop )
			{
				value = prop.GetValue( this );
				type = prop.PropertyType;
			}
			else
			{
				continue;
			}

			if ( skipNulls && value is null ) continue;

			ReadOnlySpan<byte> json;

			try
			{
				json = writers.WriteValue( value, type );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when serializing {this}.{member.Name} ({e.Message})\n{value}" );
				continue;
			}

			// A converter that wrote nothing fails this property on the node path too, not the whole object
			if ( json.IsEmpty )
			{
				Log.Warning( $"Error when serializing {this}.{member.Name} (the converter wrote no value)\n{value}" );
				continue;
			}

			// The node path can't add a key twice either
			if ( !keys.Add( member.Name ) )
			{
				Log.Warning( $"Error when serializing {this}.{member.Name} (An item with the same key has already been added. Key: {member.Name})\n{value}" );
				continue;
			}

			w.WritePropertyName( member.Name );
			w.WriteRawValue( json, skipInputValidation: true );
		}

		w.WriteEndObject();
		return true;
	}
}
