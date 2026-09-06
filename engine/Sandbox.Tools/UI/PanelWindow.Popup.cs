using System;

namespace Editor;

public partial class PanelWindow
{
	/// <summary>
	/// Open a popup - a borderless window that sits above this one and can hang outside it, the
	/// way an OS menu does. The position is in this window's client pixels, which is what a panel's
	/// <c>Box.Rect</c> is already in.
	/// <para>
	/// There is no size to pass. The popup is born hidden at the parent's size, shrinks to whatever
	/// is put in it, and only then appears - so what it ends up as is the size of its contents.
	/// </para>
	/// </summary>
	public static PanelWindow Popup( PanelWindow parent, Vector2 position, bool ignoresInput = false )
	{
		ArgumentNullException.ThrowIfNull( parent );

		// SDL popup windows position themselves relative to their parent, in window coordinates
		return new PopupWindow( parent, parent.PixelsToWindow( position ), ignoresInput );
	}
}
