using Microsoft.Win32;
using System.Collections.Immutable;

namespace Sandbox;

public static partial class Gizmo
{
	/// <summary>
	/// Using pure primary colors is horrible. Lets make it easier to avoid.
	/// </summary>
	public static class Colors
	{
		public static Color Red { get; } = "#ff7096";
		public static Color Forward => Red;
		public static Color Pitch => Red;

		public static Color Green { get; } = "#ccff33";
		public static Color Left => Green;
		public static Color Yaw => Green;

		public static Color Blue { get; } = "#00a8e8";
		public static Color Up => Blue;
		public static Color Roll => Blue;

		public static Color Selected { get; } = "#fbfbfb";
		public static Color Hovered { get; } = "#90f1ef";
		public static Color Active { get; } = "#ffc600";
	}
}
