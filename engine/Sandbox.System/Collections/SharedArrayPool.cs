// Slightly modified copy of https://github.com/dotnet/dotnet/blob/main/src/runtime/src/libraries/System.Private.CoreLib/src/System/Buffers/SharedArrayPool.cs

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// SBOX BEGIN: upstream is inside System.Private.CoreLib so has no explicit usings for BCL types;
// we need these because we live in a different assembly and namespace.
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
// SBOX END

// SBOX BEGIN: moved to Sandbox.Internal so the type is accessible to addon code without exposing System.Buffers internals.
namespace Sandbox.Internal;
// SBOX END

#nullable enable

/// <summary>
/// Provides an ArrayPool implementation meant to be used as the singleton returned from ArrayPool.Shared.
/// </summary>
/// <remarks>
/// The implementation uses a tiered caching scheme, with a small per-thread cache for each array size, followed
/// by a cache per array size shared by all threads, split into per-core stacks meant to be used by threads
/// running on that core.  Locks are used to protect each per-core stack, because a thread can migrate after
/// checking its processor number, because multiple threads could interleave on the same core, and because
/// a thread is allowed to check other core's buckets if its core's bucket is empty/full.
/// </remarks>
internal sealed partial class SharedArrayPool<T> : ArrayPool<T>
{
	/// <summary>The number of buckets (array sizes) in the pool, one for each array length, starting from length 16.</summary>
	private const int NumBuckets = 27; // Utilities.SelectBucketIndex(1024 * 1024 * 1024 + 1)

	/// <summary>A per-thread array of arrays, to cache one array per array size per thread.</summary>
	[ThreadStatic]
	private static SharedArrayPoolThreadLocalArray[]? t_tlsBuckets;
	/// <summary>Used to keep track of all thread local buckets for trimming if needed.</summary>
	private readonly ConditionalWeakTable<SharedArrayPoolThreadLocalArray[], object?> _allTlsBuckets = new ConditionalWeakTable<SharedArrayPoolThreadLocalArray[], object?>();
	/// <summary>
	/// An array of per-core partitions. The slots are lazily initialized to avoid creating
	/// lots of overhead for unused array sizes.
	/// </summary>
	private readonly SharedArrayPoolPartitions?[] _buckets = new SharedArrayPoolPartitions[NumBuckets];
	/// <summary>Whether the callback to trim arrays in response to memory pressure has been created.</summary>
	private bool _trimCallbackCreated;

	/// <summary>Allocate a new <see cref="SharedArrayPoolPartitions"/> and try to store it into the <see cref="_buckets"/> array.</summary>
	private SharedArrayPoolPartitions CreatePerCorePartitions( int bucketIndex )
	{
		var inst = new SharedArrayPoolPartitions();
		return Interlocked.CompareExchange( ref _buckets[bucketIndex], inst, null ) ?? inst;
	}

	/// <summary>Gets an ID for the pool to use with events.</summary>
	private int Id => GetHashCode();

