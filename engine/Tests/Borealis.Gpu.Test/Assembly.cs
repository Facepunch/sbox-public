global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using Borealis.Gpu;
using Borealis.Gpu.Vulkan;

// Tests that change renderer globals, use hardware counters, or rely on a process-wide setting run serially.
// Bound concurrency to limit the number of Vulkan devices and validation state alive at once.
[assembly: Parallelize( Workers = 4, Scope = ExecutionScope.MethodLevel )]

namespace BorealisGpuTests;

[TestClass]
public class GpuTestAssembly
{
	[AssemblyInitialize]
	public static void Initialize( TestContext context )
	{
		// Publish the loader's global entry points before any parallel tests open devices.
		if ( Device.IsSupported ) VkGlobal.Load();
	}
}
