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
			return _recorded.Entries.Count;
	}

	internal void Rewind( int checkpoint )
	{
		lock ( _lock )
		{
			Writing();
			_recorded.Entries.RemoveRange( checkpoint, _recorded.Entries.Count - checkpoint );
		}
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

	/// <summary>
	/// Clear the list and free the GPU buffers its painter draws from.
	/// </summary>
	internal void DisposePainter()
	{
		Reset();
		FindResource<Painter.Context>()?.Batcher.Dispose();
	}

	internal void RegisterResource( IResource resource )
	{
		lock ( _lock )
		{
			_resources ??= [];
			_resources.Add( resource );
			Volatile.Write( ref _replayedResources, _resources.ToArray() );
		}
	}

	// The resources as a replay reads them, without the lock (BeginResources); made again as one registers
	IResource[] _replayedResources;

	/// <summary>
	/// Executes commands on the current graphics context without requiring a scene view.
	/// </summary>
	internal void Execute() => ExecuteEntries();

	/// <summary>
	/// Held by a replay of this list, so one replays it at a time (its playback state is the list's); recording takes
	/// <see cref="_lock"/> and isn't held up by a replay.
	/// </summary>
	readonly Lock _playLock = new Lock();

	// Track active insertion edges before taking the next playback lock. That also catches cycles whose lists are
	// being replayed concurrently, where neither thread could acquire the other's lock to detect reentry.
	static readonly Lock insertionLock = new();
	CommandList inserted;

	void ExecuteInserted( CommandList other )
	{
		lock ( insertionLock )
		{
			for ( var next = other; next is not null; next = next.inserted )
			{
				if ( ReferenceEquals( this, next ) )
					throw new InvalidOperationException( $"Recursive command list insertion: {DebugName ?? "CommandList"}" );
			}
			inserted = other;
		}

		try
		{
			other.ExecuteEntries( state );
		}
		finally
		{
			lock ( insertionLock )
				inserted = null;
		}
	}

	/// <summary>Prepare the list's resources for a replay (a painter's textures marked used).</summary>
	void BeginResources()
	{
		if ( Volatile.Read( ref _replayedResources ) is not { } resources ) return;
		foreach ( var resource in resources )
			resource.BeginExecute();
	}

	/// <summary>Replays this list, borrowing the parent's playback state when inserted.</summary>
	void ExecuteEntries( State parent = null )
	{
		lock ( _playLock )
			Replay( parent );
	}

	void Replay( State parent )
	{
		// Catch direct reentry too, for example from an action inside the same command list.
		if ( _playing is not null )
			throw new InvalidOperationException( $"Recursive command list insertion: {DebugName ?? "CommandList"}" );

		state = parent ?? ObjectPool<State>.Get();
		try
		{
			var played = _playing = Playback();
			BeginResources();

			// Copy each entry so playback never mutates the recording.
			var entries = CollectionsMarshal.AsSpan( played.Entries );
			for ( int i = 0; i < entries.Length; i++ )
			{
				var e = entries[i];
				e.Execute( ref e, this );
			}
		}
		finally
		{
			try
			{
				if ( parent is null )
				{
					CloseLeakedScopes();
					state.Reset();
					ObjectPool<State>.Return( state );
				}
			}
			finally
			{
				state = null;
				_playing = null;
			}
		}
	}
}
