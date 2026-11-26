namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draw model in the world
	/// </summary>
	public void Model( Model model, Color color = new Color(), float duration = 0, Transform transform = default, bool overlay = false, bool castShadows = true, Material materialOveride = default, Transform[] localBoneTransforms = default )
	{
		if ( transform == default ) transform = Transform.Zero;
		if ( color == default ) color = Color.White;

		// Single frame models without bones are reused by the next frame's draws instead of recreated
		var pooled = duration <= 0 && localBoneTransforms == null && model is not null;
		var so = pooled ? TakePooledModel( model ) : null;
		if ( so is not null )
		{
			so.RenderingEnabled = true;
			so.ClearMaterialOverride();
		}
		so ??= new SceneModel( Scene.SceneWorld, model, transform );
		so.Flags.CastShadows = castShadows;
		so.ColorTint = color;
		so.RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.Default;
		so.Transform = transform;

		if ( localBoneTransforms != null )
		{
			so.Update( 0.1f, () =>
			{
				for ( int i = 0; i < localBoneTransforms.Length; i++ )
				{
					so.SetParentSpaceBone( i, localBoneTransforms[i] );
				}
			} );
		}

		if ( materialOveride is not null )
			so.SetMaterialOverride( materialOveride );

		Add( duration, so, pooled );
	}
}
