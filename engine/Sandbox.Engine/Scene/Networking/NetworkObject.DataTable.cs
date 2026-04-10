using Sandbox.Network;

namespace Sandbox;

internal sealed partial class NetworkObject
{
	internal NetworkTable dataTable;

	/// <summary>
	/// Get a deterministic property slot for use with a network table.
	/// </summary>
	internal static int GetPropertySlot( int propertyIdent, Guid guid )
	{
		//
		// https://github.com/dotnet/runtime/blob/457c6b709b54b354efd8d36a9b5c03db53494612/docs/design/security/System.HashCode.md?plain=1#L52
		// tony: If we were to use System.HashCode.Combine here, it would be non-deterministic across different processes for "security reasons" - what the fuck
		//
		return guid.GetHashCode() ^ propertyIdent;
	}

	void CreateDataTable()
	{
		dataTable?.Dispose();
		dataTable = new();

		RegisterPropertiesRecursive( GameObject );
	}

	internal void RegisterPropertiesRecursive( GameObject go = null, bool gameObjects = true, bool components = true )
	{
		go ??= GameObject;

		if ( gameObjects )
		{
			RegisterProperties( go, go.Id );
		}

		if ( components )
		{
			foreach ( var component in go.Components.GetAll() )
			{
				if ( component is null ) continue;
				RegisterProperties( component, component.Id );
			}
		}

		foreach ( var child in go.Children )
		{
			if ( child.NetworkMode != NetworkMode.Snapshot ) continue;
			// Conna: pass false here so that we don't add properties from child GameObjects. We only
			// want to add properties from components on child GameObjects. This is because we don't
			// want to add potentially hundreds of entries for OwnerTransfer, etc.
			RegisterPropertiesRecursive( child, false );
		}
	}

	internal void RegisterProperties( object instance, Guid guid )
	{
		var type = instance.GetType();

		// Register all our Sync properties with the data table.
		foreach ( var propertyAndAttribute in ReflectionQueryCache.SyncProperties( type ) )
		{
			var isHostSync = propertyAndAttribute.Attribute.Flags.HasFlag( SyncFlags.FromHost );
			var isQuery = propertyAndAttribute.Attribute.Flags.HasFlag( SyncFlags.Query );

			try
			{
				var originType = propertyAndAttribute.Property.DeclaringType ?? type;
				var identity = GetPropertySlot( $"{originType.FullName}.{propertyAndAttribute.Property.Name}".FastHash(), guid );

				var entry = new NetworkTable.Entry
				{
					TargetType = propertyAndAttribute.Property.PropertyType,
					ControlCondition = c => isHostSync ? c.IsHost : HasControl( c ),
					GetValue = () => propertyAndAttribute.Property.GetValue( instance ),
					SetValue = ( v ) => propertyAndAttribute.Property?.SetValue( instance, v ),
					NeedsQuery = isQuery,
					DebugName = $"{originType.Name}.{propertyAndAttribute.Property.Name}"
				};

				dataTable.Register( identity, entry );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Got exception when creating network table (reading {GameObject}.{propertyAndAttribute.Property.Name}) - {e.Message}" );
			}
		}
	}

	/// <summary>
	/// Write all reliable data table entries.
	/// </summary>
	byte[] WriteReliableData()
	{
		var data = ByteStream.Create( 32 );

		dataTable.WriteAllReliable( ref data );

		var bytes = data.ToArray();
		data.Dispose();

		return bytes;
	}

	/// <summary>
	/// Write all pending data table changes.
	/// </summary>
	byte[] WriteDataTable( bool full )
	{
		if ( !dataTable.HasAnyChanges && !full )
			return null;

		var data = ByteStream.Create( 32 );

		if ( full )
			dataTable.WriteAll( ref data );
		else
			dataTable.WriteChanged( ref data );

		var bytes = data.ToArray();
		data.Dispose();

		return bytes;
	}

	/// <summary>
	/// Read the network table data.
	/// </summary>
	private void ReadDataTable( byte[] data, NetworkTable.ReadFilter filter = null, Connection source = null )
	{
		if ( data is null ) return;

		var reader = ByteStream.CreateReader( data );
		dataTable.Read( ref reader, filter, source );
		reader.Dispose();
	}
}