	public override T[] Rent( int minimumLength )
	{
		ArrayPoolEventSource log = ArrayPoolEventSource.Log;
		T[]? buffer;

		// Get the bucket number for the array length. The result may be out of range of buckets,
		// either for too large a value or for 0 and negative values.
		int bucketIndex = Utilities.SelectBucketIndex( minimumLength );

		// First, try to get an array from TLS if possible.
		SharedArrayPoolThreadLocalArray[]? tlsBuckets = t_tlsBuckets;
		if ( tlsBuckets is not null && (uint)bucketIndex < (uint)tlsBuckets.Length )
		{
			buffer = Unsafe.As<T[]?>( tlsBuckets[bucketIndex].Array );
			if ( buffer is not null )
			{
				tlsBuckets[bucketIndex].Array = null;
				if ( log.IsEnabled() )
				{
					log.BufferRented( buffer.GetHashCode(), buffer.Length, Id, bucketIndex );
				}
				return buffer;
			}
		}

		// Next, try to get an array from one of the partitions.
		SharedArrayPoolPartitions?[] perCoreBuckets = _buckets;
		if ( (uint)bucketIndex < (uint)perCoreBuckets.Length )
		{
			SharedArrayPoolPartitions? b = perCoreBuckets[bucketIndex];
			if ( b is not null )
			{
				buffer = Unsafe.As<T[]?>( b.TryPop() );
				if ( buffer is not null )
				{
					if ( log.IsEnabled() )
					{
						log.BufferRented( buffer.GetHashCode(), buffer.Length, Id, bucketIndex );
					}
					return buffer;
				}
			}

			// No buffer available.  Ensure the length we'll allocate matches that of a bucket
			// so we can later return it.
			minimumLength = Utilities.GetMaxSizeForBucket( bucketIndex );
		}
		else if ( minimumLength == 0 )
		{
			// We allow requesting zero-length arrays (even though pooling such an array isn't valuable)
			// as it's a valid length array, and we want the pool to be usable in general instead of using
			// `new`, even for computed lengths. But, there's no need to log the empty array.  Our pool is
			// effectively infinite for empty arrays and we'll never allocate for rents and never store for returns.
			return [];
		}
		else
		{
			ArgumentOutOfRangeException.ThrowIfNegative( minimumLength );
		}

		buffer = typeof( T ).IsPrimitive && typeof( T ) != typeof( bool ) ?
			GC.AllocateUninitializedArray<T>( minimumLength ) :
			new T[minimumLength];
		if ( log.IsEnabled() )
		{
			int bufferId = buffer.GetHashCode();
			log.BufferRented( bufferId, buffer.Length, Id, ArrayPoolEventSource.NoBucketId );
			log.BufferAllocated( bufferId, buffer.Length, Id, ArrayPoolEventSource.NoBucketId, bucketIndex >= _buckets.Length ?
				ArrayPoolEventSource.BufferAllocatedReason.OverMaximumSize :
				ArrayPoolEventSource.BufferAllocatedReason.PoolExhausted );
		}
		return buffer;
	}

	public override void Return( T[] array, bool clearArray = false )
	{
		if ( array is null )
		{
			// SBOX BEGIN: upstream calls ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array) which is internal
			throw new ArgumentNullException( nameof( array ) );
			// SBOX END
		}

		// Determine with what bucket this array length is associated
		int bucketIndex = Utilities.SelectBucketIndex( array.Length );

		// Make sure our TLS buckets are initialized.  Technically we could avoid doing
		// this if the array being returned is erroneous or too large for the pool, but the
		// former condition is an error we don't need to optimize for, and the latter is incredibly
		// rare, given a max size of 1B elements.
		SharedArrayPoolThreadLocalArray[] tlsBuckets = t_tlsBuckets ?? InitializeTlsBucketsAndTrimming();

		bool haveBucket = false;
		bool returned = true;
		if ( (uint)bucketIndex < (uint)tlsBuckets.Length )
		{
			haveBucket = true;

			// Clear the array if the user requested it.
			if ( clearArray )
			{
				Array.Clear( array );
			}

			// Check to see if the buffer is the correct size for this bucket.
			if ( array.Length != Utilities.GetMaxSizeForBucket( bucketIndex ) )
			{
				// SBOX BEGIN: upstream calls ThrowHelper.ThrowArgumentException(ExceptionResource.ArgumentException_BufferNotFromPool, ExceptionArgument.array) which is internal
				throw new ArgumentException( "The buffer is not associated with this pool and may not be returned to it.", nameof( array ) );
				// SBOX END
			}

			// Store the array into the TLS bucket.  If there's already an array in it,
			// push that array down into the partitions, preferring to keep the latest
			// one in TLS for better locality.
			ref SharedArrayPoolThreadLocalArray tla = ref tlsBuckets[bucketIndex];
			Array? prev = tla.Array;
			tla = new SharedArrayPoolThreadLocalArray( array );
			if ( prev is not null )
			{
				SharedArrayPoolPartitions partitionsForArraySize = _buckets[bucketIndex] ?? CreatePerCorePartitions( bucketIndex );
				returned = partitionsForArraySize.TryPush( prev );
			}
		}

		// Log that the buffer was returned
		ArrayPoolEventSource log = ArrayPoolEventSource.Log;
		if ( log.IsEnabled() && array.Length != 0 )
		{
			log.BufferReturned( array.GetHashCode(), array.Length, Id );
			if ( !(haveBucket & returned) )
			{
				log.BufferDropped( array.GetHashCode(), array.Length, Id,
					haveBucket ? bucketIndex : ArrayPoolEventSource.NoBucketId,
					haveBucket ? ArrayPoolEventSource.BufferDroppedReason.Full : ArrayPoolEventSource.BufferDroppedReason.OverMaximumSize );
			}
		}
	}

