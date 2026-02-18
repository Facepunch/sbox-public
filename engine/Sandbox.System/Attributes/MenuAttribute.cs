namespace Editor;

[AttributeUsage( AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true )]
public class MenuAttribute : Attribute
{
	public string Target { get; set; }
	public string Path { get; set; }
	public string Icon { get; set; }
	public int Priority { get; set; }

	[Obsolete( "Use [Shortcut] attribute" )]
	public string Shortcut { get; set; }

	public MenuAttribute( string target, string path, string icon = null )
	{
		Target = target;
		Path = path;
		Icon = icon;
	}
}
