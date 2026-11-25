using System;

namespace Editor;

[AttributeUsage( AttributeTargets.Method )]
public class ShortcutAttribute : Attribute
{

	public ShortcutAttribute( string identifier, string keyBind, ShortcutType type = ShortcutType.Widget )
	{
		Identifier = identifier;
		Keys = keyBind;
		Type = type;
		TargetOverride = null;
	}

	public ShortcutAttribute( string identifier, string keyBind, Type targetOverride, ShortcutType type = ShortcutType.Widget )
	{
		Identifier = identifier;
		Keys = keyBind;
		Type = type;
		TargetOverride = targetOverride;
	}

	public string Identifier { get; }
	public string Keys { get; }
	public ShortcutType Type { internal set; get; }
	public Type TargetOverride { get; set; }
	public int Priority { get; set; }
}

public enum ShortcutType
{
	Widget,
	Window,
	Application
}
