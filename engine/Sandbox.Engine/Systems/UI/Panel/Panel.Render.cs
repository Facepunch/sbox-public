using Sandbox.Engine;
using Sandbox.Rendering;

namespace Sandbox.UI;

public partial class Panel
{
	internal BlendMode BackgroundBlendMode;

	internal bool IsRenderDirty = true;
	internal readonly CommandList LayerCommandList;

	internal int _lastScissorHash;
	internal Matrix? _lastLayerMatrix;

	internal enum RenderMode : byte { Inline, Batched, Layer }

	internal RenderLayer CachedDescriptors;
	internal RenderMode CachedRenderMode;
	internal float CachedRenderOpacity = 1.0f;
	internal BlendMode CachedOverrideBlendMode = BlendMode.Normal;

	public void MarkRenderDirty()
	{
		IsRenderDirty = true;
	}

	/// <summary>
	/// Override this to draw custom graphics for this panel using the <see cref="Draw"/> API.
	/// <example>
	/// <code>
	/// public override void OnDraw()
	/// {
	///     var r = Box.RectInner;
	///     Draw.Rect( r, Color.Blue.WithAlpha( 0.2f ), cornerRadius: 4 );
	///     Draw.Text( "Score: 100", r, 16, Color.White, TextFlag.Center );
	/// }
	/// </code>
	/// </example>
	/// </summary>
	public virtual void OnDraw()
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void BuildContentCommandList( CommandList commandList, ref RenderState state )
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void BuildCommandList( CommandList commandList )
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void DrawContent( ref RenderState state )
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void DrawBackground( ref RenderState state )
	{
	}

	[Obsolete( "Use Draw" )]
	internal virtual void DrawContent( PanelRenderer renderer, ref RenderState state )
	{
	}

	/// <summary>
	/// Build descriptors for all children. Called during tick phase.
	/// </summary>
	internal void BuildDescriptorsForChildren( PanelRenderer render, ref RenderState state )
	{
		using var _ = render.Clip( this );

		if ( _renderChildrenDirty )
		{
			_renderChildren.Sort( ( x, y ) => x.GetRenderOrderIndex() - y.GetRenderOrderIndex() );
			_renderChildrenDirty = false;
		}

		for ( int i = 0; i < _renderChildren.Count; i++ )
		{
			render.BuildDescriptors( _renderChildren[i], state );
		}
	}

}
