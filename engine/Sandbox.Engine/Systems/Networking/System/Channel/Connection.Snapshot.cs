using Sandbox.Network;
using System.Threading;

namespace Sandbox;

public abstract partial class Connection
{
	// Owned and drained by the main thread. Workers never access the connection or transport.
	readonly Queue<(Task<byte[][]> Packets, NetFlags Flags, bool PreparedSnapshot)> pendingSends = new();
	bool sendsClosed;
	CancellationTokenSource snapshotCancellation;
	internal bool HasPendingSends => pendingSends.Count > 0;

	internal void SendSnapshot<T>( SnapshotCapture capture, Func<SnapshotMsg, T> envelope, NetFlags flags )
	{
		if ( sendsClosed )
		{
			return;
		}
		if ( RoutesThroughHost && !HasPendingSends )
		{
			SendMessage( envelope( capture.Materialize() ), flags );
			return;
		}

		var serializer = System.TypeLibrary.CreateDetachedSerializer( typeof( T ), typeof( SnapshotMsg ),
			typeof( SnapshotMsg.GameObjectSystemData ), typeof( ObjectCreateMsg ) );
		var cancellation = (snapshotCancellation ??= new()).Token;
		var logAs = Diagnostics.DevTools.NetworkLog.LogsMessages ? Diagnostics.DevTools.NetworkLog.PeerName( this ) : null;
		QueueSend( Task.Run( () =>
		{
			try
			{
				cancellation.ThrowIfCancellationRequested();
				var packets = capture.Encode( serializer, envelope, flags );
				if ( logAs is not null ) Diagnostics.DevTools.NetworkLog.Message( Diagnostics.DevTools.NetworkLog.Kind.Snapshot, "SceneSnapshot", packets[0].Length, true, logAs );
				return packets;
			}
			finally
			{
				serializer.Dispose();
			}
		} ), flags, preparedSnapshot: true );
	}

	internal void QueueSend( Task<byte[][]> packets, NetFlags flags, bool preparedSnapshot = false )
	{
		// Observe failures even when the connection closes before the job completes.
		_ = packets.ContinueWith( t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted );
		if ( !sendsClosed )
		{
			pendingSends.Enqueue( (packets, flags, preparedSnapshot) );
		}
	}

	internal void FlushPendingSends()
	{
		CheckDirectRouteBarrier();

		while ( pendingSends.TryPeek( out var pending ) && pending.Packets.IsCompleted )
		{
			pendingSends.Dequeue();
			if ( !pending.Packets.IsCompletedSuccessfully )
			{
				Log.Warning( pending.Packets.Exception, "Snapshot encoding failed" );
				Close( 0, "Snapshot encoding failed" );
				return;
			}
			var prepared = pending.Packets.Result;
			if ( pending.PreparedSnapshot )
			{
				if ( RoutesThroughHost )
				{
					System?.SendMulticast( [Id], Decode( prepared[0] ), pending.Flags );
					continue;
				}

				// The worker kept the complete message at index zero and prepared its chunks afterwards.
				for ( var i = prepared.Length > 1 ? 1 : 0; i < prepared.Length; i++ )
				{
					var packet = NetPacket.Wrap( prepared[i] );
					InternalSend( packet, pending.Flags );
					packet.Release();
				}
				continue;
			}

			foreach ( var data in prepared )
			{
				if ( RoutesThroughHost )
				{
					System?.SendMulticast( [Id], Decode( data ), pending.Flags );
					continue;
				}

				foreach ( var chunk in CreatePackets( data, pending.Flags ) )
				{
					var packet = NetPacket.Wrap( chunk );
					InternalSend( packet, pending.Flags );
					packet.Release();
				}
			}
		}
	}

	internal static byte[][] CreatePackets( ReadOnlySpan<byte> encoded, NetFlags flags )
	{
		if ( (flags & NetFlags.Reliable) == 0 || encoded.Length <= MaxChunkSize )
		{
			return [encoded.ToArray()];
		}

		var count = (encoded.Length + MaxChunkSize - 1) / MaxChunkSize;
		var packets = new byte[count][];
		for ( int i = 0; i < count; i++ )
		{
			var offset = i * MaxChunkSize;
			packets[i] = BuildChunkPacket( encoded.Slice( offset, Math.Min( MaxChunkSize, encoded.Length - offset ) ), i, count );
		}
		return packets;
	}
}
