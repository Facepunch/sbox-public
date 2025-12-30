using Sandbox.Internal;

namespace Sandbox;

[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Method )]
public class PropertyAttribute : Attribute, IClassNameProvider, ITitleProvider
{
	/// <summary>
	/// The internal name of this property. This should be lowercase with no spaces. If unset the lowercased C# variable name is used.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The user friendly name of this property. If unset, it will be auto generated from C# variable name.
	/// </summary>
	public string Title { get; set; }

	public PropertyAttribute() : base()
	{
	}

	/// <param name="internal_name">The internal name of this property. This should be lowercase with no spaces.</param>
	internal PropertyAttribute( string internal_name )
	{
		Name = internal_name.Replace( " ", "_" ).Replace( "\t", "_" );
	}


	string IClassNameProvider.Value => Name;
	string ITitleProvider.Value => Title;
}

/// <summary>
/// Mark this property as the key property - which means that it can represent the whole object in a single line, while
/// usually offering an advanced mode to view the entire object.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event )]
public class KeyPropertyAttribute : Attribute
{
	// alternative names, RepresentativeProperty, MainProeprty
}

/// <summary>
/// Tell the editor to try to display inline editing for this property, rather than hiding it behind a popup etc.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public class InlineEditorAttribute : Attribute
{
	public bool Label { get; set; } = true;
}

/// <summary>
/// Some properties are not meant for the average user, hide them unless they really want to see them.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public class AdvancedAttribute : Attribute
{
}
