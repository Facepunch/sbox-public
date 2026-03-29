using Facepunch.ActionGraphs;
using Sandbox.Internal;

namespace Sandbox.Internal
{
	/// <summary>
	/// Provides a title or a "nice name" for DisplayInfo of a member or a type.
	/// </summary>
	public interface ITitleProvider
	{
		/// <summary>
		/// The title.
		/// </summary>
		public string Value { get; }
	}

	/// <summary>
	/// Provides placeholder text for DisplayInfo of a member or a type.
	/// </summary>
	public interface IPlaceholderProvider
	{
		/// <summary>
		/// The placeholder text.
		/// </summary>
		public string Value { get; }
	}

	/// <summary>
	/// Provides a description for DisplayInfo of a member or a type.
	/// </summary>
	public interface IDescriptionProvider
	{
		/// <summary>
		/// The description.
		/// </summary>
		public string Value { get; }
	}

	/// <summary>
	/// Provides category or group for DisplayInfo of a member or a type.
	/// </summary>
	public interface ICategoryProvider
	{
		/// <summary>
		/// The category.
		/// </summary>
		public string Value { get; }
	}

	/// <summary>
	/// Provides internal class name for DisplayInfo of a member or a type.
	/// </summary>
	public interface IClassNameProvider
	{
		/// <summary>
		/// The class name.
		/// Typically a class name is all lower case, has spaces replaced by underscores (_) or dashes (-) and contains no other special symbols.
		/// </summary>
		public string Value { get; }
	}

	/// <summary>
	/// Provides an icon for DisplayInfo of a member or a type.
	/// </summary>
	public interface IIconProvider
	{
		/// <summary>
		/// The icon. Typically this is the name of a <a href="https://fonts.google.com/icons">material icon</a>.
		/// </summary>
		public string Value { get; }
	}

	/// <summary>
	/// Provides an order number for DisplayInfo of a member or a type.
	/// </summary>
	public interface IOrderProvider
	{
		/// <summary>
		/// Order value, for sorting in menus.
		/// </summary>
		public int Value { get; }
	}

	/// <summary>
	/// Automatically added to codegenerated classes to let them determine their location
	/// This helps when looking for resources relative to them, like style sheets.
	/// Replaced in Sept 2023 by SourceLocationAttribute, which is added to classes and members.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
	public class ClassFileLocationAttribute : System.Attribute
	{
		public string Path { get; set; }

		public ClassFileLocationAttribute( string value )
		{
			Path = value;
		}
	}

	public interface ISourcePathProvider
	{
		string Path { get; }
	}

	public interface ISourceLineProvider : ISourcePathProvider
	{
		int Line { get; }
	}

	public interface ISourceColumnProvider : ISourceLineProvider
	{
		int Column { get; }
	}

	public interface IMemberNameProvider : ISourcePathProvider
	{
		string MemberName { get; }
	}

	/// <summary>
	/// Automatically added to classes and their members to let them determine their location
	/// This helps when looking for resources relative to them, like style sheets.
	/// </summary>
	[AttributeUsage( AttributeTargets.All, AllowMultiple = true, Inherited = false )]
	public class SourceLocationAttribute : ClassFileLocationAttribute, ISourceLineProvider
	{
		public int Line { get; set; }

		public SourceLocationAttribute( string path, int line ) : base( path )
		{
			Line = line;
		}
	}

	/// <summary>
	/// Automatically added to classes that implement OnUpdate()
	/// </summary>
	public interface IUpdateSubscriber { }

	/// <summary>
	/// Automatically added to classes that implement OnFixedUpdate()
	/// </summary>
	public interface IFixedUpdateSubscriber { }

	/// <summary>
	/// Automatically added to classes that implement OnPreRender()
	/// </summary>
	public interface IPreRenderSubscriber { }
}

/// <summary>
/// Add placeholder text, typically displayed for string properties when the text entry field is empty.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public class PlaceholderAttribute : System.Attribute, IPlaceholderProvider, IUninheritable
{
	/// <inheritdoc cref="IPlaceholderProvider.Value"/>
	public string Value { get; set; }
	string IPlaceholderProvider.Value => Value;

	public PlaceholderAttribute( string value )
	{
		Value = value;
	}
}

/// <summary>
/// Set the class name for this type or member.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public class ClassNameAttribute : System.Attribute, IClassNameProvider, IUninheritable
{
	/// <inheritdoc cref="IClassNameProvider.Value"/>
	public string Value { get; set; }
	string IClassNameProvider.Value => Value;

	public ClassNameAttribute( string value )
	{
		Value = value;
	}
}

