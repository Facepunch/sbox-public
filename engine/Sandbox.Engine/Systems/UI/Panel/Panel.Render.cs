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
	internal Matrix? _lastLayerMatrixInverted;

	internal enum RenderMode : byte { Inline, Batched, Layer }

	internal int CachedBackgroundVersion;
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
		SortRenderChildren();

		// Content clips short of the scrollbar gutter, the scrollbars clip to the whole padding box
		using ( render.Clip( this, ContentClipRect ) )
		{
			for ( int i = 0; i < _renderChildren.Count; i++ )
			{
				if ( !_renderChildren[i].IsFixed && _renderChildren[i] is not ScrollBar )
					render.BuildDescriptors( _renderChildren[i], state );
			}
		}

		if ( ScrollbarCount == 0 ) return;

		using ( render.Clip( this ) )
		{
			for ( int i = 0; i < _renderChildren.Count; i++ )
			{
				if ( !_renderChildren[i].IsFixed && _renderChildren[i] is ScrollBar )
					render.BuildDescriptors( _renderChildren[i], state );
			}
		}
	}

}