	public bool Trim()
	{
		int currentMilliseconds = Environment.TickCount;
		Utilities.MemoryPressure pressure = Utilities.GetMemoryPressure();

		// Log that we're trimming.
		ArrayPoolEventSource log = ArrayPoolEventSource.Log;
		if ( log.IsEnabled() )
		{
			log.BufferTrimPoll( currentMilliseconds, (int)pressure );
		}

		// Trim each of the per-core buckets.
		SharedArrayPoolPartitions?[] perCoreBuckets = _buckets;
		for ( int i = 0; i < perCoreBuckets.Length; i++ )
		{
			perCoreBuckets[i]?.Trim( currentMilliseconds, Id, pressure );
		}

		// Trim each of the TLS buckets. Note that threads may be modifying their TLS slots concurrently with
		// this trimming happening. We do not force synchronization with those operations, so we accept the fact
		// that we may end up firing a trimming event even if an array wasn't trimmed, and potentially
		// trim an array we didn't need to.  Both of these should be rare occurrences.

		// Under high pressure, release all thread locals.
		if ( pressure == Utilities.MemoryPressure.High )
		{
			if ( !log.IsEnabled() )
			{
				foreach ( KeyValuePair<SharedArrayPoolThreadLocalArray[], object?> tlsBuckets in _allTlsBuckets )
				{
					Array.Clear( tlsBuckets.Key );
				}
			}
			else
			{
				foreach ( KeyValuePair<SharedArrayPoolThreadLocalArray[], object?> tlsBuckets in _allTlsBuckets )
				{
					SharedArrayPoolThreadLocalArray[] buckets = tlsBuckets.Key;
					for ( int i = 0; i < buckets.Length; i++ )
					{
						if ( Interlocked.Exchange( ref buckets[i].Array, null ) is T[] buffer )
						{
							log.BufferTrimmed( buffer.GetHashCode(), buffer.Length, Id );
						}
					}
				}
			}
		}
		else
		{
			// Otherwise, release thread locals based on how long we've observed them to be stored. This time is
			// approximate, with the time set not when the array is stored but when we see it during a Trim, so it
			// takes at least two Trim calls (and thus two gen2 GCs) to drop an array, unless we're in high memory
			// pressure. These values have been set arbitrarily; we could tune them in the future.
			uint millisecondsThreshold = pressure switch
			{
				Utilities.MemoryPressure.Medium => 15_000,
				_ => 30_000,
			};

			foreach ( KeyValuePair<SharedArrayPoolThreadLocalArray[], object?> tlsBuckets in _allTlsBuckets )
			{
				SharedArrayPoolThreadLocalArray[] buckets = tlsBuckets.Key;
				for ( int i = 0; i < buckets.Length; i++ )
				{
					if ( buckets[i].Array is null )
					{
						continue;
					}

					// We treat 0 to mean it hasn't yet been seen in a Trim call. In the very rare case where Trim records 0,
					// it'll take an extra Trim call to remove the array.
					int lastSeen = buckets[i].MillisecondsTimeStamp;
					if ( lastSeen == 0 )
					{
						buckets[i].MillisecondsTimeStamp = currentMilliseconds;
					}
					else if ( (currentMilliseconds - lastSeen) >= millisecondsThreshold )
					{
						// Time noticeably wrapped, or we've surpassed the threshold.
						// Clear out the array, and log its being trimmed if desired.
						if ( Interlocked.Exchange( ref buckets[i].Array, null ) is T[] buffer &&
							log.IsEnabled() )
						{
							log.BufferTrimmed( buffer.GetHashCode(), buffer.Length, Id );
						}
					}
				}
			}
		}

		return true;
	}

