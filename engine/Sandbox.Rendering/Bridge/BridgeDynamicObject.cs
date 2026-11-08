namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Draws mirrored dynamic objects from live native vertices (<c>RenderTools.DrawDynamicSceneObject</c>).
/// </summary>
internal sealed class BridgeDynamicObject : CustomObject
{
	public SceneDynamicObject Source { get; }

	public BridgeDynamicObject( SceneDynamicObject source )
	{
		Source = source;
	}

	internal override void Render( RenderContext context, ViewPass view, MeshLayer layer )
	{
		if ( !Source.IsValid() ) return;

		// Inherit frame attributes.
		var native = context.Native;
		native.GetAttributesPtrForModify().SetParent( context.Attributes.Get() );

		SceneNative.DrawDynamicObject( context, Source, layer.ShaderMode );
	}
}
