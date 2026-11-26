namespace Sandbox.SceneRenderer.Bridge;

internal sealed class BridgeMeshObject( SceneMeshObject source ) : CustomObject
{
	internal override void Render( RenderContext context, ViewPass view, MeshLayer layer )
	{
		if ( !source.IsValid() )
			return;

		context.SetObjectAttributes( source.CreatedAttributes );
		try
		{
			context.DrawBufferObject( source, layer.ShaderMode );
		}
		finally
		{
			context.SetObjectAttributes( null );
			context.Invalidate();
		}
	}
}
