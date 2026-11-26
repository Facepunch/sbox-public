
unsafe struct MeshTraceInput
{
	public Vector3 start;
	public Vector3 end;
	public fixed uint tagRequire[8];
	public fixed uint tagAny[8];
	public fixed uint tagExclude[8];
	public int cullMode;
	public IntPtr filterDelegate;
};

struct MeshTraceOutput
{
	public float distance;
	public Vector3 position;
	public Vector3 normal;
	public IntPtr material;
	public Transform transform;
	public int sceneobjectHandle;
	public int triangleIndex;
	public Vector2 uv;

	public IntPtr v0;
	public IntPtr v1;
	public IntPtr v2;
};
