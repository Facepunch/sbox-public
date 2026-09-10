
namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Handles the storage, progression and application of CSS transitions.
	/// </summary>
	[Hide]
	public Transitions Transitions { get; private set; }

	/// <summary>
	/// Returns true if this panel has any active CSS transitions.
	/// </summary>
	[Hide]
	public bool HasActiveTransitions => Transitions?.HasAny ?? false;

	/// <summary>
	/// Any transitions running, or about to run, will jump straight to the end.
	/// </summary>
	public void SkipTransitions()
	{
		Style.skipTransitions = true;
	}

}
