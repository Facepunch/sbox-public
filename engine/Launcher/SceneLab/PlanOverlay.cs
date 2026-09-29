using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox.SceneLab;

/// <summary>
/// The managed renderer's frame plan over the viewport (<b>View &gt; Frame Plan</b>, or <c>-plan [level]</c>): the engine's
/// <c>overlay_scene_plan</c>, drawn by a panel since a panel app has no engine overlay pass. Level 1 shows the layers that ran
/// or were culled, level 2 every layer.
/// </summary>
internal sealed class PlanOverlay : Panel
{
	/// <summary>
	/// 0 hides it.
	/// </summary>
	public int Level { get; set; }

	public PlanOverlay()
	{
		AddClass( "scenelab-plan" );
	}

	public override void Tick()
	{
		base.Tick();
		SetClass( "hidden", Level <= 0 );
	}

	public override void OnDraw( Painter painter )
	{
		base.OnDraw( painter );
		if ( Level <= 0 ) return;

		// To the right of the stats
		var pos = new Vector2( 280, 12 );
		DebugOverlay.ScenePlan.Draw( painter, ref pos, Level );
	}

	/// <summary>
	/// The plan drawn into an image the size given, over a dark backdrop, for a capture to save.
	/// </summary>
	public static Color32[] Render( int width, int height, int level )
	{
		using var texture = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).Create( "Scene Lab plan" );
		using ( var painter = Painter.Begin( texture ) )
		{
			painter.Fill = new Color( 0.06f, 0.07f, 0.09f );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( 0, 0, width, height ) );

			var pos = new Vector2( 16, 16 );
			DebugOverlay.ScenePlan.Draw( painter, ref pos, level );
		}

		return texture.GetPixels();
	}
}
