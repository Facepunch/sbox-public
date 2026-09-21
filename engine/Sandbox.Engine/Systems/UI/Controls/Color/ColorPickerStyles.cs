namespace Sandbox.UI;

/// <summary>
/// Style shared between the picker's parts, spliced into each one's inline sheet.
/// </summary>
internal static class ColorPickerStyles
{
	/// <summary>
	/// The round marker the strips, square and wheels move about.
	/// </summary>
	public const string Handle = """
		.color-handle
		{
			position: absolute;
			width: 14px;
			height: 14px;
			border-radius: 50%;
			border: 2px solid #fff;
			box-shadow: 0 0 0 1px #0008;
			transform: translateX( -50% ) translateY( -50% );
			pointer-events: none;
			z-index: 2;
		}
		""";
}
