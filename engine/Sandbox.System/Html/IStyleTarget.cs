
namespace Sandbox.UI;

/// <summary>
/// Everything the style system needs to work out a style
/// </summary>
public interface IStyleTarget
{
	string ElementName { get; }
	string Id { get; }
	PseudoClass PseudoClass { get; }
	IStyleTarget Parent { get; }
	IReadOnlyList<IStyleTarget> Children { get; }
	int SiblingIndex { get; }

	bool HasClass( string classname );
	bool HasClasses( string[] classes );

	/// <summary>
	/// Returns true if this is ::before or ::after
	/// </summary>
	internal bool IsBeforeOrAfter => PseudoClass.Contains( PseudoClass.Before ) || PseudoClass.Contains( PseudoClass.After );
}
