
namespace Sandbox.UI;

public partial class Panel : IStyleTarget
{
	string IStyleTarget.ElementName => ElementName;
	string IStyleTarget.Id => Id;
	PseudoClass IStyleTarget.PseudoClass => PseudoClass;
	IStyleTarget IStyleTarget.Parent => Parent;
	IReadOnlyList<IStyleTarget> IStyleTarget.Children => _children;
	bool IStyleTarget.HasClasses( string[] classes ) => HasClasses( classes );
	int IStyleTarget.SiblingIndex => SiblingIndex;

}
