using NativeEngine;

namespace Sandbox.Rendering;

public sealed unsafe partial class CommandList
{
	/// <summary>
	/// Times a section of this command list on the GPU. Shows up as a PIX/RenderDoc event and as a row in
	/// the GPU profiler overlay. Bound with a <c>using</c> block, which is what decides where the section ends.
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
	}

	private static bool _debugMarkers;
	private static bool _debugMarkersQueried;

	private static bool DebugMarkersEnabled
	{
		get
		{
			if ( !_debugMarkersQueried )
			{
				_debugMarkers = g_pRenderDevice.AreDebugMarkersEnabled();
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
		var context = Graphics.Context;

		var pix = DebugMarkersEnabled;
		if ( pix ) context.BeginPixEvent( sampler.NamePtr );

		// None on the async compute queue, whose timestamps native doesn't support (CSceneSystem::SubmitViews)
		var marker = Diagnostics.GpuProfilerStats.Enabled && !Graphics.OnComputeQueue
			? CSceneSystem.BeginManagedPerfMarker( context, sampler.NamePtr )
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
