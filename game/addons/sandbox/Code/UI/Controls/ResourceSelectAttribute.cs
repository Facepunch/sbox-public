namespace Sandbox.UI;

public sealed class ResourceSelectAttribute : System.Attribute
{
	public string Extension { get; set; }
	public bool AllowPackages { get; set; }
	public bool Popup { get; set; }
}
