using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	internal struct LayerEntry
	{
		public string RTHandle;
		public Matrix Matrix;
	}

	internal Stack<LayerEntry> LayerStack = new();

	void DrawLayerPanel( Panel panel, CommandList cl )
	{
		Stats.LayerPanels++;
		FlushBatch( cl );

		cl.Attributes.Set( "TransformMat", panel.CachedDescriptors.TransformMat );
		SetScissorAttributes( cl, panel.CachedDescriptors.Scissor );
		panel.PushLayerGather( this, cl );

		DrawPanel( panel, cl );

		FlushBatch( cl );

		PopLayer( panel, cl, DefaultRenderTarget );

		// LayerCommandList was built during Build without knowing the parent RT.
		// Set it now so InsertList targets the correct render target.
		if ( LayerStack.TryPeek( out var parent ) )
			panel.LayerCommandList.SetRenderTarget( new RenderTargetHandle { Name = parent.RTHandle } );
		else
			panel.LayerCommandList.SetRenderTarget( DefaultRenderTarget );

		cl.InsertList( panel.LayerCommandList );
	}

	internal void PushLayer( Panel panel, CommandList cl, RenderTargetHandle handle, Matrix mat )
	{
		cl.SetRenderTarget( handle );
		cl.Attributes.Set( "LayerMat", mat );
		cl.Attributes.SetCombo( "D_WORLDPANEL", 0 );
		cl.Clear( Color.Transparent );

		LayerStack.Push( new LayerEntry { RTHandle = handle.Name, Matrix = mat } );
	}

	internal void PopLayer( Panel panel, CommandList cl, RenderTarget defaultRT )
	{
		LayerStack.Pop();

		if ( LayerStack.TryPeek( out var top ) )
		{
			cl.SetRenderTarget( new RenderTargetHandle { Name = top.RTHandle } );
			cl.Attributes.Set( "LayerMat", top.Matrix );
			cl.Attributes.SetCombo( "D_WORLDPANEL", 0 );
		}
		else
		{
			cl.SetRenderTarget( defaultRT );
			cl.Attributes.Set( "LayerMat", Matrix.Identity );
			cl.Attributes.SetCombo( "D_WORLDPANEL", WorldPanelCombo );
		}
	}
}
