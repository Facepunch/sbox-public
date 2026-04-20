namespace Sandbox;

/// <summary>
/// When added to a method - the inspector will show a button for it.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public class ButtonAttribute : System.Attribute
{
	public string Icon { get; set; }
	public string Title { get; set; }
	//public string Group { get; set; }
	//public Color Color { get; set; } = Color.White;

	public ButtonAttribute( string value = "", string icon = "" )
	{
		Title = value;
		Icon = icon;
	}
}


/// <summary>
/// When added to a property on a Component, we'll try to make that component value non null.
/// We will first look on the GameObject for the component type. If it's not found, we'll create one.
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
public class RequireComponentAttribute : System.Attribute
{
	public FindMode FindMode { get; set; }

	public RequireComponentAttribute( FindMode findMode = FindMode.EverythingInSelf )
	{
		FindMode = findMode;
	}
}
