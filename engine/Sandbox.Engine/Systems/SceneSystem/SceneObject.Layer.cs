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
	uint? _layerMatch;

	/// <summary>
	/// The layer match id as a string token (<c>CSceneObject::m_nLayerMatchID</c>): set with <see cref="RenderLayer"/>, or read
	/// once from native for an object native code made (a map's overlays). Native still gets it while something reads it.
	/// </summary>
	internal uint LayerMatchToken => _layerMatch ??= native.GetLayerMatchIDValue();

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
				_renderLayer = SceneRenderLayer.Default;
				SetLayerMatch( null );
				NotifyChanged( Rendering.SceneObjectChange.Flags );
				return;
			}

			_renderLayer = value;
			SetLayerMatch( layer );

			// The layers that take it changed, as a flag changing does
			NotifyChanged( Rendering.SceneObjectChange.Flags );
		}
	}

	void SetLayerMatch( StringToken token )
	{
		if ( native.IsNull ) Sandbox.Interop.ThrowNullSelf( "CSceneObject", "SetLayerMatchID" );
		_layerMatch = token.Value;
		if ( WritesNative ) native.SetLayerMatchID( token );
	}

	static readonly StringToken LightGroupName = new( "LightGroup" );
	static readonly StringToken RenderOrderName = new( "RenderOrder" );
	int? _nativeLightGroup, _nativeRenderOrder;

	/// <summary>
	/// The light group the object is lit by (its <c>LightGroup</c> attribute, as <c>CSceneObject::GetIntValue</c> reads it).
	/// </summary>
	internal int LightGroup => ObjectInt( LightGroupName, ref _nativeLightGroup );

	/// <summary>
	/// The order a static overlay draws in (its <c>RenderOrder</c> attribute).
	/// </summary>
	internal int RenderOrder => ObjectInt( RenderOrderName, ref _nativeRenderOrder );

	/// <summary>
	/// An integer attribute of the object's. While native keeps the object's attributes they're read from it; when they're
	/// managed (<see cref="WritesNative"/> off) the managed set's value wins, and what native code gave the object when it made
	/// it (a map's light groups and overlay order) is read from native once.
	/// </summary>
	int ObjectInt( StringToken name, ref int? fromNative )
	{
		if ( native.IsNull ) return fromNative ?? 0;
		if ( WritesNative ) return native.GetIntValue( name, 0 );
		if ( _attributes?.Managed?.GetFloats( name ) is { Length: > 0 } value ) return (int)value[0];
		return fromNative ??= native.GetIntValue( name, 0 );
	}


}
