using System;
using NativeEngine;

namespace ResourceTests;

/// <summary>
/// Source 2's empty render device (the game's and the editor's when Borealis.Gpu draws) reads only buffers it made. A
/// compiled model's draw calls name their buffers by index until a device makes them, and the empty device makes none:
/// an index such as 3 reaching it as a handle was read as a pointer to a kept buffer (the editor's crash matching a map's
/// or a model's input layouts to a standard material as it loaded).
/// </summary>
[TestClass]
public class EmptyDeviceForeignHandleTests
{
	[TestMethod]
	public unsafe void HandlesTheDeviceDidNotMakeAreNotRead()
	{
		var data = new byte[64];
		fixed ( byte* pData = data )
		{
			for ( nint handle = 3; handle < 64; handle++ )
			{
				MeshGlue.SetVertexBufferData( handle, (IntPtr)pData, data.Length, 0 );
				MeshGlue.SetIndexBufferData( handle, (IntPtr)pData, data.Length, 0 );
			}
		}
	}
}
