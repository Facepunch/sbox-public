using Sandbox.Rendering;

namespace Sandbox.UI;

internal sealed partial class PanelRenderer
{
	public Rect Screen { get; internal set; }

	readonly UIBatcher batcher = new();
	readonly List<GPUBoxInstance> pendingInstances = new();
	int batchIndex;

	[ConVar( "ui_visualize_batches", Help = "Visualize UI draw batches with colored overlays" )]
	internal static bool DebugVisualizeBatches { get; set; }

	bool isWorldPanelContext;
	int WorldPanelCombo => isWorldPanelContext ? 1 : 0;
	internal RenderTarget DefaultRenderTarget;

	internal void BuildDescriptors( RootPanel panel, float opacity = 1.0f )
	{
		Screen = panel.PanelBounds;
		isWorldPanelContext = panel.IsWorldPanel;
		Matrix = Matrix.Identity;

		RenderModeStack.Clear();
		RenderModeStack.Push( "normal" );
		SetRenderMode( "normal" );

		DefaultRenderTarget = Graphics.RenderTarget;
		LayerStack?.Clear();
		InitScissor( Screen );

		BuildDescriptors( (Panel)panel, new RenderState { X = Screen.Left, Y = Screen.Top, Width = Screen.Width, Height = Screen.Height, RenderOpacity = opacity } );
	}

	internal void AdvanceFrame()
	{
		batcher.AdvanceFrame();
	}

	internal void BuildCommandList( RootPanel root, float opacity = 1.0f )
	{
		var cl = root.PanelCommandList;
		cl.Reset();

		Screen = root.PanelBounds;
		DefaultRenderTarget = Graphics.RenderTarget;
		isWorldPanelContext = root.IsWorldPanel;

		LayerStack?.Clear();
		pendingInstances.Clear();
		backdropGrabActive = false;
		batchIndex = 0;

		cl.Attributes.Set( "LayerMat", Matrix.Identity );
		cl.Attributes.SetCombo( "D_WORLDPANEL", WorldPanelCombo );
		InitScissor( Screen, cl );

		DrawPanel( root, cl );
		FlushBatch( cl );

		Stats.ScissorCount = batcher.ScissorCount;
		Stats.GpuBufferCount = batcher.GpuBufferCount;
	}

	internal bool IsWorldPanel( Panel panel )
	{
		if ( panel is RootPanel { IsWorldPanel: true } )
			return true;

		return panel.FindRootPanel()?.IsWorldPanel ?? false;
	}

	//
	// Stats
	//

	internal struct FrameStats
	{
		public int Panels;
		public int BatchedPanels;
		public int InlinePanels;
		public int LayerPanels;
		public int DrawCalls;
		public int InstanceCount;
		public int FlushCount;
		public int ScissorCount;
		public int GpuBufferCount;

		public void Reset() => this = default;
	}

	internal static FrameStats Stats;
}
