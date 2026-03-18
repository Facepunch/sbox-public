using Sandbox.Network;
using System.Buffers.Binary;
using System.IO;
using System.Threading;

namespace Sandbox;

// Records incoming messages for zstd dictionary training using reservoir sampling,
// which preserves the natural distribution of message types across the session.
//
// net_msgrecord [capacity]        start (default 10000)
// net_msgrecord_stop [writeCount] stop and flush to netmsg_samples/<timestamp>/
// zstd --train netmsg_samples/<timestamp>/ -o dictionary.zstd

public static partial class Networking
{
	internal static volatile bool IsRecordingMessages;

	private static int _reservoirCapacity;
	private static long _totalSeen;
	private static string _recordDirectory;
	private static (string Category, byte[] Data)[] _reservoir = [];
	private static readonly Lock _reservoirLock = new();
	private static readonly Random _rng = new();

	internal static void TryRecordMessage( ReadOnlySpan<byte> data )
	{
		if ( !IsRecordingMessages )
			return;

		// Skip fragments and anything too large to be compressed as a single unit.
		if ( data.Length > 127 * 1024 )
			return;
		if ( data.Length > 0 && (InternalMessageType)data[0] == InternalMessageType.Chunk )
			return;

		var category = GetMessageCategory( data );
		var bytes = data.ToArray();

		lock ( _reservoirLock )
		{
			var n = ++_totalSeen;

			if ( n <= _reservoirCapacity )
				_reservoir[n - 1] = (category, bytes);
			else
			{
				var j = (long)(_rng.NextDouble() * n);
				if ( j < _reservoirCapacity )
					_reservoir[j] = (category, bytes);
			}
		}
	}

	private static string GetMessageCategory( ReadOnlySpan<byte> data )
	{
		var offset = 0;
		var msgType = (InternalMessageType)data[offset++];

		if ( msgType == InternalMessageType.Response )
		{
			if ( data.Length < offset + 17 ) return "Response";
			offset += 16;
			msgType = (InternalMessageType)data[offset++];
		}

		if ( msgType != InternalMessageType.Packed )
			return msgType.ToString();

		if ( data.Length < offset + 5 ) return "Packed";

		var typeId = BinaryPrimitives.ReadInt32LittleEndian( data.Slice( offset + 1 ) );
		return Game.TypeLibrary?.GetTypeByIdent( typeId )?.Name ?? $"Packed_{typeId:X8}";
	}

	internal static void FlushRecordedMessages( int writeCount = int.MaxValue )
	{
		IsRecordingMessages = false;

		(string Category, byte[] Data)[] snapshot;
		long totalSeen;
		lock ( _reservoirLock )
		{
			totalSeen = _totalSeen;
			snapshot = _reservoir[..(int)Math.Min( totalSeen, _reservoirCapacity )];
			_totalSeen = 0;
		}

		// Partial Fisher-Yates to subsample while preserving distribution.
		var toWrite = Math.Min( writeCount, snapshot.Length );
		for ( var i = 0; i < toWrite; i++ )
		{
			var j = i + (int)_rng.NextInt64( snapshot.Length - i );
			(snapshot[i], snapshot[j]) = (snapshot[j], snapshot[i]);
		}

		Directory.CreateDirectory( _recordDirectory );
		var counters = new Dictionary<string, int>();
		for ( var i = 0; i < toWrite; i++ )
		{
			var (category, bytes) = snapshot[i];
			File.WriteAllBytes( Path.Combine( _recordDirectory, $"msg_{i:D6}.bin" ), bytes );
			counters[category] = counters.GetValueOrDefault( category ) + 1;
		}

		Log.Info( $"Wrote {toWrite}/{totalSeen} sample(s) to: {_recordDirectory}" );
		foreach ( var (cat, cnt) in counters.OrderByDescending( x => x.Value ) )
			Log.Info( $"  {cat}: {cnt}" );
	}

	[ConCmd( "net_msgrecord", ConVarFlags.Protected )]
	internal static void Record( int capacity = 10000 )
	{
		if ( IsRecordingMessages ) { Log.Warning( "Already recording." ); return; }

		_reservoirCapacity = capacity;
		_reservoir = new (string, byte[])[capacity];
		_totalSeen = 0;
		_recordDirectory = Path.Combine( "netmsg_samples", DateTime.Now.ToString( "yyyy.MM.dd.HH.mm.ss" ) );
		IsRecordingMessages = true;

		Log.Info( $"net_msgrecord: reservoir={capacity}, output={_recordDirectory}" );
	}

	[ConCmd( "net_msgrecord_stop", ConVarFlags.Protected )]
	internal static void RecordStop( int writeCount = int.MaxValue )
	{
		if ( !IsRecordingMessages ) { Log.Warning( "Not recording." ); return; }
		FlushRecordedMessages( writeCount );
	}
}

