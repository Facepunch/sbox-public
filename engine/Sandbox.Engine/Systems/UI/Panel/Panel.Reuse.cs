namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Advances once per root recording. A panel stamps the current value on itself and its ancestors when something it
	/// draws changes, so a recording made before that stamp is out of date for every panel holding the change.
	/// </summary>
	internal static long PaintClock = 1;

	/// <summary>
	/// The <see cref="PaintClock"/> value when this panel or a descendant last changed something it draws.
	/// </summary>
	internal long PaintChangedAt;

	/// <summary>
	/// Called when something this panel draws changes: its layout, style, children, visibility or content. Stamps the
	/// panel and its ancestors, stopping at one already stamped since the last recording, whose ancestors are too.
	/// </summary>
	internal void MarkPaintChanged()
	{
		var clock = PaintClock;
		for ( var panel = this; panel is not null && panel.PaintChangedAt != clock; panel = panel._parent )
			panel.PaintChangedAt = clock;
	}

	/// <summary>
	/// What this panel's OnDraw reads besides its layout and style, for panel types whose drawing follows from those alone:
	/// equal keys draw the same thing. False when the drawing can't be described this way, so it's recorded every frame.
	/// Only asked after OnDraw ran, once its lazy state is settled.
	/// </summary>
	internal virtual bool TryGetDrawKey( out DrawKey key )
	{
		key = default;
		return false;
	}

	/// <summary>
	/// Whether <see cref="TryGetDrawKey"/> would give <paramref name="drawn"/> now. Recordings ask this of every keyed panel
	/// they keep each frame, so panel types with many instances answer it without building the key.
	/// </summary>
	internal virtual bool DrawKeyHolds( in DrawKey drawn ) => TryGetDrawKey( out var now ) && now == drawn;

	/// <summary>
	/// Where this panel's subtree was recorded last frame and what it was recorded under, so it can be replayed while unchanged.
	/// </summary>
	internal PanelRecording.Subtree SubtreeRecording;

	/// <summary>
	/// Panels whose subtree is worth keeping: ones with children, or drawing of their own beyond a CSS box. The root is kept whole.
	/// </summary>
	internal bool WantsSubtreeRecording => this is not RootPanel && (HasChildren || _hasDrawCallback);

	/// <summary>
	/// A description of what a panel's OnDraw drew, compared between frames.
	/// </summary>
	internal readonly record struct DrawKey( object Source, int Version, Rect Rect, GpuFontText.Options Options );
}
