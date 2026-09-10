using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Draws text in screenspace
/// </summary>
internal class TextSceneObject : SceneCustomObject
{
	public Vector2 ScreenPos { get; set; }
	public Vector2 ScreenSize { get; set; } = 1000f;

	/// <summary>
	/// this argument is short sighted and stupid, don't keep using it
	/// </summary>
	public float AngleDegrees { get; set; } = 0f;

	public TextFlag TextFlags { get; set; } = TextFlag.Center;

	public TextRendering.Scope TextBlock;

	CommandList _commandList = new( "EditorText" );

	public TextSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		TextBlock = TextRendering.Scope.Default;
	}

	public void BuildCommandList()
	{
		_commandList.Reset();

		var pos = ScreenPos;

		if ( TextFlags.Contains( TextFlag.CenterHorizontally ) )
		{
			pos.x -= ScreenSize.x * 0.5f;
		}

		if ( TextFlags.Contains( TextFlag.CenterVertically ) )
		{
			pos.y -= ScreenSize.y * 0.5f;
		}

		if ( TextFlags.Contains( TextFlag.Bottom ) )
		{
			pos.y -= ScreenSize.y;
		}

		var rect = new Rect( pos, ScreenSize );

		if ( AngleDegrees == 0f )
		{
			_commandList.DrawText( TextBlock, rect, TextFlags );
		}
		else
		{
			_commandList.DrawText( TextBlock, rect, TextFlags, AngleDegrees );
		}
	}

	public override void RenderSceneObject()
	{
		_commandList.ExecuteOnRenderThread();
	}
}

internal class WorldTextSceneObject : SceneCustomObject
{
	public string Text { get; set; }
	public string FontName { get; set; } = "Roboto";
	public float FontSize { get; set; } = 12.0f;
	public float FontWeight { get; set; } = 500.0f;
	public TextFlag TextFlags { get; set; } = TextFlag.Center;
	public Color Color { get; set; } = Color.White;
	public bool IgnoreDepth { get; set; } = false;

	CommandList _commandList = new( "WorldText" );

	public WorldTextSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		RenderLayer = SceneRenderLayer.OverlayWithDepth;
	}

	public void BuildCommandList()
	{
		_commandList.Reset();

		_commandList.Attributes.SetCombo( "D_WORLDPANEL", 1 );
		_commandList.Attributes.SetCombo( "D_NO_ZTEST", IgnoreDepth ? 1 : 0 );

		Matrix mat = Matrix.CreateRotation( Rotation.From( 0, 0, 0 ) );
		_commandList.Attributes.Set( "WorldMat", mat );

		var scope = new TextRendering.Scope( Text, Color, FontSize, FontName, (int)FontWeight );
		_commandList.DrawText( scope, new Rect( 0 ), TextFlags | TextFlag.DontClip );
	}

	public override void RenderSceneObject()
	{
		_commandList.ExecuteOnRenderThread();
	}
}
