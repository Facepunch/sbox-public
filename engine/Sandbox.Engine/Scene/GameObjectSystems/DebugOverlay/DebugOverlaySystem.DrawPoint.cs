namespace Sandbox;

public partial class DebugOverlaySystem
{
	internal void Point( Vector3 position, float size, Color color = new Color(), float duration = 0, bool overlay = false )
	{
		if ( color == default ) color = Color.White;

		Add( duration, new PointSceneObject( Scene.SceneWorld, size )
		{
			ColorTint = color,
			Transform = new Transform( position ),
			RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth,
			Bounds = BBox.FromPositionAndSize( position, size )
		} );
	}
}

file class PointSceneObject : SceneCustomObject
{
	public float Size;

	public PointSceneObject( SceneWorld sceneWorld, float size ) : base( sceneWorld )
	{
		Size = size;
		Flags.CastShadows = false;
	}

	public override void RenderSceneObject()
	{
		var rect = new Rect( Size * -0.5f, Size );
		Attributes.SetCombo( "D_WORLDPANEL", 1 );
		Attributes.Set( "WorldMat", Matrix.CreateRotation( Graphics.CameraRotation ) );
		Attributes.Set( "TransformMat", Matrix.CreateRotation( Rotation.From( 0, -90, 90 ) ) );
		Attributes.Set( "Texture", Texture.White );
		Graphics.DrawQuad( rect, Material.UI.Basic, ColorTint, Attributes );
	}
}
