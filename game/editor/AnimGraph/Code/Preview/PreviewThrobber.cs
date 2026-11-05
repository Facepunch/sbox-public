using Sandbox.Rendering;

namespace Editor.AnimGraph;

sealed class PreviewThrobber : SceneCustomObject
{
	private readonly SceneRenderingWidget _preview;
	private readonly Texture _texture;
	private bool _enabled;
	private RealTimeSince _timeSinceDisabled;

	public bool Enabled
	{
		set
		{
			_enabled = value;

			if ( !value )
				_timeSinceDisabled = 0;
		}
	}

	public PreviewThrobber( SceneWorld sceneWorld, SceneRenderingWidget preview ) : base( sceneWorld )
	{
		_preview = preview;
		_texture = Texture.Load( "tools/images/common/busy.png", true );
		Bounds = BBox.FromPositionAndSize( Vector3.Zero, float.MaxValue );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		if ( !_enabled && _timeSinceDisabled > 0.5f )
			return;

		const int speed = 300;
		var delta = _enabled ? 0.0f : _timeSinceDisabled * 2.0f;
		var angle = RealTime.Now % (MathF.PI * (2.0f * speed));
		var dpiScale = _preview.DpiScale;
		var position = new Vector2( _preview.Width - 39, 41 ) * dpiScale;
		var matrix = Matrix.CreateRotation( Rotation.From( 0, angle * speed, 0 ) );
		matrix *= Matrix.CreateTranslation( position );
		Graphics.Attributes.Set( "LayerMat", matrix );

		Graphics.Attributes.Set( "Texture", _texture );
		Graphics.Attributes.SetComboEnum( "D_BLENDMODE", Sandbox.BlendMode.Normal );
		Graphics.DrawQuad( new Rect( -50, 100 ) * dpiScale, Material.UI.Basic, Color.Black.WithAlpha( 0.5f.LerpTo( 0.0f, delta ) ) );
		Graphics.Attributes.SetComboEnum( "D_BLENDMODE", Sandbox.BlendMode.Lighten );

		position = new Vector2( _preview.Width - 40, 40 ) * dpiScale;
		matrix = Matrix.CreateRotation( Rotation.From( 0, angle * speed, 0 ) );
		matrix *= Matrix.CreateTranslation( position );
		Graphics.Attributes.Set( "LayerMat", matrix );
		Graphics.DrawQuad( new Rect( -50, 100 ) * dpiScale, Material.UI.Basic, _enabled ? Color.White : Color.White.WithAlpha( 1.0f.LerpTo( 0.0f, delta ) ) );
	}
}
