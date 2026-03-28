namespace Sandbox;

/// <summary>
/// Flags to search for Components.
/// I've named this something generic because I think we can re-use it to search for GameObjects too.
/// </summary>
[Flags]
public enum FindMode
{
	/// <summary>
	/// Components that are enabled
	/// </summary>
	Enabled = 1,

	/// <summary>
	/// Components that are disabled
	/// </summary>
	Disabled = 2,

	/// <summary>
	/// Components in this object
	/// </summary>
	InSelf = 4,

	/// <summary>
	/// Components in our parent
	/// </summary>
	InParent = 8,

	/// <summary>
	/// Components in all ancestors (parent, their parent, their parent, etc)
	/// </summary>
	InAncestors = 16,

	/// <summary>
	/// Components in our children
	/// </summary>
	InChildren = 32,

	/// <summary>
	/// Components in all decendants (our children, their children, their children etc)
	/// </summary>
	InDescendants = 64,


	EnabledInSelf = Enabled | InSelf,
	EnabledInSelfAndDescendants = Enabled | InSelf | InDescendants,
	EnabledInSelfAndChildren = Enabled | InSelf | InChildren,

	DisabledInSelf = Disabled | InSelf,
	DisabledInSelfAndDescendants = Disabled | InSelf | InDescendants,
	DisabledInSelfAndChildren = Disabled | InSelf | InChildren,

	EverythingInSelf = Enabled | InSelf | Disabled,
	EverythingInSelfAndDescendants = Enabled | InSelf | Disabled | InDescendants,
	EverythingInSelfAndChildren = Enabled | InSelf | Disabled | InChildren,
	EverythingInSelfAndParent = Enabled | InSelf | Disabled | InParent,
	EverythingInSelfAndAncestors = Enabled | InSelf | Disabled | InAncestors,
	EverythingInAncestors = Enabled | Disabled | InAncestors,
	EverythingInChildren = Enabled | Disabled | InChildren,
	EverythingInDescendants = Enabled | Disabled | InDescendants,
}
