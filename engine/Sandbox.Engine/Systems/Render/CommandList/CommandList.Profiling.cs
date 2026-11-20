namespace Sandbox.Rendering;

public sealed unsafe partial class CommandList
{
	/// <summary>
	/// Marks a section of this command list for graphics debuggers (a RenderDoc label on Borealis.Gpu, whose passes its own
	/// pass timer times). Bound with a <c>using</c> block, which is what decides where the section ends.
	/// </summary>
	public ProfilingScope ProfileScope( ProfilingSampler sampler )
	{
		if ( sampler is null )
			return default;

		AddEntry( &ExecuteBeginScope, new Entry { Object1 = sampler } );

		return new ProfilingScope( this );
	}

	/// <summary>
	/// A marked section in a command list. Closes at the end of its <c>using</c> block.
	/// </summary>
	public ref struct ProfilingScope
	{
		private CommandList _list;

		internal ProfilingScope( CommandList list )
		{
			_list = list;
		}

		public void Dispose()
		{
			if ( _list is null )
				return;

			_list.AddEntry( &ExecuteEndScope, default );
			_list = null;
		}
	}

	/// <summary>
	/// A scope open during this execution: the Borealis.Gpu list a debug label was begun on, for graphics debuggers.
	/// </summary>
	internal struct OpenScope
	{
		public Borealis.Gpu.CommandList Label;
	}

	/// <summary>
	/// Begin a scope on the render thread. Checked here rather than at record time so toggling the profiler takes effect immediately
	/// </summary>
	internal static OpenScope OpenScopeFor( ProfilingSampler sampler )
	{
		// On Borealis.Gpu a debug label around the scope (its passes are timed by Borealis's own pass timer)
		if ( Graphics.Backend is BorealisGpuRenderBackend { Context.List: { } list } )
		{
			list.BeginLabel( sampler.Name );
			return new OpenScope { Label = list };
		}

		return default;
	}

	static void ExecuteBeginScope( ref Entry entry, CommandList commandList )
	{
		var sampler = (ProfilingSampler)entry.Object1;

		commandList.state.openScopes.Push( OpenScopeFor( sampler ) );
	}

	static void ExecuteEndScope( ref Entry entry, CommandList commandList )
	{
		if ( !commandList.state.openScopes.TryPop( out var open ) )
			return;

		CloseScope( open );
	}

	static void CloseScope( OpenScope open )
	{
		// Ended on the list it began on, while that's still the one recording (a block that ended took its labels with it)
		if ( open.Label is not null && Graphics.Backend is BorealisGpuRenderBackend { Context.List: { } list } && list == open.Label ) list.EndLabel();
	}

	/// <summary>
	/// Close anything left open by a missing Dispose, so a recording mistake can't leak a timestamp query for the rest of the frame
	/// </summary>
	void CloseLeakedScopes()
	{
		while ( state.openScopes.TryPop( out var open ) )
		{
			CloseScope( open );
		}
	}
}
