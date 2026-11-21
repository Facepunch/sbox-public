namespace Sandbox.UI;

/// <summary>
/// Edits a rope material setting as a row of recommended materials, each previewed as a rendered rope, with a
/// button that opens the full material picker (workshop included). The value is a local path or a workshop
/// package ident.
/// </summary>
public sealed class RopeMaterialsAttribute : System.Attribute
{
	/// <summary>
	/// The recommended materials, in the order they're shown. Without any, the usual rope ones (<see cref="RopeLook"/>).
	/// </summary>
	public string[] Paths { get; }

	bool UsesRopeLook => ReferenceEquals( Paths, RopeLook.Presets );

	string[] _titles;
	string[] _glow;

	/// <summary>
	/// Tooltips for <see cref="Paths"/>, by index. Missing ones use the file name.
	/// </summary>
	public string[] Titles
	{
		get => _titles ?? (UsesRopeLook ? RopeLook.Titles : null);
		set => _titles = value;
	}

	/// <summary>
	/// Which of <see cref="Paths"/> are drawn as glowing beams, so their previews are drawn the same way.
	/// </summary>
	public string[] Glow
	{
		get => _glow ?? (UsesRopeLook ? RopeLook.Glow : null);
		set => _glow = value;
	}

	/// <summary>
	/// A <see cref="Color"/> property on the same object to tint the previews with, so they match what'll be placed.
	/// </summary>
	public string TintProperty { get; set; }

	public RopeMaterialsAttribute( params string[] paths )
	{
		Paths = paths is { Length: > 0 } ? paths : RopeLook.Presets;
	}
}
