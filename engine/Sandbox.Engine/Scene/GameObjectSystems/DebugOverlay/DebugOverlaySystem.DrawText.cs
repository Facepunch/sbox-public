namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draw text in the world
	/// </summary>
	public void Text( Vector3 position, string text, float size = 32, TextFlag flags = TextFlag.Center, Color color = new Color(), float duration = 0, bool overlay = false )
	{
		if ( color == default ) color = Color.White;

		var textBlock = new TextRendering.Scope( text, color, size )
		{
			Shadow = new TextRendering.Shadow { Color = Color.Black, Enabled = true, Offset = 2, Size = 4 }
		};

		Text( position, textBlock, flags, duration, overlay );
	}

	/// <summary>
	/// Draw text in the world
	/// </summary>
	public void Text( Vector3 position, TextRendering.Scope scope, TextFlag flags = TextFlag.Center, float duration = 0, bool overlay = false )
	{
		Add( duration, new DebugTextSceneObject( Scene.SceneWorld, scope, flags )
		{
			RenderLayer = overlay ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth,
			Transform = new Transform( position ),
			LocalBounds = BBox.FromPositionAndSize( 0, 256 )
		} );
	}
}

file class DebugTextSceneObject : SceneCustomObject
{
	private readonly TextRendering.Scope _scope;
	private readonly TextFlag _flags;
	private readonly float _scale;

	public DebugTextSceneObject( SceneWorld sceneWorld, TextRendering.Scope scope, TextFlag flags ) : base( sceneWorld )
	{
		_scope = scope;
		_flags = flags;

		var fontSize = _scope.FontSize;
		var scaleFactor = fontSize < 32 ? fontSize.Remap( 0, 32, 0, 1 ) : 1.0f;

		_scale = 0.33f * scaleFactor;
		_scope.FontSize = MathF.Max( fontSize, 32 );
	}

	public override void RenderSceneObject()
	{
		var rotation = Graphics.CameraRotation;
		Graphics.Attributes.Set( "WorldMat", Matrix.CreateScale( _scale ) * Matrix.CreateWorld( Vector3.Zero, rotation.Forward, rotation.Up ) );
		Graphics.Attributes.SetCombo( "D_WORLDPANEL", 1 );
		Graphics.DrawText( new Rect( 0 ), _scope, _flags );
	}
}
