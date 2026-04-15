using Sandbox.Engine;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	readonly HashSet<StringTable> tables = new();

	/// <summary>
	/// String tables should all get installed at this point.
	/// </summary>
	void InstallStringTables()
	{
		InstallTable( ConnectionInfo.StringTable );
		IGameInstanceDll.Current?.InstallNetworkTables( this );
	}

	/// <summary>
	/// Install a network table. If we're the host then this table will
	/// be sent to all clients.. but only if they have the same named network
	/// table installed.
	/// </summary>
	public void InstallTable( StringTable table )
	{
		tables.Add( table );
	}

	void TableMessage( InternalMessageType type, NetworkMessage msg )
	{
		if ( !msg.Source.IsHost ) return;

		var tableName = msg.Data.Read<string>();

		var table = tables.FirstOrDefault( x => x.Name == tableName );
		if ( table == null )
		{
			Log.Warning( $"No data table called '{tableName}' installed! [{msg.Source.State}]" );
			return;
		}

		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.StringTable, msg.Data.Length );

		if ( type == InternalMessageType.TableSnapshot )
		{
			table.ReadSnapshot( msg.Data );
		}

		if ( type == InternalMessageType.TableUpdated )
		{
			table.ReadUpdate( msg.Data );
		}
	}

	public void SendTableUpdates()
	{
		// Only the host sends updated changes
		if ( !IsHost )
		{
			foreach ( var table in tables )
			{
				table.ClearChanges();
			}

			return;
		}

		foreach ( var table in tables )
		{
			if ( !table.HasChanged )
				continue;

			var bs = ByteStream.Create( 512 );
			bs.Write( InternalMessageType.TableUpdated );
			bs.Write( table.Name );
			table.BuildUpdateMessage( ref bs );

			// send this update to anyone that has got string snapshots already
			Broadcast( bs, Connection.ChannelState.Welcome );

			table.ClearChanges();

			bs.Dispose();
		}
	}
}
