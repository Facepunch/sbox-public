namespace Sandbox.Rendering;

public sealed partial class CommandList
{
	/// <summary>
	/// Access to simple 2D painting functions to draw shapes and text.
	/// </summary>
	[Obsolete( "Use Painter.Begin( commandList ) instead. Dispose the painter to submit its drawing." )]
	public HudPainter Paint => new( this );
}
