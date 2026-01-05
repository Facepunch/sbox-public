using System.Runtime.InteropServices;
using System.Text.Json;

namespace Sandbox.Network;

internal class StringTable
{
	/// <summary>
	/// The maximum size of a snapshot entry in megabytes.
	/// </summary>
	private const int MaxSnapshotEntrySize = 8;

	public readonly Dictionary<string, Entry> Entries = new();

	public string Name { get; }
	public bool Compressed { get; }
	public bool HasChanged => changed.Count > 0 || removed.Count > 0;

	/// <summary>
	/// Called when a table entry is changed or added from the network
	/// </summary>
	public Action<Entry> OnChangeOrAdd;

	/// <summary>
	/// The table has been fully replaced. Anything or everything could have changed.
	/// </summary>
	public Action OnSnapshot;

	/// <summary>
	/// Called when a table entry is removed by the network
	/// </summary>
	public Action<Entry> OnRemoved;

	/// <summary>
	/// Called after a network update of the table is complete. Not called after snapshots!
	/// </summary>
	public Action PostNetworkUpdate;

	readonly HashSet<string> changed = new();
	readonly HashSet<string> removed = new();

	public StringTable( string name, bool compressed )
	{
		Name = name;
		Compressed = compressed;
	}

	internal Entry Remove( string name )
	{
		if ( !Entries.Remove( name, out var e ) )
			return default;

		Log.Trace( $"Removed Key {name}" );
		removed.Add( name );
		changed.Remove( name );
		return e;
	}

	internal void Reset()
	{
		Log.Trace( "Table Reset" );
		Entries.Clear();
		removed.Clear();
		changed.Clear();
	}

	internal Entry Set<T>( string name, T assemblyBytes ) where T : unmanaged
	{
		var size = Marshal.SizeOf<T>();
		var arr = new byte[size];
		MemoryMarshal.Write( arr, assemblyBytes );

		return Set( name, arr );
	}

	internal Entry Set( string name, byte[] assemblyBytes )
	{
		if ( Entries.TryGetValue( name, out var entry ) )
		{
			entry.Data = assemblyBytes;
			Log.Trace( $"Updated {name} [{assemblyBytes.Length}]" );
		}
		else
		{
			entry = new Entry { Name = name, Data = assemblyBytes };
			Entries[name] = entry;
			Log.Trace( $"Added {name} [{assemblyBytes.Length}]" );
		}

		changed.Add( name );
		removed.Remove( name );

		return entry;
	}

	public class Entry
	{
		public string Name { get; set; }
		public byte[] Data { get; set; }

		/// <summary>
		/// Read an unmanaged struct from the data
		/// </summary>
		internal T Read<T>() where T : unmanaged
		{
			using var bytes = ByteStream.CreateReader( Data );
			return bytes.Read<T>();
		}

		internal string ReadAsString()
		{
			return System.Text.Encoding.UTF8.GetString( Data );
		}

		internal T ReadJson<T>()
		{
			var json = ReadAsString();
			if ( string.IsNullOrWhiteSpace( json ) ) return default;
			return JsonSerializer.Deserialize<T>( json );
		}
	}

	internal void SendSnapshot( Connection source )
	{
		var bs = ByteStream.Create( 1024 );
		bs.Write( InternalMessageType.TableSnapshot );
		bs.Write( Name );

		BuildSnapshotMessage( ref bs );
		source.SendRawMessage( bs );

		bs.Dispose();
	}

	internal void ReadSnapshot( ByteStream bs )
	{
		while ( true )
		{
			var key = bs.Read<string>();
			if ( key == null ) break;
			var data = bs.ReadArraySpan<byte>( 1024 * 1024 * MaxSnapshotEntrySize );
			Set( key, data.ToArray() );
		}

		OnSnapshot?.Invoke();
	}

	internal void BuildSnapshotMessage( ref ByteStream bs )
	{
		foreach ( var entry in Entries )
		{
			bs.Write( entry.Value.Name );
			bs.WriteArray( entry.Value.Data );
		}

		bs.Write( (string)null );
	}

	internal void BuildUpdateMessage( ref ByteStream bs )
	{
		bs.Write( removed.Count );
		bs.Write( changed.Count );

		foreach ( var entry in removed )
		{
			bs.Write( entry );
		}

		foreach ( var entry in changed )
		{
			var e = Entries[entry];

			bs.Write( e.Name );
			bs.Write( e.Data.Length );
			bs.Write( e.Data );
		}
	}

	internal void ReadUpdate( ByteStream bs )
	{
		var removed = bs.Read<int>();
		var changed = bs.Read<int>();

		for ( var i = 0; i < removed; i++ )
		{
			var e = Remove( bs.Read<string>() );
			if ( e is not null )
			{
				OnRemoved?.Invoke( e );
			}
		}

		for ( var i = 0; i < changed; i++ )
		{
			var key = bs.Read<string>();
			var len = bs.Read<int>();
			if ( len == 0 )
			{
				var e = Set( key, [] );
				if ( e is not null )
				{
					OnChangeOrAdd?.Invoke( e );
				}
			}
			else
			{
				using var data = bs.ReadByteStream( len );

				var e = Set( key, data.ToArray() );
				if ( e is not null )
				{
					OnChangeOrAdd?.Invoke( e );
				}
			}
		}

		PostNetworkUpdate?.Invoke();
	}

	internal void ClearChanges()
	{
		changed.Clear();
		removed.Clear();
	}

	internal Entry SetJson<T>( string key, T value )
	{
		var json = JsonSerializer.Serialize( value );
		return SetString( key, json );
	}

	internal Entry SetString( string key, string value )
	{
		return Set( key, System.Text.Encoding.UTF8.GetBytes( value ) );
	}
}
