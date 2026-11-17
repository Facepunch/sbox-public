using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Gizmo
{
	/// <summary>
	/// Strings reused across instance pushes. Every <see cref="MaxIdleFrames"/> pushes, entries unused for that long are dropped;
	/// and once the cache has doubled since its last sweep, entries that weren't used in the latest push are dropped, so names
	/// that change every push (counters, Guids) can't grow it without bound
	/// </summary>
	sealed class StringCache<TKey>( IEqualityComparer<TKey> comparer )
	{
		/// <summary>
		/// Smallest size a growth sweep runs at, so small caches of live names aren't swept every few pushes
		/// </summary>
		const int MinSweepCount = 1024;

		/// <summary>
		/// How often, in pushes, entries unused for this long are dropped, so names left behind when a scene shrinks are released
		/// </summary>
		const int MaxIdleFrames = 64;

		struct Entry
		{
			public string Value;
			public int Frame;
		}

		readonly Dictionary<TKey, Entry> _entries = new( comparer );
		int _frame;
		int _sweepAt = MinSweepCount;

		public string Get( TKey key, Func<TKey, string> create )
		{
			ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault( _entries, key, out _ );
			entry.Value ??= create( key );
			entry.Frame = _frame;
			return entry.Value;
		}

		public void NextFrame()
		{
			if ( _entries.Count > _sweepAt )
				Sweep( 0 );
			else if ( _frame % MaxIdleFrames == 0 )
				Sweep( MaxIdleFrames );

			_frame++;
		}

		/// <summary>
		/// Drop entries that haven't been used for more than <paramref name="maxIdle"/> pushes before the current one
		/// </summary>
		void Sweep( int maxIdle )
		{
			foreach ( var (key, entry) in _entries )
			{
				if ( _frame - entry.Frame > maxIdle )
					_entries.Remove( key );
			}

			_sweepAt = Math.Max( MinSweepCount, _entries.Count * 2 );
		}
	}

	/// <summary>
	/// Compares the string by reference so long paths aren't hashed; the same text in another string just gets its own entry
	/// </summary>
	sealed class ReferenceFirstComparer<T> : IEqualityComparer<(string, T)>
	{
		public static readonly ReferenceFirstComparer<T> Default = new();

		public bool Equals( (string, T) x, (string, T) y ) => ReferenceEquals( x.Item1, y.Item1 ) && EqualityComparer<T>.Default.Equals( x.Item2, y.Item2 );

		public int GetHashCode( (string, T) key ) => HashCode.Combine( RuntimeHelpers.GetHashCode( key.Item1 ), key.Item2 );
	}
}
