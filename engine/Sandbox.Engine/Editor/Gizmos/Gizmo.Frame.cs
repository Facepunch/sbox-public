using System.Collections.Immutable;

namespace Sandbox;

public static partial class Gizmo
{
	//
	// We store states for 'now' and 'previous' and a 'builder'
	// These are combined and queries to work out what is happening
	//
	internal struct Frame
	{
		public ImmutableHashSet<string> SelectedPath;
		public string HoveredPath;
		public object HoveredObject;
		public string PressedPath;

		public Inputs Input;
		public bool Click;
		public float HitDistance;
	}
}