/// <summary>
/// Sets the title or a "nice name" of a type or a type member.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public class TitleAttribute : System.Attribute, ITitleProvider, ITitleAttribute, IUninheritable
{
	/// <inheritdoc cref="ITitleProvider.Value"/>
	public string Value { get; set; }
	string ITitleProvider.Value => Value;

	public TitleAttribute( string value )
	{
		Value = value;
	}
}

/// <summary>
/// Hint that this type is expected to be this. This is used internally for
/// the editor UX to hint that a type of a value should be a specific type.
/// </summary>
public sealed class TypeHintAttribute : System.Attribute
{
	/// <summary>
	/// The type we're hinting towards
	/// </summary>
	public Type HintedType { get; set; }

	public TypeHintAttribute( Type hint )
	{
		HintedType = hint;
	}
}

/// <summary>
/// Sets the description of a type or a type member. This attribute is usually applied automatically by codegen based on the XML comment of the type or member.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )]
public class DescriptionAttribute : System.Attribute, IDescriptionProvider, IDescriptionAttribute, IUninheritable
{
	/// <inheritdoc cref="IDescriptionProvider.Value"/>
	public string Value { get; set; }
	string IDescriptionProvider.Value => Value;

	public DescriptionAttribute( string value )
	{
		Value = value;
	}
}

/// <summary>
/// Sets the category or the group of a type or a type member.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public class CategoryAttribute : System.Attribute, ICategoryProvider, IGroupAttribute, IUninheritable
{
	/// <inheritdoc cref="ICategoryProvider.Value"/>
	public string Value { get; set; }
	string ICategoryProvider.Value => Value;

	public CategoryAttribute( string value )
	{
		Value = value;
	}
}

/// <summary>
/// Sets the category or the group of a type or a type member.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public class GroupAttribute : System.Attribute, ICategoryProvider, IGroupAttribute, IUninheritable
{
	/// <inheritdoc cref="ICategoryProvider.Value"/>
	public string Value { get; set; }

	public string Icon { get; set; }

	/// <summary>
	/// If true then the group should start closed
	/// </summary>
	public bool StartFolded { get; set; }

	string ICategoryProvider.Value => Value;

	public GroupAttribute( string value )
	{
		Value = value;
	}
}

/// <summary>
/// Very much like a GroupAttribute, except we're indicating that the group can be toggle on and off using the named property
/// </summary>
public class ToggleGroupAttribute : GroupAttribute
{
	public string Label { get; set; }

	public ToggleGroupAttribute( string value ) : base( value )
	{
	}
}

/// <summary>
/// Sets the icon of a type or a type member. Colors are expected in HTML formats, like "rgb(255,255,255)" or "#FFFFFF".
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public sealed class IconAttribute : System.Attribute, IIconProvider, IIconAttribute, IUninheritable
{
	/// <inheritdoc cref="IIconProvider.Value"/>
	public string Value { get; set; }
	string IIconProvider.Value => Value;

	/// <summary>
	/// The preferred background color for the icon.
	/// </summary>
	public Color? BackgroundColor { get; private set; }

	/// <summary>
	/// The preferred color of the icon itself.
	/// </summary>
	public Color? ForegroundColor { get; private set; }

	public IconAttribute( string icon, string bgColor, string fgColor )
	{
		Value = icon;
		BackgroundColor = bgColor;
		ForegroundColor = fgColor;
	}

	public IconAttribute( string icon )
	{
		Value = icon;
	}
}

/// <summary>
/// Visual order of this member for UI purposes.
/// This info can then be retrieved via DisplayInfo library.
/// </summary>
public sealed class OrderAttribute : System.Attribute
{
	/// <summary>
	/// The visual order.
	/// </summary>
	public int Value { get; set; }

	public OrderAttribute( int value )
	{
		Value = value;
	}
}


namespace Sandbox
{
	/// <summary>
	/// Expand the value editor to fill the next line in the inspector, leaving the title above it
	/// </summary>
	public sealed class WideModeAttribute : System.Attribute
	{
		public bool HasLabel { get; set; } = true;
	}

	/// <summary>
	/// Display this in the inspector - but don't let anyone edit it
	/// </summary>
	public sealed class ReadOnlyAttribute : System.Attribute
	{
	}

	/// <summary>
	/// When applied to a string property, show a multi-line text box instead of a single line.
	/// </summary>
	public sealed class TextAreaAttribute : System.Attribute
	{
	}

