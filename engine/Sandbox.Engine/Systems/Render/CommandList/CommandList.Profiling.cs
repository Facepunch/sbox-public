using NativeEngine;

namespace Sandbox.Rendering;

public sealed unsafe partial class CommandList
{
	/// <summary>
	/// Times a section of this command list on the GPU. Shows up as a PIX/RenderDoc event and as a row in
	/// the GPU profiler overlay when profiling is enabled. Bound with a <c>using</c> block,
	/// which is what decides where the section ends.
	/// </summary>
	public ProfilingScope ProfileScope( ProfilingSampler sampler )
	{
		if ( sampler is null )
			return default;

		AddEntry( &ExecuteBeginScope, new Entry { Object1 = sampler } );

		return new ProfilingScope( this );
	}

	/// <summary>
	/// A GPU timing section in a command list. Closes at the end of its <c>using</c> block.
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
	/// A scope open during this execution. Carries what was actually begun, since the profiler or a debugger
	/// can be toggled between a scope opening and closing.
	/// </summary>
	internal struct OpenScope
	{
		public IntPtr Marker;
		public bool Pix;

		/// <summary>The Borealis.Gpu list a debug label was begun on, for graphics debuggers.</summary>
		public Borealis.Gpu.CommandList Label;
	}

	private static bool _debugMarkers;
	private static bool _debugMarkersQueried;

	private static bool DebugMarkersEnabled
	{
		get
		{
			if ( !_debugMarkersQueried )
			{
				_debugMarkers = Graphics.HasSource2Device && g_pRenderDevice.AreDebugMarkersEnabled();
				_debugMarkersQueried = true;
			}

			return _debugMarkers;
		}
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

		// Source 2's markers and timestamps
		if ( Graphics.Backend is not Source2RenderBackend ) return default;

		var context = Graphics.Context;

		var pix = DebugMarkersEnabled;
		if ( pix ) context.BeginPixEvent( sampler.NamePtr );

		// Native selects the actual queue and rejects unsupported timestamp clock domains.
		var marker = Diagnostics.GpuProfilerStats.Enabled
			? CSceneSystem.BeginManagedPerfMarker( context, sampler.NamePtr, false, false )
			: IntPtr.Zero;

		// Native copies the name during those calls, so the sampler only has to survive until here.
		GC.KeepAlive( sampler );

		return new OpenScope { Marker = marker, Pix = pix };
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

		CloseScope( Graphics.Context, open );
	}

	static void CloseScope( IRenderContext context, OpenScope open )
	{
		// Ended on the list it began on, while that's still the one recording (a block that ended took its labels with it)
		if ( open.Label is not null )
		{
			if ( Graphics.Backend is BorealisGpuRenderBackend { Context.List: { } list } && list == open.Label ) list.EndLabel();
			return;
		}

		// End the timing marker first so its timestamp lands inside the PIX event that brackets it
		if ( open.Marker != IntPtr.Zero )
			CSceneSystem.EndManagedPerfMarker( context, open.Marker );

		if ( open.Pix )
			context.EndPixEvent();
	}

	/// <summary>
	/// Close anything left open by a missing Dispose, so a recording mistake can't leak a timestamp query for the rest of the frame
	/// </summary>
	void CloseLeakedScopes()
	{
		var context = Graphics.Context;

		while ( state.openScopes.TryPop( out var open ) )
		{
			CloseScope( context, open );
		}
	}
}
