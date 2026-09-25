using Sandbox.Rendering;
using Sandbox.Utility;
using System.Buffers;
using System.Threading;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	readonly record struct CommandListEntry( int Priority, CommandList List );

	// Stage hooks run on render worker threads, and one camera can be rendering several views at
	// once (a planar reflection or refraction capture drawn with the viewing camera). Capture
	// registrations into a separate pooled buffer for each execution, so callbacks can change
	// registrations without invalidating readers or holding this lock while commands run.
	readonly Lock _commandListLock = new();
	readonly Dictionary<Stage, List<CommandListEntry>> commandlists = new();

	/// <summary>
	/// Add a command list to the render
	/// </summary>
	public void AddCommandList( CommandList buffer, Stage stage, int order = 0 )
	{
		lock ( _commandListLock )
		{
			if ( !commandlists.TryGetValue( stage, out var list ) )
			{
				list = new List<CommandListEntry>();
				commandlists[stage] = list;
			}

			// Keep render workers read-only, and preserve registration order for equal priorities.
			var index = list.Count;
			while ( index > 0 && list[index - 1].Priority > order )
				index--;

			list.Insert( index, new CommandListEntry( order, buffer ) );
		}
	}

	/// <summary>
	/// Remove an entry
	/// </summary>
	public void RemoveCommandList( CommandList buffer, Stage stage )
	{
		lock ( _commandListLock )
		{
			if ( !commandlists.TryGetValue( stage, out var list ) )
				return;

			RemoveCommandListEntries( list, buffer );
		}
	}

	/// <summary>
	/// Remove an entry
	/// </summary>
	public void RemoveCommandList( CommandList buffer )
	{
		lock ( _commandListLock )
		{
			foreach ( var list in commandlists.Values )
			{
				RemoveCommandListEntries( list, buffer );
			}
		}
	}

	/// <summary>
	/// Remove matching registrations without allocating a capturing predicate. Caller holds the lock.
	/// </summary>
	private static void RemoveCommandListEntries( List<CommandListEntry> list, CommandList buffer )
	{
		for ( var i = list.Count - 1; i >= 0; i-- )
		{
			if ( list[i].List == buffer )
				list.RemoveAt( i );
		}
	}

	/// <summary>
	/// Remove all entries in this stage
	/// </summary>
	public void ClearCommandLists( Stage stage )
	{
		lock ( _commandListLock )
		{
			// Retain capacity for callers that clear and rebuild registrations every frame.
			if ( commandlists.TryGetValue( stage, out var list ) )
				list.Clear();
		}
	}

	/// <summary>
	/// Remove all command list registrations.
	/// </summary>
	public void ClearCommandLists()
	{
		lock ( _commandListLock )
		{
			foreach ( var list in commandlists.Values )
				list.Clear();
		}
	}

	static Superluminal _executeCommandList = new Superluminal( "ExecuteCommandList", Color.Cyan );

	/// <summary>
	/// Called during the render pipeline on a worker thread.
	/// </summary>
	private void ExecuteCommandLists( Stage stage, SceneCamera currentCamera )
	{
		Scene.RunRenderThreadEvent( this, stage );

		CommandListEntry[] entries;
		int count;
		lock ( _commandListLock )
		{
			if ( !commandlists.TryGetValue( stage, out var list ) || list.Count == 0 )
				return;

			count = list.Count;
			entries = ArrayPool<CommandListEntry>.Shared.Rent( count );
			list.CopyTo( entries );
		}

		try
		{
			// The rented array can be larger than the captured registration count.
			for ( var i = 0; i < count; i++ )
			{
				var entry = entries[i];
				using ( _executeCommandList.Start( entry.List.DebugName ) )
				{
					if ( entry.List.Flags.Contains( CommandList.Flag.PostProcess ) && !currentCamera.EnablePostProcessing )
						continue;

					entry.List.ExecuteOnRenderThread();
				}
			}
		}
		finally
		{
			ArrayPool<CommandListEntry>.Shared.Return( entries, clearArray: true );
		}
	}
}
