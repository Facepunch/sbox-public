using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// A page on the shelf (<see cref="IShelfPage"/>). The shelf keeps a page it's had up, hidden, while another's up -
/// hidden, it still ticks and is still asked for its hash, but has nothing to do. So it's only built from
/// <see cref="ShownHash"/>, and ticked through <see cref="TickShown"/>, while it's showing.
/// </summary>
public abstract class ShelfPage : Panel
{
	int shownHash;
	bool shown;

	/// <summary>
	/// Hidden, the last hash it had - so it isn't built again for being put away, only for what changed while it was
	/// </summary>
	protected sealed override int BuildHash() => IsVisible ? shownHash = ShownHash() : shownHash;

	public sealed override void Tick()
	{
		base.Tick();

		if ( IsVisible )
		{
			shown = true;
			TickShown();
		}
		else if ( shown )
		{
			shown = false;
			OnHidden();
		}
	}

	/// <summary>
	/// What it's built from - asked each frame while it's showing
	/// </summary>
	protected virtual int ShownHash() => 0;

	/// <summary>
	/// Each frame while it's showing
	/// </summary>
	protected virtual void TickShown() { }

	/// <summary>
	/// It's been hidden - another page is up
	/// </summary>
	protected virtual void OnHidden() { }
}
