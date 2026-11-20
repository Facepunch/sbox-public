using Sandbox.UI;

/// <summary>
/// Base class for pages opened in the utility tab right panel.
/// Decorate subclasses with [Icon], [Title], [Group], and [Order].
/// </summary>
public abstract class UtilityPage : Panel
{
	/// <summary>
	/// Return false to hide this page from the utility menu.
	/// </summary>
	public virtual bool IsPageVisible() => true;
}
