namespace Sandbox;

/// <summary>
/// The style of a stroke or CSS border. Solid is the default for width-only borders.
/// </summary>
public enum BorderStyle
{
	/// <summary>
	/// A single continuous line.
	/// </summary>
	Solid = 0,

	/// <summary>
	/// No border; its used width is zero.
	/// </summary>
	None = 1,

	/// <summary>
	/// No border; its used width is zero.
	/// </summary>
	Hidden = 2,

	/// <summary>
	/// A sequence of round dots.
	/// </summary>
	Dotted = 3,

	/// <summary>
	/// A sequence of dashes.
	/// </summary>
	Dashed = 4,

	/// <summary>
	/// Two lines separated by a gap.
	/// </summary>
	Double = 5,

	/// <summary>
	/// A border that appears carved into the surface.
	/// </summary>
	Groove = 6,

	/// <summary>
	/// A border that appears raised from the surface.
	/// </summary>
	Ridge = 7,

	/// <summary>
	/// A border that makes the box appear recessed.
	/// </summary>
	Inset = 8,

	/// <summary>
	/// A border that makes the box appear raised.
	/// </summary>
	Outset = 9
}
