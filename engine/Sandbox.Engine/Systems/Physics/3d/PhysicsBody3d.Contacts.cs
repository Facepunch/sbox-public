using System.Runtime.InteropServices;

namespace Sandbox;

internal sealed partial class PhysicsBody3d
{
	[StructLayout( LayoutKind.Sequential )]
	internal struct ScrapeContact
	{
		public int OtherBodyIndex;
		public Vector3 Point;
		public Vector3 Normal;
		public float FrictionForce;
		public int SelfSurfaceIndex;
		public int OtherSurfaceIndex;

		public readonly PhysicsBody OtherBody => HandleIndex.Get<PhysicsBody3d>( OtherBodyIndex )?.Owner;
		public readonly Surface Surface => Surface.All.GetValueOrDefault( SelfSurfaceIndex );
	}

	internal unsafe int GetScrapeContacts( Span<ScrapeContact> buffer )
	{
		if ( native.IsNull || buffer.IsEmpty )
			return 0;
		fixed ( ScrapeContact* pointer = buffer )
			return native.GetScrapeContacts( (IntPtr)pointer, buffer.Length );
	}
}
