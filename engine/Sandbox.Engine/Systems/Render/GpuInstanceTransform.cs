using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// GPU instance transform - matches TransformBufferData_t in transform_buffer.fxc. Feed a buffer of
/// these to <see cref="Graphics.DrawModelInstancedIndirect(Model, GpuBuffer, GpuBuffer, int, int, RenderAttributes)"/>.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct GpuInstanceTransform
{
	public Vector4 Row0, Row1, Row2; // row-major float3x4
	public float Alpha;
	public uint Tint;
	public uint VertexCacheOffset;
	public uint BlendWeightCount;

	public static GpuInstanceTransform From( Transform transform )
	{
		// transposed - the instancing path does mul( matrix, position )
		var m = System.Numerics.Matrix4x4.Transpose( Matrix.FromTransform( transform ) );
		return new GpuInstanceTransform
		{
			Row0 = new Vector4( m.M11, m.M12, m.M13, m.M14 ),
			Row1 = new Vector4( m.M21, m.M22, m.M23, m.M24 ),
			Row2 = new Vector4( m.M31, m.M32, m.M33, m.M34 ),
			Alpha = 1.0f,
			Tint = 0xFFFFFF,
		};
	}
}