	/// <summary>
	/// When applied to a string property, use an input action selector.
	/// </summary>
	public sealed class InputActionAttribute : System.Attribute
	{
	}

	/// <summary>
	/// When applied to a Type property, allows you to specify a Type that the property's value must derive from.
	/// </summary>
	public sealed class TargetTypeAttribute : System.Attribute
	{
		/// <summary>
		/// The type that the property's value must derive from.
		/// </summary>
		public Type Type { get; set; }

		public TargetTypeAttribute( Type type )
		{
			Type = type;
		}
	}

	/// <summary>
	/// When applied to a string property, uses a font name selector.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
	public sealed class FontNameAttribute : System.Attribute
	{
	}

	/// <summary>
	/// When applied to a string property, uses a Material Icon selector.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
	public sealed class IconNameAttribute : System.Attribute
	{
	}

	/// <summary>
	/// When applied to a Color property, allows you to specify whether the color should have an alpha channel and/or be in HDR.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
	public sealed class ColorUsageAttribute : System.Attribute
	{
		public bool HasAlpha { get; set; } = true;
		public bool IsHDR { get; set; } = true;

		public ColorUsageAttribute( bool hasAlpha = true, bool isHDR = true )
		{
			HasAlpha = hasAlpha;
			IsHDR = isHDR;
		}
	}

	/// <summary>
	/// Sets the category or the group of a type or a type member.
	/// This info can then be retrieved via DisplayInfo library.
	/// </summary>
	public class FeatureAttribute : System.Attribute, IUninheritable
	{
		/// <summary>
		/// How we will group features together
		/// </summary>
		public string Identifier { get; set; }

		/// <summary>
		/// Title of the feature. Keep it short please!
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// The description of the feature
		/// </summary>
		public string Description { get; set; }

		/// <summary>
		/// Icon to show next to the feature
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// The color of the feature button. Helps group things, helps things to stand out. Defaults to white.
		/// </summary>
		public EditorTint Tint { get; set; }


		public FeatureAttribute( string value )
		{
			Identifier = value;
			Title = value;
		}
	}

	public class TintAttribute : System.Attribute
	{
		public EditorTint Tint;

		public TintAttribute( EditorTint tint )
		{
			Tint = tint;
		}
	}

	public enum EditorTint
	{
		White,
		Pink,
		Green,
		Yellow,
		Blue,
		Red
	}

	/// <summary>
	/// Mark a boolean property as a feature toggle
	/// </summary>
	public class FeatureEnabledAttribute : FeatureAttribute, IUninheritable
	{
		public FeatureEnabledAttribute( string value ) : base( value )
		{

		}
	}

	/// <summary>
	/// Add a header above this property
	/// </summary>
	[AttributeUsage( AttributeTargets.Property )]
	public sealed class HeaderAttribute : System.Attribute
	{
		public string Title { get; set; }

		public HeaderAttribute( string header )
		{
			Title = header;
		}
	}

	/// <summary>
	/// Add a space above this property
	/// </summary>
	[AttributeUsage( AttributeTargets.Property )]
	public sealed class SpaceAttribute : System.Attribute
	{
		public float Height { get; set; } = 22;

		public SpaceAttribute( float height = 22 )
		{
			Height = height;
		}
	}


	/// <summary>
	/// Add a link to some documentation for this component, or <see langword="property"/>
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Class )]
	public sealed class HelpUrlAttribute : System.Attribute
	{
		public string Url { get; set; }

		public HelpUrlAttribute( string url )
		{
			Url = url;
		}
	}

	/// <summary>
	/// Draw a box with information above this property
	/// </summary>
	public sealed class InfoBoxAttribute : System.Attribute, IUninheritable
	{
		/// <summary>
		/// Message to display
		/// </summary>
		public string Message { get; set; }

		/// <summary>
		/// The icon to show (material icons)
		/// </summary>
		public string Icon { get; set; }

		/// <summary>
		/// The color of this info box. Helps group things, helps things to stand out. Defaults to blue.
		/// </summary>
		public EditorTint Tint { get; set; } = EditorTint.Blue;

		public InfoBoxAttribute( string message, string icon = "info", EditorTint tint = EditorTint.Blue )
		{
			Message = message;
			Icon = icon;
			Tint = tint;
		}
	}

	/// <summary>
	/// When applied to a Vector property, provides normal selection tools.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
	public sealed class NormalAttribute : System.Attribute
	{
	}
}
