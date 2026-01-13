namespace Sandbox;

public static partial class Gizmo
{
	/// <summary>
	/// Whenever Gizmo.State is called, this is copied, stored and restored. This
	/// should hold whatever data is important to reset at the end of a scope. We
	/// should be really careful not to fill this with too much shit.
	/// </summary>
	internal struct ScopeState
	{
		public Transform Transform;
		public object Object;
		public string Path;
		public float HitDepthBias;
		public int Create;
		public bool IgnoreDepth;
		public float LineThickness;
		public bool CullBackfaces;
		public bool CanInteract;

		public Color Color;
		internal Color32 Color32;
	}
}