	private SharedArrayPoolThreadLocalArray[] InitializeTlsBucketsAndTrimming()
	{
		Debug.Assert( t_tlsBuckets is null, $"Non-null {nameof( t_tlsBuckets )}" );

		var tlsBuckets = new SharedArrayPoolThreadLocalArray[NumBuckets];
		t_tlsBuckets = tlsBuckets;

		_allTlsBuckets.Add( tlsBuckets, null );
		if ( !Interlocked.Exchange( ref _trimCallbackCreated, true ) )
		{
			// SBOX BEGIN: Gen2GcCallback is internal to System.Private.CoreLib; call via cached reflection delegate
			SharedArrayPoolStatics.s_gen2GcCallbackRegister?.Invoke( s => ((SharedArrayPool<T>)s).Trim(), this );
			// SBOX END
		}

		return tlsBuckets;
	}

	// The following partition types are separated out of SharedArrayPool<T> to avoid
	// them being generic, in order to avoid the generic code size increase that would
	// result, in particular for Native AOT. The only thing that's necessary to actually
	// be generic is the return type of TryPop, and we can handle that at the access
	// site with a well-placed Unsafe.As.

	/// <summary>Wrapper for arrays stored in ThreadStatic buckets.</summary>
	internal struct SharedArrayPoolThreadLocalArray
	{
		/// <summary>The stored array.</summary>
		public Array? Array;
		/// <summary>Environment.TickCount timestamp for when this array was observed by Trim.</summary>
		public int MillisecondsTimeStamp;

		public SharedArrayPoolThreadLocalArray( Array array )
		{
			Array = array;
			MillisecondsTimeStamp = 0;
		}
	}
}

/// <summary>Provides a collection of partitions, each of which is a pool of arrays.</summary>
internal sealed class SharedArrayPoolPartitions
{
	/// <summary>The partitions.</summary>
	private readonly Partition[] _partitions;

	/// <summary>Initializes the partitions.</summary>
	public SharedArrayPoolPartitions()
	{
		// Create the partitions.  We create as many as there are processors, limited by our max.
		var partitions = new Partition[SharedArrayPoolStatics.s_partitionCount];
		for ( int i = 0; i < partitions.Length; i++ )
		{
			partitions[i] = new Partition();
		}
		_partitions = partitions;
	}

	/// <summary>
	/// Try to push the array into any partition with available space, starting with partition associated with the current core.
	/// If all partitions are full, the array will be dropped.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool TryPush( Array array )
	{
		// Try to push on to the associated partition first.  If that fails,
		// round-robin through the other partitions.
		Partition[] partitions = _partitions;
		int index = (int)((uint)Thread.GetCurrentProcessorId() % (uint)SharedArrayPoolStatics.s_partitionCount); // mod by constant in tier 1
		for ( int i = 0; i < partitions.Length; i++ )
		{
			if ( partitions[index].TryPush( array ) ) return true;
			if ( ++index == partitions.Length ) index = 0;
		}

		return false;
	}

	/// <summary>
	/// Try to pop an array from any partition with available arrays, starting with partition associated with the current core.
	/// If all partitions are empty, null is returned.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Array? TryPop()
	{
		// Try to pop from the associated partition first.  If that fails, round-robin through the other partitions.
		Array? arr;
		Partition[] partitions = _partitions;
		int index = (int)((uint)Thread.GetCurrentProcessorId() % (uint)SharedArrayPoolStatics.s_partitionCount); // mod by constant in tier 1
		for ( int i = 0; i < partitions.Length; i++ )
		{
			if ( (arr = partitions[index].TryPop()) is not null ) return arr;
			if ( ++index == partitions.Length ) index = 0;
		}
		return null;
	}

