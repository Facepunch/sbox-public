using Sandbox.Engine;
using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	internal BlendMode BackgroundBlendMode;

	internal bool IsRenderDirty = true;
	internal readonly CommandList CommandList = new();
	internal readonly CommandList ClipCommandList = new(); // Don't execute directly - save off to this and then combine into main command list during rendering
	internal readonly CommandList TransformCommandList = new(); // Stores TransformMat attribute, combined into main command list
	internal readonly CommandList LayerCommandList = new(); // For post-children layer drawing (filters, masks, etc.)

	internal int _lastScissorHash;
	internal Matrix _lastTransformMat;
	internal bool _lastTransformIsRoot;
	internal Matrix? _lastLayerMatrix;

	public void MarkRenderDirty()
	{
		IsRenderDirty |= true;
	}

	internal virtual void DrawContent( CommandList commandList, PanelRenderer renderer, ref RenderState state )
	{
		BuildContentCommandList( commandList, ref state );
	}

	/// <summary>
	/// Called when <see cref="HasContent"/> is set to <see langword="true"/> to custom draw the panels content.
	/// You'll probably want to call <see cref="MarkRenderDirty"/> when your content changes to ensure the command list gets rebuilt.
	/// </summary>
	public virtual void BuildContentCommandList( CommandList commandList, ref RenderState state )
	{
		// nothing by default
	}

	/// <summary>
	/// You'll probably want to call <see cref="MarkRenderDirty"/> when your content changes to ensure the command list gets rebuilt.
	/// </summary>
	public virtual void BuildCommandList( CommandList commandList )
	{
		// nothing by default
	}

	/// <summary>
	/// Called to draw the panels background.
	/// </summary>
	[Obsolete( "Use BuildCommandList" )]
	public virtual void DrawBackground( ref RenderState state )
	{
		// nothing by default
	}

	/// <summary>
	/// Called when <see cref="HasContent"/> is set to <see langword="true"/> to custom draw the panels content.
	/// </summary>
	[Obsolete( "Use BuildContentCommandList" )]
	public virtual void DrawContent( ref RenderState state )
	{
		// nothing by default
	}
	/// <summary>
	/// Build command lists for all children. Called during tick phase.
	/// </summary>
	internal void BuildCommandListsForChildren( PanelRenderer render, ref RenderState state )
	{
		using var _ = render.Clip( this );

		if ( _renderChildrenDirty )
		{
			_renderChildren.Sort( ( x, y ) => x.GetRenderOrderIndex() - y.GetRenderOrderIndex() );
			_renderChildrenDirty = false;
		}

		for ( int i = 0; i < _renderChildren.Count; i++ )
		{
			render.BuildCommandLists( _renderChildren[i], state );
		}
	}

	/// <summary>
	/// Gather children's pre-built command lists into the global command list.
	/// Called during the gather phase after all per-panel CLs have been built.
	/// </summary>
	internal void GatherChildrenCommandLists( PanelRenderer render, ref RenderState state, CommandList globalCL )
	{
		if ( _renderChildrenDirty )
		{
			_renderChildren.Sort( ( x, y ) => x.GetRenderOrderIndex() - y.GetRenderOrderIndex() );
			_renderChildrenDirty = false;
		}

		for ( int i = 0; i < _renderChildren.Count; i++ )
		{
			render.GatherPanel( _renderChildren[i], state, globalCL );
		}
	}
}
