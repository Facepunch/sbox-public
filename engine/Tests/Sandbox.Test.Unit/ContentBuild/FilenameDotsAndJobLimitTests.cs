using System;
using System.Collections.Generic;
using Editor;
using Sandbox.ContentBuild;

namespace ContentBuild;

[TestClass]
public class FilenameDotsAndJobLimitTests
{
	[TestMethod]
	public void DefaultMaxJobsScalesWithCoresAndMemory()
	{
		var expected = (int)Math.Clamp( (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes - (2L << 30)) / (1L << 30), 1, Environment.ProcessorCount );
		Assert.AreEqual( expected, ResourceCompiler.DefaultMaxJobs );
		Assert.IsTrue( ResourceCompiler.DefaultMaxJobs >= 1 );
		Assert.IsTrue( ResourceCompiler.DefaultMaxJobs <= Environment.ProcessorCount );
	}

	[TestMethod]
	public void MaterialGeneratorMatchesSlotNamesWithDots()
	{
		var dotSet = new MaterialGenerator.TextureSet
		{
			Directory = "materials",
			BaseName = "mymaterial.001",
			BySuffix = new Dictionary<string, Asset>( StringComparer.OrdinalIgnoreCase ),
		};

		var plainSet = new MaterialGenerator.TextureSet
		{
			Directory = "materials",
			BaseName = "mymaterial",
			BySuffix = new Dictionary<string, Asset>( StringComparer.OrdinalIgnoreCase ),
		};

		var sets = new List<MaterialGenerator.TextureSet> { plainSet, dotSet };

		Assert.AreSame( dotSet, MaterialGenerator.FindSetForSlot( sets, "mymaterial.001" ) );
		Assert.AreSame( dotSet, MaterialGenerator.FindSetForSlot( sets, "materials/mymaterial.001.vmat" ) );
		Assert.AreSame( dotSet, MaterialGenerator.FindSetForSlot( sets, "materials/mymaterial.001.vmat_c" ) );
		Assert.AreSame( plainSet, MaterialGenerator.FindSetForSlot( sets, "materials/mymaterial.vmat" ) );
	}
}
