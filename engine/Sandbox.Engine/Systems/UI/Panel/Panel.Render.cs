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

	internal virtual void DrawContent( CommandList commandList, PanelRenderer renderer, ref RenderState state )
	{
		BuildContentCommandList( commandList, ref state );
	}

	/// <summary>
	/// Called when <see cref="HasContent"/> is set to <see langword="true"/> to custom draw the panels content.
	/// </summary>
	public virtual void BuildContentCommandList( CommandList commandList, ref RenderState state )
	{
		// nothing by default
	}

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

	internal void RenderChildren( PanelRenderer render, ref RenderState state )
	{
		if ( _renderChildrenDirty )
		{
			_renderChildren.Sort( ( x, y ) => x.GetRenderOrderIndex() - y.GetRenderOrderIndex() );
			_renderChildrenDirty = false;
		}

		// Render Children
		{
			for ( int i = 0; i < _renderChildren.Count; i++ )
			{
				render.Render( _renderChildren[i], state );
			}
		}
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
}
