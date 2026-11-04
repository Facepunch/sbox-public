using Sandbox;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// We only want 1 instance of a Resource class in C# and we want that to have 1 strong handle to native.
/// So we need a WeakReference lookup everytime we get a Resource from native to match that class.
/// This way GC can work for us and free anything we're no longer using anywhere, fantastic!
/// 
/// However sometimes GC is very good at it's job and will free Resources we don't keep a strong reference to
/// in generation 0 or 1 immediately after usage. This can cause the resource to need to be loaded every frame.
/// Or worse be finalized at unpredictable times.
/// 
/// So we hold a strong reference to recently touched Resources for a few seconds - realistically
/// these only need to live for an extra frame, but it's nice to keep them around a little longer
/// if they're going to be used on and off.
/// </summary>
internal static partial class NativeResourceCache
{
	const int ExpirationSeconds = 5;

	/// <summary>
	/// Recently touched resources, and the batch before. Each swap drops the older batch, so
	/// retention is one to two ExpirationSeconds - enough that something fetched every frame
	/// isn't collected and rebuilt in between.
	/// </summary>
	static ConcurrentDictionary<long, object> retained = new();
	static ConcurrentDictionary<long, object> retainedPrevious = new();

	/// <summary>
	/// We still want a WeakReference cache because we might have a strong reference somewhere to a resource
	/// that has been expired from the cache. And we absolutely only want 1 instance of the resource.
	/// </summary>
	static readonly ConcurrentDictionary<long, WeakReference> WeakTable = new();

	/// <summary>
	/// When <see cref="LeakTracking"/> is enabled, stores the callstack captured at the time each resource
	/// was added to the cache. This prevents WeakTable pruning so every allocation remains visible at
	/// shutdown.
	/// </summary>
	static readonly ConcurrentDictionary<long, string> CallstackTable = new();

	/// <summary>
	/// When enabled, disables WeakTable entry pruning and captures allocation callstacks so that
	/// resource leaks can be diagnosed at shutdown with full context.
	/// </summary>
	[ConVar( "resource_leak_tracking" )]
	public static bool LeakTracking
	{
		get => field;
		set
		{
			if ( value && !field )
			{
				Log.Warning( "resource_leak_tracking enabled — resources allocated before this point will not have callstacks and may not appear in the shutdown leak report." );
			}
			field = value;
		}
	} = false;

	internal static void Add( long key, object value )
	{
		KeepAlive( key, value );

		WeakTable[key] = new WeakReference( value );

		if ( LeakTracking )
		{
			CallstackTable[key] = new System.Diagnostics.StackTrace( skipFrames: 1, fNeedFileInfo: false ).ToString();
		}
	}

	/// <summary>Hold a strong reference until at least the next swap.</summary>
	static void KeepAlive( long key, object value ) => retained[key] = value;

	/// <summary>
	/// Remove a key. Used when a resource is explicitly disposed so that a new instance can be
	/// created for the same native pointer.
	/// </summary>
	internal static void Remove( long key )
	{
		// Explicitly disposed, so drop the strong reference now rather than at the next swap.
		retained.TryRemove( key, out _ );
		retainedPrevious.TryRemove( key, out _ );

		WeakTable.TryRemove( key, out _ );
		CallstackTable.TryRemove( key, out _ );
	}

	/// <summary>
	/// Look up without renewing retention, for anything that walks every resource - a plain
	/// TryGetValue would touch the whole set and keep all of it alive.
	/// </summary>
	internal static bool TryPeek<T>( long key, out T value ) where T : class
	{
		value = null;

		if ( WeakTable.TryGetValue( key, out var weakValue ) && weakValue.Target is T target )
		{
			value = target;
			return true;
		}

		return false;
	}

	internal static bool TryGetValue<T>( long key, out T value ) where T : class
	{
		value = null;

		// Read Target once to avoid TOCTOU race with GC.
		if ( WeakTable.TryGetValue( key, out var weakValue ) && weakValue.Target is T target )
		{
			value = target;

			// Touching it renews retention, so anything in active use stays put.
			KeepAlive( key, target );

			return true;
		}

		return false;
	}

