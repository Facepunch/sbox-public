using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// A page on the shelf, and how it sits on it - by default it fills the shelf up to the top of the screen, scrolling its
/// own columns. The shelf keeps a page it's had up, hidden, while another's up -
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

	/// <summary>
	/// Brought up on the shelf - from the dock, or from another page on it. Not when it's gone back to. It starts again
	/// from what it was opened with.
	/// </summary>
	public virtual void OnShelfShown() { }

	/// <summary>
	/// Up to the top of the screen, whatever's on it. Otherwise as tall as <see cref="Height"/> says.
	/// </summary>
	public virtual bool Fills => true;

	/// <summary>
	/// How tall it wants the shelf, when it doesn't fill it - given how tall it is (<paramref name="tall"/>). The shelf's
	/// never taller than <paramref name="room"/>.
	/// </summary>
	public virtual float Height( float room, float tall ) => room;

	/// <summary>
	/// When it doesn't fill the shelf, whether the shelf scrolls it past its height - or just shows what fits
	/// </summary>
	public virtual bool Scrolls => true;

	/// <summary>
	/// Right down to the dock's top - fading off into it
	/// </summary>
	public virtual bool ToDock => false;

	/// <summary>
	/// The whole screen over the dock, edge to edge - for a scene that's the page
	/// </summary>
	public virtual bool EdgeToEdge => false;

	/// <summary>
	/// Clicks off its own panels go through to the blur behind the shelf, which puts it away - for a page that doesn't
	/// fill the shelf's width
	/// </summary>
	public virtual bool ClicksThrough => false;
}
