using NativeEngine;

namespace Sandbox;

//
// garry:
//
// I decided to keep this seperate from SceneLayerType because that was implemented
// when I had no idea what was going on.. SceneLayerType kind of serves to tell a manual
// rendering sceneobject that we're rendering a shadow layer etc..
//

/// <summary>
/// SceneObjects can be rendered on layers other than the main game layer.
/// This is useful if, for example, you want to render on top of everything without
/// applying post processing.
/// </summary>
public enum SceneRenderLayer
{
	/// <summary>
	/// Draw wherever makes sense based on the flags, default behaviour
	/// </summary>
	Default,

	/// <summary>
	/// Layer drawn on top of everything else - with altered depth
	/// </summary>
	ViewModel = 10,

	/// <summary>
	/// Overlay - after post processing - but still with the scene's depth
	/// </summary>
	OverlayWithDepth = 20,

	/// <summary>
	/// Overlay - after post processing - without depth (draw over)
	/// </summary>
	OverlayWithoutDepth = 30
}


internal static class SceneRenderLayerHelper
{
	/// <summary>
	/// Internally we don't pass these enums, they're just string tokens.
	/// No need to expose the actual string tokens to people unless we expose the render pipeline fully to them.
	/// </summary>
	public static Dictionary<SceneRenderLayer, string> Names = new Dictionary<SceneRenderLayer, string>()
	{
		{ SceneRenderLayer.ViewModel, "viewmodel" },
		{ SceneRenderLayer.OverlayWithDepth, "OverlayWithDepth" },
		{ SceneRenderLayer.OverlayWithoutDepth, "OverlayWithoutDepth" }
	};
}


public partial class SceneObject
{

	SceneRenderLayer _renderLayer;

	/// <summary>
	/// For a layer to draw this object, the target layer must match (or be unset)
	/// and the flags must match
	/// </summary>
	public SceneRenderLayer RenderLayer
	{
		get => _renderLayer;
		set
		{
			if ( value == _renderLayer )
				return;

			// if not found or default we'll set it to default
			if ( !SceneRenderLayerHelper.Names.TryGetValue( value, out var layer ) )
			{
				native.SetLayerMatchID( null );
				NotifyChanged( Rendering.SceneObjectChange.Flags );
				return;
			}

			_renderLayer = value;
			native.SetLayerMatchID( layer );

			// The layers that take it changed, as a flag changing does
			NotifyChanged( Rendering.SceneObjectChange.Flags );
		}
	}


}
