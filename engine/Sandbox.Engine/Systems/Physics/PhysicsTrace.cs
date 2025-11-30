using System.Runtime.InteropServices;

internal unsafe static partial class PhysicsTrace
{
	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate byte PhysicsFilterFunction_t( int shapeIdent );

	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct Request
	{
		public const int NumTagFields = 8;

		public IntPtr World;
		public IntPtr Body;
		public Vector3 StartPos;
		public Vector3 EndPos;
		public fixed uint TagRequire[NumTagFields];
		public fixed uint TagAny[NumTagFields];
		public fixed uint TagExclude[NumTagFields];
		public IntPtr FilterDelegate;
		public byte TriggerFilter;
		public ushort ObjectSetMask;
		public byte UseHitPosition;
		public Shape StartShape;

		internal enum ShapeType
		{
			Sphere = 0,
			Box = 1,
			Capsule = 2,
			Cylinder = 3
		}

		[StructLayout( LayoutKind.Sequential )]
		internal struct Shape
		{
			internal ShapeType Type;
			internal Rotation StartRot;
			internal Vector3 Mins;
			internal Vector3 Maxs;
			internal float Radius;
		}
	}

	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct Result
	{
		public const int NumTagFields = 16; // SBOX_MAX_COLLISION_TAG_COUNT

		public Vector3 StartPos;
		public Vector3 EndPos;
		public Vector3 HitPos;
		public Vector3 Normal;
		public float Fraction;
		public byte StartedInSolid;
		public int PhysicsBodyHandle;
		public int PhysicsShapeHandle;
		public int SurfaceProperty;
		public int TriangleIndex;
		public fixed uint Tags[NumTagFields];
	}
}
