using System.Runtime.InteropServices;
using Borealis.Gpu;
using Borealis.Gpu.Vulkan;

namespace BorealisGpuTests;

/// <summary>
/// Validation messages name what they're about (<see cref="Device.DescribeObjects"/>): the named objects the layer lists
/// and the command-buffer labels open at the call, so a message from the game's log leads to its pipeline and pass.
/// </summary>
[TestClass]
public class ValidationMessageTests
{
	[TestMethod]
	public unsafe void NamesTheObjectsAndOpenLabels()
	{
		var pipeline = Marshal.StringToCoTaskMemUTF8( "surface Opaque Forward" );
		var pass = Marshal.StringToCoTaskMemUTF8( "Opaque Forward" );
		try
		{
			var objects = stackalloc VkDebugUtilsObjectNameInfoEXT[3];
			objects[0] = new VkDebugUtilsObjectNameInfoEXT { objectType = VkObjectType.CommandBuffer };
			objects[1] = new VkDebugUtilsObjectNameInfoEXT { objectType = VkObjectType.Pipeline, pObjectName = (byte*)pipeline };
			objects[2] = new VkDebugUtilsObjectNameInfoEXT { objectType = VkObjectType.Image };
			var labels = stackalloc VkDebugUtilsLabelEXT[1];
			labels[0] = new VkDebugUtilsLabelEXT { pLabelName = (byte*)pass };
			var data = new VkDebugUtilsMessengerCallbackDataEXT { objectCount = 3, pObjects = objects, cmdBufLabelCount = 1, pCmdBufLabels = labels };

			Assert.AreEqual( " [Pipeline 'surface Opaque Forward', labels 'Opaque Forward']", Device.DescribeObjects( &data ) );
		}
		finally
		{
			Marshal.FreeCoTaskMem( pipeline );
			Marshal.FreeCoTaskMem( pass );
		}
	}

	[TestMethod]
	public unsafe void NothingNamedAddsNothing()
	{
		var objects = stackalloc VkDebugUtilsObjectNameInfoEXT[1];
		objects[0] = new VkDebugUtilsObjectNameInfoEXT { objectType = VkObjectType.Buffer };
		var data = new VkDebugUtilsMessengerCallbackDataEXT { objectCount = 1, pObjects = objects };

		Assert.AreEqual( "", Device.DescribeObjects( &data ) );
	}

	/// <summary>A long name (a shader's path) keeps its end, where the file is named</summary>
	[TestMethod]
	public void LongNamesKeepTheirEnd()
	{
		var path = @"C:\Users\Someone\Development\worktrees\sbox\a-long-worktree-name\game\core\shaders/complex.gpudriven.shader_c";
		Span<byte> utf8 = stackalloc byte[256];
		var name = System.Text.Encoding.UTF8.GetString( utf8[..Device.ShortName( path, utf8 )] );
		Assert.AreEqual( 80, name.Length );
		Assert.IsTrue( name.StartsWith( "..." ) && name.EndsWith( "shaders/complex.gpudriven.shader_c" ), name );
		Assert.AreEqual( "short", System.Text.Encoding.UTF8.GetString( utf8[..Device.ShortName( "short", utf8 )] ) );
	}
}
