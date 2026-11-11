using System;
namespace Sandbox.UI;

/// <summary>
/// Will automatically apply the named stylesheet to the Panel.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class StyleSheetAttribute : System.Attribute
{
	/// <summary>
	/// File name of the style sheet file.
	/// </summary>
	public string Name;

	/// <summary>
	/// Applies the named stylesheet, or the stylesheet beside the panel's source file when no name is given.
	/// </summary>
	public StyleSheetAttribute( string name = null )
	{
		Name = name;
	}
}
