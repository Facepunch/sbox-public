using System;
using System.Collections.Generic;

namespace Sandbox.UI;

/// <summary>
/// What a drop would do here - what a panel answers while a drag hovers it, and what the OS
/// shows on the drag cursor. None rejects.
/// </summary>
public enum DropAction
{
	None,
	Copy,
	Move,
}

/// <summary>
/// Files or text dragged in from outside the app - the desktop, an explorer window. Fires as
/// "ondrop" on the deepest panel under the cursor and bubbles up from there: repeatedly with
/// <see cref="IsDrop"/> false while the drag hovers, so the panel can inspect the payload and
/// set <see cref="Action"/>, then once more with it true when the payload lands.
/// </summary>
public class DropEvent : PanelEvent
{
	/// <summary>
	/// Full paths of the dragged files. Empty when text is being dragged.
	/// </summary>
	public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

	/// <summary>
	/// The dragged text, or null when files are being dragged.
	/// </summary>
	public string Text { get; init; }

	/// <summary>
	/// Where the drag is, in the panel's coordinate space.
	/// </summary>
	public Vector2 Position { get; init; }

	/// <summary>
	/// False while the drag is still hovering - look at the payload and set
	/// <see cref="Action"/> to answer. True when it actually lands.
	/// </summary>
	public bool IsDrop { get; init; }

	/// <summary>
	/// What dropping here would do. Leave <see cref="DropAction.None"/> to reject - the OS
	/// drag cursor shows the answer while hovering.
	/// </summary>
	public DropAction Action { get; set; }

	public DropEvent( Panel target ) : base( "ondrop", target ) { }

	// The leave notification wears the same clothes under its own name
	internal DropEvent( Panel target, string name ) : base( name, target ) { }
}