	// Expiry has to stop when renewals do. Only a paused game qualifies: nothing ticks, so
	// nothing renews. Everywhere else - playing, editor stopped, menu - still fetches every
	// frame. Was a TimeSince, which froze whenever the editor wasn't playing, so nothing expired.
	static double expiryClock;

	/// <summary>
	/// Expires resources we haven't touched recently, and prunes dead weak entries.
	/// </summary>
	internal static void Tick()
	{
		expiryClock += Game.IsPaused ? 0 : RealTime.Delta;

		if ( expiryClock < ExpirationSeconds )
			return;

		expiryClock = 0;

		// Anything not touched since the last swap loses its strong reference here.
		retainedPrevious = Interlocked.Exchange( ref retained, new ConcurrentDictionary<long, object>() );

		// Prune dead WeakTable entries to prevent unbounded growth from procedural resources.
		// Skipped when leak tracking is enabled so every allocation stays visible until shutdown.
		if ( !LeakTracking )
		{
			foreach ( var kvp in WeakTable )
			{
				if ( kvp.Value.Target is null )
				{
					WeakTable.TryRemove( kvp.Key, out _ );
				}
			}
		}

	}

	/// <summary>
	/// Returns stats about the NativeResourceCache for debug overlays.
	/// </summary>
	internal static NativeCacheStats GetStats()
	{
		var stats = new NativeCacheStats();

		foreach ( var kvp in WeakTable )
		{
			var target = kvp.Value.Target;
			var alive = target is not null;
			var typeName = alive ? target.GetType().Name : "(dead)";
			stats.Entries.TryGetValue( typeName, out var count );
			stats.Entries[typeName] = count + 1;
		}

		stats.WeakTableTotal = WeakTable.Count;
		// Can over-count: a key touched again after a swap is in both batches. Only a debug
		// readout, not worth walking the whole set to deduplicate.
		stats.MemoryCacheCount = retained.Count + retainedPrevious.Count;

		return stats;
	}

	internal struct NativeCacheStats
	{
		public Dictionary<string, int> Entries;
		public int WeakTableTotal;
		public int MemoryCacheCount;

		public NativeCacheStats()
		{
			Entries = new();
		}
	}

	/// <summary>
	/// Clear the cache when games are closed etc. ready for a <see cref="GC.Collect()"/>
	/// </summary>
	internal static void Clear()
	{
		ClearCache();

		// When leak tracking is enabled, preserve the WeakTable and CallstackTable across
		// game resets so that resources allocated before the reset remain visible to HandleShutdownLeaks() at shutdown.
		if ( !LeakTracking ) ClearWeakTable();
	}

	internal static void ClearCache()
	{
		retained = new ConcurrentDictionary<long, object>();
		retainedPrevious = new ConcurrentDictionary<long, object>();
	}

	private static void ClearWeakTable()
	{
		WeakTable.Clear();
		CallstackTable.Clear();
	}

	internal static void HandleShutdownLeaks()
	{
		int leaks = 0;

		foreach ( var kvp in WeakTable )
		{
			if ( kvp.Value.Target is Resource resource && resource.IsValid() )
			{
				var resourceName = resource.ResourceName;
				ulong resourceId = resource.ResourceIdLong;

				if ( resource is Texture tex )
				{
					resourceName = "RenderTarget";
					resourceId = (ulong)tex.native.self; // Texture resources can be render targets, which have a unique native pointer but have no name or path.
				}
				Log.Warning( $"NativeResourceCache: Resource still alive during shutown, this can indicate a leak: {resource.GetType().Name} [{resourceId}] {resourceName} ({resource.ResourcePath}) will be force destroyed." );
				if ( LeakTracking && CallstackTable.TryGetValue( kvp.Key, out var callstack ) )
				{
					Log.Warning( $"NativeResourceCache: Allocation callstack:\n{callstack}" );
				}
				leaks++;
			}
		}

		if ( leaks > 0 ) Log.Warning( $"NativeResourceCache: Total leaks: {leaks}" );

		// Force destory all resources and than clear the cache to prevent any resurrected resources from being reported as leaks.
		foreach ( var kvp in WeakTable )
		{
			if ( kvp.Value.Target is Resource resource && resource.IsValid() )
			{
				resource.Destroy();
			}
		}

		ClearWeakTable();
	}
}
