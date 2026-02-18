namespace Sandbox;

public static partial class Gizmo
{
	public static SceneSettings Settings => Active?.Settings;

	[Expose]
	public enum GridAxis
	{
		XY,
		YZ,
		ZX,
	}

	[Expose]
	public class SceneSettings
	{
		/// <summary>
		/// How do we want to edit this? Usually something like "position", "rotation", "scale" etc
		/// </summary>
		public string EditMode { get; set; } = "position";

		/// <summary>
		/// Do we want to let the user select things in the current mode?
		/// </summary>
		public bool Selection { get; set; } = true;

		/// <summary>
		/// What is the current view mode? 3d, 2d, ui?
		/// </summary>
		public string ViewMode { get; set; } = "3d";

		/// <summary>
		/// Are gizmos enabled?
		/// </summary>
		public bool GizmosEnabled { get; set; } = true;

		/// <summary>
		/// How big to show the gizmos
		/// </summary>
		[Range( 0, 2 )]
		public float GizmoScale { get; set; } = 1.0f;

		/// <summary>
		/// Grid spacing
		/// </summary>
		[Range( 0.125f, 128 ), Step( 1 )]
		public float GridSpacing { get; set; } = 32.0f;

		/// <summary>
		/// Snap positions to the grid
		/// </summary>
		public bool SnapToGrid { get; set; } = false;

		/// <summary>
		/// Snap angles
		/// </summary>
		public bool SnapToAngles { get; set; } = true;

		/// <summary>
		/// Grid spacing
		/// </summary>
		[Range( 0.25f, 180f ), Step( 5 )]
		public float AngleSpacing { get; set; } = 15;

		/// <summary>
		/// Editing in local space
		/// </summary>
		public bool GlobalSpace { get; set; } = false;

		/// <summary>
		/// Should we show lines representing GameObject references in action graphs?
		/// </summary>
		public bool DebugActionGraphs { get; set; } = false;

		/// <summary>
		/// Which gizmos are disabled
		/// </summary>
		private Dictionary<Type, bool> DisabledGizmos = new();

		/// <summary>
		/// Check if a gizmo type is enabled
		/// </summary>
		public bool IsGizmoEnabled( Type type )
		{
			return !DisabledGizmos.TryGetValue( type, out var disabled ) || !disabled;
		}

		/// <summary>
		/// Set the enabled state of a gizmo type
		/// </summary>
		public void SetGizmoEnabled( Type type, bool enabled )
		{
			DisabledGizmos[type] = !enabled;
		}

		/// <summary>
		/// Clear all enabled gizmos
		/// </summary>
		public void ClearEnabledGizmos()
		{
			DisabledGizmos.Clear();
		}
	}
}
