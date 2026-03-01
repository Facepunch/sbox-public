using Sandbox.Rendering;
using Sandbox.Utility;

namespace Sandbox;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	readonly record struct CommandListEntry( int Priority, CommandList List ) : IComparable<CommandListEntry>
	{
		public int CompareTo( CommandListEntry other ) => Priority.CompareTo( other.Priority );
	}
	Dictionary<Stage, List<CommandListEntry>> commandlists = new();

	/// <summary>
	/// Add a command list to the render
	/// </summary>
	public void AddCommandList( CommandList buffer, Stage stage, int order = 0 )
	{
		if ( !commandlists.TryGetValue( stage, out var list ) )
		{
			list = new List<CommandListEntry>();
			commandlists[stage] = list;
		}

		list.Add( new CommandListEntry( order, buffer ) );
	}

	/// <summary>
	/// Remove an entry 
	/// </summary>
	public void RemoveCommandList( CommandList buffer, Stage stage )
	{
		if ( !commandlists.TryGetValue( stage, out var list ) )
			return;

		list.RemoveAll( x => x.List == buffer );
	}

	/// <summary>
	/// Remove an entry 
	/// </summary>
	public void RemoveCommandList( CommandList buffer )
	{
		foreach ( var list in commandlists.Values )
		{
			list.RemoveAll( x => x.List == buffer );
		}
	}

	/// <summary>
	/// Remove all entries in this stage
	/// </summary>
	public void ClearCommandLists( Stage stage )
	{
		commandlists.Remove( stage );
	}

	/// <summary>
	/// Remove all entries in this stage
	/// </summary>
	public void ClearCommandLists()
	{
		commandlists.Clear();
	}

	static Superluminal _executeCommandList = new Superluminal( "ExecuteCommandList", Color.Cyan );

	/// <summary>
	/// Called during the render pipeline. Currently this is rendered on the main thread, but ideally, one day, this will all be threaded.
	/// </summary>
	private void ExecuteCommandLists( Stage stage, SceneCamera currentCamera )
	{
		Scene.RunEvent<IRenderThread>( x => x.OnRenderStage( this, stage ) );

		if ( commandlists.TryGetValue( stage, out var list ) )
		{
			list.Sort();

			foreach ( var entry in list )
			{
				using ( _executeCommandList.Start( entry.List.DebugName ) )
				{
					if ( entry.List.Flags.Contains( CommandList.Flag.PostProcess ) && !currentCamera.EnablePostProcessing )
						continue;

					entry.List.ExecuteOnRenderThread();
				}
			}
		}
	}
}
