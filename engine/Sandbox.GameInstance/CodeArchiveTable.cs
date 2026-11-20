using Sandbox.Network;
using Sandbox.Utility;
using System.Collections.Generic;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Advertises checksums on join and sends whole archives in live updates.
/// </summary>
internal sealed class CodeArchiveTable() : StringTable( "CodeArchive", true )
{
	sealed class AdvertisedArchive( ulong checksum ) : Entry
	{
		internal readonly ulong Checksum = checksum;
	}

	protected override void WriteSnapshotEntry( Entry entry, ref ByteStream bs ) => bs.Write( Crc64.FromBytes( entry.Data ) );

	internal override void ReadSnapshot( ByteStream bs )
	{
		Reset();
		while ( bs.Read<string>() is { } name )
		{
			Entries.Add( name, new AdvertisedArchive( bs.Read<ulong>() ) { Name = name } );
		}
	}

	internal async Task<bool> ResolveAsync( Connection host, LoadedAssembly[] loaded, CancellationToken token )
	{
		foreach ( var entry in Entries.Values.OfType<AdvertisedArchive>().ToArray() )
		{
			if ( !IsPending( entry ) ) continue;

			var bytes = loaded.FirstOrDefault( a => a.Name == entry.Name )?.CodeArchiveBytes;
			if ( bytes is null || Crc64.FromBytes( bytes ) != entry.Checksum )
			{
				bytes = await host.SendRequest( new RequestCodeArchive( entry.Name, entry.Checksum ) ).WaitAsync( token ) as byte[];

				// A live update or removal takes precedence over an outstanding request.
				if ( !IsPending( entry ) ) continue;
				if ( bytes is not { Length: > 0 } || Crc64.FromBytes( bytes ) != entry.Checksum ) return false;
			}

			entry.Data = bytes;
		}

		return true;
	}

	bool IsPending( Entry entry ) => entry.Data is null && Entries.GetValueOrDefault( entry.Name ) == entry;

	internal void OnRequest( RequestCodeArchive request, Connection client, System.Guid id )
	{
		if ( client.System?.IsHost != true || client.State != Connection.ChannelState.Welcome ) return;

		var bytes = request.Name is null ? null : Entries.GetValueOrDefault( request.Name )?.Data;
		if ( bytes is null || Crc64.FromBytes( bytes ) != request.Checksum )
		{
			// Deliver the newer version (or its removal) before answering the old request.
			client.System.SendTableUpdates();
			bytes = [];
		}

		client.SendResponse( id, bytes );
	}
}