	public void Trim( int currentMilliseconds, int id, Utilities.MemoryPressure pressure )
	{
		Partition[] partitions = _partitions;
		for ( int i = 0; i < partitions.Length; i++ )
		{
			partitions[i].Trim( currentMilliseconds, id, pressure );
		}
	}

	/// <summary>Provides a simple, bounded stack of arrays, protected by a lock.</summary>
	private sealed class Partition
	{
		/// <summary>The arrays in the partition.</summary>
		private readonly Array?[] _arrays = new Array[SharedArrayPoolStatics.s_maxArraysPerPartition];
		/// <summary>Number of arrays stored in <see cref="_arrays"/>.</summary>
		private int _count;
		/// <summary>Timestamp set by Trim when it sees this as 0.</summary>
		private int _millisecondsTimestamp;

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public bool TryPush( Array array )
		{
			bool enqueued = false;
			Monitor.Enter( this );
			Array?[] arrays = _arrays;
			int count = _count;
			if ( (uint)count < (uint)arrays.Length )
			{
				if ( count == 0 )
				{
					// Reset the time stamp now that we're transitioning from empty to non-empty.
					// Trim will see this as 0 and initialize it to the current time when Trim is called.
					_millisecondsTimestamp = 0;
				}

				Unsafe.Add( ref MemoryMarshal.GetArrayDataReference( arrays ), count ) = array; // arrays[count] = array, but avoiding stelemref
				_count = count + 1;
				enqueued = true;
			}
			Monitor.Exit( this );
			return enqueued;
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public Array? TryPop()
		{
			Array? arr = null;
			Monitor.Enter( this );
			Array?[] arrays = _arrays;
			int count = _count - 1;
			if ( (uint)count < (uint)arrays.Length )
			{
				arr = arrays[count];
				arrays[count] = null;
				_count = count;
			}
			Monitor.Exit( this );
			return arr;
		}

		public void Trim( int currentMilliseconds, int id, Utilities.MemoryPressure pressure )
		{
			const int TrimAfterMS = 60 * 1000;                                  // Trim after 60 seconds for low/moderate pressure
			const int HighTrimAfterMS = 10 * 1000;                              // Trim after 10 seconds for high pressure

			if ( _count == 0 )
			{
				return;
			}

			int trimMilliseconds = pressure == Utilities.MemoryPressure.High ? HighTrimAfterMS : TrimAfterMS;

			lock ( this )
			{
				if ( _count == 0 )
				{
					return;
				}

				if ( _millisecondsTimestamp == 0 )
				{
					_millisecondsTimestamp = currentMilliseconds;
					return;
				}

				if ( (currentMilliseconds - _millisecondsTimestamp) <= trimMilliseconds )
				{
					return;
				}

				// We've elapsed enough time since the first item went into the partition.
				// Drop the top item(s) so they can be collected.

				int trimCount = pressure switch
				{
					Utilities.MemoryPressure.High => SharedArrayPoolStatics.s_maxArraysPerPartition,
					Utilities.MemoryPressure.Medium => 2,
					_ => 1,
				};

				ArrayPoolEventSource log = ArrayPoolEventSource.Log;
				while ( _count > 0 && trimCount-- > 0 )
				{
					Array? array = _arrays[--_count];
					Debug.Assert( array is not null, "No nulls should have been present in slots < _count." );
					_arrays[_count] = null;

					if ( log.IsEnabled() )
					{
						log.BufferTrimmed( array.GetHashCode(), array.Length, id );
					}
				}

				_millisecondsTimestamp = _count > 0 ?
					_millisecondsTimestamp + (trimMilliseconds / 4) : // Give the remaining items a bit more time
					0;
			}
		}
	}
}

internal static class SharedArrayPoolStatics
{
	/// <summary>Number of partitions to employ.</summary>
	internal static readonly int s_partitionCount = GetPartitionCount();
	/// <summary>The maximum number of arrays per array size to store per partition.</summary>
	internal static readonly int s_maxArraysPerPartition = GetMaxArraysPerPartition();

