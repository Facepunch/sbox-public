namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Pose selectors pick an option when they're entered and hold it, while value selectors follow
/// their conditions every frame. That difference catches people out, so the pose ones wear a badge
/// and the node's tooltip explains the rest.
/// </summary>
internal static class EntrySelectionHint
{
	private const float BadgeSize = 15f;

	public static void Draw( Rect rect )
	{
		var badge = rect.Shrink( 5 ).Align( new Vector2( BadgeSize ), TextFlag.RightTop );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground.WithAlpha( 0.8f ) );
		Paint.DrawRect( badge, BadgeSize * 0.5f );

		Paint.SetPen( Theme.Red.WithAlpha( 0.9f ) );
		Paint.DrawIcon( badge, "login", 10 );
	}
}
