using System.Runtime.InteropServices;

namespace Sandbox;

public static partial class Gizmo
{
	/// <summary>Restores the owning gizmo's state without allocating a disposable object.</summary>
	internal readonly struct ScopeHandle : IDisposable
	{
		readonly Instance _owner;
		readonly ScopeState _state;

		internal ScopeHandle( Instance owner )
		{
			_owner = owner;
			_state = owner.scope;
			owner.scope.Create = 0;
		}

		public void Dispose()
		{
			if ( _owner is not null ) _owner.scope = _state;
		}
	}

	// Public signatures stay IDisposable; engine hot paths use the value handles directly.
	sealed class ScopeLease<T> : IDisposable where T : struct, IDisposable
	{
		static readonly Stack<ScopeLease<T>> _free = new();
		T _value;
		bool _open;

		internal static IDisposable Rent( T value )
		{
			if ( !_free.TryPop( out var lease ) ) lease = new();
			lease._value = value;
			lease._open = true;
			return lease;
		}

		public void Dispose()
		{
			if ( !_open ) return;
			_open = false;
			var value = _value;
			_value = default;
			value.Dispose();
			_free.Push( this );
		}
	}

	public partial class Instance
	{
		readonly record struct PathKey( string Parent, ulong ParentHash, string Name, int Index, bool Numbered, bool ObjectScope )
		{
			public override int GetHashCode() => HashCode.Combine( ParentHash, Name, Index );
		}
		struct CachedPath
		{
			public string Text;
			public ulong Hash;
			public int Frame;
		}
		readonly Dictionary<PathKey, CachedPath> _paths = new();
		int _pathFrame;
		int _pathSweepAt = 1024;

		internal void SetScopePath( string name, int index = 0, bool objectScope = false, bool numbered = false )
		{
			var key = new PathKey( objectScope ? null : scope.Path, objectScope ? 0 : scope.PathHash, name, index, numbered, objectScope );
			ref var path = ref CollectionsMarshal.GetValueRefOrAddDefault( _paths, key, out var exists );
			if ( !exists )
			{
				path.Text = objectScope ? $"object-{index}" : numbered ? $"{scope.Path}/{name}{index}" : name is null ? $"{scope.Path}/Scope{index}" : $"{scope.Path}/{name}";
				path.Hash = HashString( path.Text );
			}
			path.Frame = _pathFrame;
			scope.Path = path.Text;
			scope.PathHash = path.Hash;
		}

		void PrunePaths()
		{
			var grew = _paths.Count > _pathSweepAt;
			if ( !grew && (_pathFrame & 63) != 0 ) return;
			var keepFrames = grew ? 1 : 64;
			foreach ( var entry in _paths )
				if ( _pathFrame - entry.Value.Frame > keepFrames ) _paths.Remove( entry.Key );
			_pathSweepAt = Math.Max( 1024, _paths.Count * 2 );
		}
	}
}