	// SBOX BEGIN: Gen2GcCallback.Register is internal to System.Private.CoreLib.
	// Cache a strongly-typed delegate so reflection cost is paid exactly once, and all SharedArrayPool<T>
	// instantiations share the same delegate regardless of T.
	internal static readonly Action<Func<object, bool>, object>? s_gen2GcCallbackRegister = CreateGen2GcCallbackRegister();

	private static Action<Func<object, bool>, object>? CreateGen2GcCallbackRegister()
	{
		try
		{
			var type = Type.GetType( "System.Gen2GcCallback, System.Private.CoreLib" );
			var method = type?.GetMethod( "Register",
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
			if ( method is null ) return null;
			return (Action<Func<object, bool>, object>)Delegate.CreateDelegate(
				typeof( Action<Func<object, bool>, object> ), method );
		}
		catch
		{
			// The internal API changed shape or is unavailable in this runtime version.
			// Trimming will simply not run, which is non-critical.
			return null;
		}
	}
	// SBOX END

	/// <summary>Gets the maximum number of partitions to shard arrays into.</summary>
	/// <remarks>Defaults to int.MaxValue.  Whatever value is returned will end up being clamped to <see cref="Environment.ProcessorCount"/>.</remarks>
	private static int GetPartitionCount()
	{
		int partitionCount = TryGetInt32EnvironmentVariable( "DOTNET_SYSTEM_BUFFERS_SHAREDARRAYPOOL_MAXPARTITIONCOUNT", out int result ) && result > 0 ?
			result :
			int.MaxValue; // no limit other than processor count
		return Math.Min( partitionCount, Environment.ProcessorCount );
	}

	/// <summary>Gets the maximum number of arrays of a given size allowed to be cached per partition.</summary>
	/// <returns>Defaults to 32. This does not factor in or impact the number of arrays cached per thread in TLS (currently only 1).</returns>
	private static int GetMaxArraysPerPartition()
	{
		return TryGetInt32EnvironmentVariable( "DOTNET_SYSTEM_BUFFERS_SHAREDARRAYPOOL_MAXARRAYSPERPARTITION", out int result ) && result > 0 ?
			result :
			32; // arbitrary limit
	}

	/// <summary>Look up an environment variable and try to parse it as an Int32.</summary>
	/// <remarks>This avoids using anything that might in turn recursively use the ArrayPool.</remarks>
	private static bool TryGetInt32EnvironmentVariable( string variable, out int result )
	{
		// Avoid globalization stack, as it might in turn be using ArrayPool.

		// SBOX BEGIN: upstream calls Environment.GetEnvironmentVariableCore_NoArrayPool which is internal to
		// System.Private.CoreLib. We can't call it, so we always return false and use the hardcoded defaults.
		// If env-var overrides are ever needed, replace this with a standard Environment.GetEnvironmentVariable
		// call and accept the slight risk of a recursive pool use during startup.
		//if ( Environment.GetEnvironmentVariableCore_NoArrayPool( variable ) is string envVar &&
		//	envVar.Length is > 0 and <= 32 ) // arbitrary limit that allows for some spaces around the maximum length of a non-negative Int32 (10 digits)
		//{
		//	ReadOnlySpan<char> value = envVar.AsSpan().Trim( ' ' );
		//	if ( !value.IsEmpty && value.Length <= 10 )
		//	{
		//		long tempResult = 0;
		//		foreach ( char c in value )
		//		{
		//			uint digit = (uint)(c - '0');
		//			if ( digit > 9 )
		//			{
		//				goto Fail;
		//			}

		//			tempResult = tempResult * 10 + digit;
		//		}

		//		if ( tempResult is >= 0 and <= int.MaxValue )
		//		{
		//			result = (int)tempResult;
		//			return true;
		//		}
		//	}
		//}

		//Fail:
		result = 0;
		return false;
		// SBOX END
	}
}

[EventSource( Guid = "0866B2B8-5CEF-5DB9-2612-0C0FFD814A44", Name = "System.Buffers.ArrayPoolEventSource" )]
internal sealed partial class ArrayPoolEventSource : EventSource
{
	private const string EventSourceSuppressMessage = "Parameters to this method are primitive and are trimmer safe";
	internal static readonly ArrayPoolEventSource Log = new ArrayPoolEventSource();

