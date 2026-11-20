using Sandbox.Rendering;

public sealed class SniperScopeEffect : BasePostProcess<SniperScopeEffect>
{
	public float BlurInput { get; set; }

	private float _smoothedBlur;
	private float _opacity;
	private static Material _cachedMaterial;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_opacity = 0f;
	}

	public override void Render()
	{
		// A camera entity's view goes through the same main camera, the scope isn't part of it
		if ( CameraViewSystem.Get( Scene )?.ActiveCamera.IsValid() == true ) return;

		_opacity = _opacity.LerpTo( 1f, Time.Delta * 20f );
		_smoothedBlur = _smoothedBlur.LerpTo( BlurInput, Time.Delta * 8f );
		var blurAmount = (1.0f - _smoothedBlur).Clamp( 0.1f, 1f );

		Attributes.Set( "BlurAmount", blurAmount );
		Attributes.Set( "ScopeReveal", _opacity );
		Attributes.Set( "Offset", Vector2.Zero );

		_cachedMaterial ??= Material.FromShader( "shaders/postprocess/sniper_scope.shader" );
		var blit = BlitMode.WithBackbuffer( _cachedMaterial, Stage.AfterPostProcess, 200, false );
		Blit( blit, "SniperScope" );
	}
}
