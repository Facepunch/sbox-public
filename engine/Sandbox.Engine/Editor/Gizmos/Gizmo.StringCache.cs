using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Gizmo
{
	/// <summary>
	/// Strings reused across instance pushes; entries unused for <see cref="MaxIdleFrames"/> pushes are dropped
	/// </summary>
	sealed class StringCache<TKey>( IEqualityComparer<TKey> comparer )
	{
		const int MaxIdleFrames = 64;

		struct Entry
		{
			public string Value;
			public int Frame;
		}

		readonly Dictionary<TKey, Entry> _entries = new( comparer );
		int _frame;

		public string Get( TKey key, Func<TKey, string> create )
		{
			ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault( _entries, key, out _ );
			entry.Value ??= create( key );
			entry.Frame = _frame;
			return entry.Value;
		}

		public void NextFrame()
		{
			if ( ++_frame % MaxIdleFrames != 0 )
				return;

			foreach ( var (key, entry) in _entries )
			{
				if ( _frame - entry.Frame > MaxIdleFrames )
					_entries.Remove( key );
			}
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