	/// <summary>Bucket ID used when renting/returning an array that's too large for a pool.</summary>
	internal const int NoBucketId = -1;

	/// <summary>The reason for a BufferAllocated event.</summary>
	internal enum BufferAllocatedReason : int
	{
		/// <summary>The pool is allocating a buffer to be pooled in a bucket.</summary>
		Pooled,
		/// <summary>The requested buffer size was too large to be pooled.</summary>
		OverMaximumSize,
		/// <summary>The pool has already allocated for pooling as many buffers of a particular size as it's allowed.</summary>
		PoolExhausted
	}

	/// <summary>The reason for a BufferDropped event.</summary>
	internal enum BufferDroppedReason : int
	{
		/// <summary>The pool is full for buffers of the specified size.</summary>
		Full,
		/// <summary>The buffer size was too large to be pooled.</summary>
		OverMaximumSize,
	}

	/// <summary>
	/// Event for when a buffer is rented.  This is invoked once for every successful call to Rent,
	/// regardless of whether a buffer is allocated or a buffer is taken from the pool.  In a
	/// perfect situation where all rented buffers are returned, we expect to see the number
	/// of BufferRented events exactly match the number of BuferReturned events, with the number
	/// of BufferAllocated events being less than or equal to those numbers (ideally significantly
	/// less than).
	/// </summary>
	[UnconditionalSuppressMessage( "ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
				Justification = EventSourceSuppressMessage )]
	[System.Diagnostics.Tracing.Event( 1, Level = EventLevel.Verbose )]
	internal unsafe void BufferRented( int bufferId, int bufferSize, int poolId, int bucketId )
	{
		EventData* payload = stackalloc EventData[4];

		// Need to 0 reserved field of EventData
		Unsafe.InitBlock( payload, 0, (uint)(sizeof( EventData ) * 4) );

		payload[0].Size = sizeof( int );
		payload[0].DataPointer = ((IntPtr)(&bufferId));
		payload[1].Size = sizeof( int );
		payload[1].DataPointer = ((IntPtr)(&bufferSize));
		payload[2].Size = sizeof( int );
		payload[2].DataPointer = ((IntPtr)(&poolId));
		payload[3].Size = sizeof( int );
		payload[3].DataPointer = ((IntPtr)(&bucketId));
		WriteEventCore( 1, 4, payload );
	}

	/// <summary>
	/// Event for when a buffer is allocated by the pool.  In an ideal situation, the number
	/// of BufferAllocated events is significantly smaller than the number of BufferRented and
	/// BufferReturned events.
	/// </summary>
	[UnconditionalSuppressMessage( "ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
				Justification = EventSourceSuppressMessage )]
	[System.Diagnostics.Tracing.Event( 2, Level = EventLevel.Informational )]
	internal unsafe void BufferAllocated( int bufferId, int bufferSize, int poolId, int bucketId, BufferAllocatedReason reason )
	{
		EventData* payload = stackalloc EventData[5];

		// Need to 0 reserved field of EventData
		Unsafe.InitBlock( payload, 0, (uint)(sizeof( EventData ) * 5) );

		payload[0].Size = sizeof( int );
		payload[0].DataPointer = ((IntPtr)(&bufferId));
		payload[1].Size = sizeof( int );
		payload[1].DataPointer = ((IntPtr)(&bufferSize));
		payload[2].Size = sizeof( int );
		payload[2].DataPointer = ((IntPtr)(&poolId));
		payload[3].Size = sizeof( int );
		payload[3].DataPointer = ((IntPtr)(&bucketId));
		payload[4].Size = sizeof( BufferAllocatedReason );
		payload[4].DataPointer = ((IntPtr)(&reason));
		WriteEventCore( 2, 5, payload );
	}

