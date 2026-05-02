using System;
using System.IO;
using Editor;

namespace SystemTest;

[TestClass]
public class ProjectEditorSessionLockTests
{
	[TestMethod]
	public void MutexNameForPath_IsStable_ForSameInput()
	{
		var path = Path.GetFullPath( Path.Combine( Path.GetTempPath(), "mutex_test_project", "game.sbproj" ) );
		var a = ProjectEditorSessionLock.MutexNameForPath( path );
		var b = ProjectEditorSessionLock.MutexNameForPath( path );
		Assert.AreEqual( a, b );
	}

	[TestMethod]
	public void MutexNameForPath_Differs_ForDifferentPaths()
	{
		var a = ProjectEditorSessionLock.MutexNameForPath( @"C:\Temp\A\game.sbproj" );
		var b = ProjectEditorSessionLock.MutexNameForPath( @"C:\Temp\B\game.sbproj" );
		Assert.AreNotEqual( a, b );
	}

	[TestMethod]
	public void NormalizeProjectConfigPath_AppendsSbproj_ForDirectory()
	{
		var root = Path.Combine( Path.GetTempPath(), "norm_test_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		try
		{
			var expected = Path.GetFullPath( Path.Combine( root, ".sbproj" ) );
			var actual = ProjectEditorSessionLock.NormalizeProjectConfigPath( root );
			Assert.AreEqual( expected, actual );
		}
		finally
		{
			try { Directory.Delete( root, true ); } catch { }
		}
	}
}
