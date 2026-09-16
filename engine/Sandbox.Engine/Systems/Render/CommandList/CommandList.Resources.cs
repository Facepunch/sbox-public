using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Rendering;

public sealed unsafe partial class CommandList
{
	/// <summary>
	/// Owns reusable command data referenced by this list. Reset releases recorded data for reuse.
	/// </summary>
	internal interface IResource
	{
		void Reset();
		void BeginExecute();
	}

	List<IResource> _resources;

	internal Lock SyncRoot => _lock;

	internal int GetCheckpoint()
	{
		lock ( _lock )
			return _entries.Count;
	}

	internal void Rewind( int checkpoint )
	{
		lock ( _lock )
			_entries.RemoveRange( checkpoint, _entries.Count - checkpoint );
	}

	internal T FindResource<T>() where T : class, IResource
	{
		lock ( _lock )
		{
			if ( _resources is not null )
			{
				foreach ( var resource in _resources )
				{
					if ( resource is T match )
						return match;
				}
			}

			return null;
		}
	}

	internal void RegisterResource( IResource resource )
	{
		lock ( _lock )
		{
			_resources ??= [];
			_resources.Add( resource );
		}
	}

	/// <summary>
	/// Executes commands on the current graphics context without requiring a scene view.
	/// </summary>
	internal void Execute()
	{
		lock ( _lock )
			ExecuteEntries();
	}

	/// <summary>
	/// Runs every recorded entry against a fresh state. The caller holds the lock.
	/// </summary>
	void ExecuteEntries()
	{
		var previous = state;
		state = ObjectPool<State>.Get();
		try
		{
			if ( _resources is not null )
			{
				foreach ( var resource in _resources )
					resource.BeginExecute();
			}

			var entries = CollectionsMarshal.AsSpan( _entries );
			for ( int i = 0; i < entries.Length; i++ )
				entries[i].Execute( ref entries[i], this );
		}
		finally
		{
			state.Reset();
			ObjectPool<State>.Return( state );
			state = previous;
		}
	}
}