	/// <summary>
	/// Event raised when a buffer is returned to the pool.  This event is raised regardless of whether
	/// the returned buffer is stored or dropped.  In an ideal situation, the number of BufferReturned
	/// events exactly matches the number of BufferRented events.
	/// </summary>
	[System.Diagnostics.Tracing.Event( 3, Level = EventLevel.Verbose )]
	internal void BufferReturned( int bufferId, int bufferSize, int poolId ) => WriteEvent( 3, bufferId, bufferSize, poolId );

	/// <summary>
	/// Event raised when we attempt to free a buffer due to inactivity or memory pressure (by no longer
	/// referencing it). It is possible (although not common) this buffer could be rented as we attempt
	/// to free it. A rent event before or after this event for the same ID, is a rare, but expected case.
	/// </summary>
	[System.Diagnostics.Tracing.Event( 4, Level = EventLevel.Informational )]
	internal void BufferTrimmed( int bufferId, int bufferSize, int poolId ) => WriteEvent( 4, bufferId, bufferSize, poolId );

	/// <summary>
	/// Event raised when we check to trim buffers.
	/// </summary>
	[System.Diagnostics.Tracing.Event( 5, Level = EventLevel.Informational )]
	internal void BufferTrimPoll( int milliseconds, int pressure ) => WriteEvent( 5, milliseconds, pressure );

	/// <summary>
	/// Event raised when a buffer returned to the pool is dropped.
	/// </summary>
	[UnconditionalSuppressMessage( "ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
				Justification = EventSourceSuppressMessage )]
	[System.Diagnostics.Tracing.Event( 6, Level = EventLevel.Informational )]
	internal unsafe void BufferDropped( int bufferId, int bufferSize, int poolId, int bucketId, BufferDroppedReason reason )
	{
		EventData* payload = stackalloc EventData[5];

		// Need to 0 reserved field of EventData
		Unsafe.InitBlock( payload, 0, (uint)(sizeof( EventData ) * 5) );

		payload[0].Size = sizeof( int );
		payload[0].DataPointer = ((IntPtr)(&bufferId));
		payload[1].Size = sizeof( int );
		payload[1].DataPointer = ((IntPtr)(&bufferSize));
		payload[2].Size = sizeof( int );
		payload[2].DataPointer = ((IntPtr)(&poolId));
		payload[3].Size = sizeof( int );
		payload[3].DataPointer = ((IntPtr)(&bucketId));
		payload[4].Size = sizeof( BufferDroppedReason );
		payload[4].DataPointer = ((IntPtr)(&reason));
		WriteEventCore( 6, 5, payload );
	}
}

internal static class Utilities
{
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static int SelectBucketIndex( int bufferSize )
	{
		// Buffers are bucketed so that a request between 2^(n-1) + 1 and 2^n is given a buffer of 2^n
		// Bucket index is log2(bufferSize - 1) with the exception that buffers between 1 and 16 bytes
		// are combined, and the index is slid down by 3 to compensate.
		// Zero is a valid bufferSize, and it is assigned the highest bucket index so that zero-length
		// buffers are not retained by the pool. The pool will return the Array.Empty singleton for these.
		return BitOperations.Log2( (uint)bufferSize - 1 | 15 ) - 3;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static int GetMaxSizeForBucket( int binIndex )
	{
		int maxSize = 16 << binIndex;
		Debug.Assert( maxSize >= 0 );
		return maxSize;
	}

	internal enum MemoryPressure
	{
		Low,
		Medium,
		High
	}

	internal static MemoryPressure GetMemoryPressure()
	{
		const double HighPressureThreshold = .90;       // Percent of GC memory pressure threshold we consider "high"
		const double MediumPressureThreshold = .70;     // Percent of GC memory pressure threshold we consider "medium"

		GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();

		if ( memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * HighPressureThreshold )
		{
			return MemoryPressure.High;
		}

		if ( memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * MediumPressureThreshold )
		{
			return MemoryPressure.Medium;
		}

		return MemoryPressure.Low;
	}
}
