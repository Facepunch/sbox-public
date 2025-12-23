using System;

namespace Editor;

/// <summary>
/// Opens an invisible popup above the game screen which allows you to left click once on the scene.
/// This is great for things like selecting something from the game scene.
/// </summary>
public class GameScenePicker : Widget
{
	public Action Destroyed { get; set; }

	public GameScenePicker() : base( null )
	{
		NoSystemBackground = true;
		TranslucentBackground = true;
		MinimumSize = 32;
		MouseTracking = true;
		IsPopup = true;
		Visible = true;
		DeleteOnClose = true;
		Cursor = CursorShape.None;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		Destroyed?.Invoke();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		var activeSession = SceneEditorSession.Active;
		var dock = activeSession.SceneDock;

		if ( activeSession is GameEditorSession gameEditorSession )
		{
			dock = gameEditorSession.Parent.SceneDock;
			var headerSize = new Vector2( 0, 36 );
			Position = dock.ScreenPosition + headerSize;
			Size = dock.ScreenRect.Size - headerSize;
		}
		else
		{
			Position = dock.ScreenPosition - 8;
			Size = dock.ScreenRect.Size + 16;
		}
	}

	Vector2 hoverPos;

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		hoverPos = e.LocalPosition;
		Update();
	}

	protected override void OnPaint()
	{
		var r = LocalRect;
		r.Size -= 2;

		var color = Color.Parse( "#df9194" ) ?? Color.White;

		Paint.SetPen( color, 8.0f, PenStyle.Dot );
		Paint.DrawRect( LocalRect );

	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		Destroy();
	}
}
