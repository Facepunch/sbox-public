using Sandbox.Network;
using System.Threading;

namespace Sandbox;

public abstract partial class Connection
{
	// Owned and drained by the main thread. Workers never access the connection or transport.
	readonly Queue<(Task<byte[][]> Packets, NetFlags Flags)> pendingSends = new();
	bool sendsClosed;
	CancellationTokenSource snapshotCancellation;
	internal bool HasPendingSends => pendingSends.Count > 0;

	internal void SendSnapshot<T>( SnapshotCapture capture, Func<SnapshotMsg, T> envelope, NetFlags flags )
	{
		if ( sendsClosed )
		{
			return;
		}
		if ( this is MockConnection )
		{
			SendMessage( envelope( capture.Materialize() ), flags );
			return;
		}

		var serializer = System.TypeLibrary.CreateDetachedSerializer( typeof( T ), typeof( SnapshotMsg ),
			typeof( SnapshotMsg.GameObjectSystemData ), typeof( ObjectCreateMsg ) );
		var cancellation = (snapshotCancellation ??= new()).Token;
		QueueSend( Task.Run( () =>
		{
			try
			{
				cancellation.ThrowIfCancellationRequested();
				return capture.Encode( serializer, envelope, flags );
			}
			finally
			{
				serializer.Dispose();
			}
		} ), flags );
	}

	internal void QueueSend( Task<byte[][]> packets, NetFlags flags )
	{
		// Observe failures even when the connection closes before the job completes.
		_ = packets.ContinueWith( t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted );
		if ( !sendsClosed )
		{
			pendingSends.Enqueue( (packets, flags) );
		}
	}

	internal void FlushPendingSends()
	{
		while ( pendingSends.TryPeek( out var pending ) && pending.Packets.IsCompleted )
		{
			pendingSends.Dequeue();
			if ( !pending.Packets.IsCompletedSuccessfully )
			{
				Log.Warning( pending.Packets.Exception, "Snapshot encoding failed" );
				Close( 0, "Snapshot encoding failed" );
				return;
			}
			foreach ( var packet in pending.Packets.Result )
			{
				InternalSend( packet, pending.Flags );
			}
		}
	}

	internal static byte[][] CreatePackets( byte[] encoded, NetFlags flags )
	{
		if ( (flags & NetFlags.Reliable) == 0 || encoded.Length <= MaxChunkSize )
		{
			return [encoded];
		}

		var count = (encoded.Length + MaxChunkSize - 1) / MaxChunkSize;
		var packets = new byte[count][];
		for ( int i = 0; i < count; i++ )
		{
			var offset = i * MaxChunkSize;
			packets[i] = BuildChunkPacket( encoded, offset, Math.Min( MaxChunkSize, encoded.Length - offset ), i, count );
		}
		return packets;
	}
}
