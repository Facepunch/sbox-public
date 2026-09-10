namespace Editor.SpriteEditor;

[CustomEditor( typeof( Sprite.BroadcastEventType ) )]
public class BroadcastEventTypeControlWidget : EnumControlWidget
{
	protected override float? MenuWidthOverride => 300;

	public BroadcastEventTypeControlWidget( SerializedProperty property ) : base( property )
	{
		FixedWidth = Theme.ControlHeight + 7;
	}
}
