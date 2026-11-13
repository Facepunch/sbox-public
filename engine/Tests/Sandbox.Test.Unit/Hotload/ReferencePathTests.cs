extern alias Before;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using System.Reflection;

namespace HotloadTests;

[TestClass]
[DoNotParallelize]
public class ReferencePathTests : HotloadTestBase
{
	[Reset]
	public static object Root;

	private static FieldInfo RootField => typeof( ReferencePathTests ).GetField( nameof( Root ), BindingFlags.Public | BindingFlags.Static );

	/// <summary>
	/// Paths must not outlive the hotload that walked them: a kept tree pinned unloaded assemblies.
	/// </summary>
	[TestMethod]
	public void PathsAreDroppedAfterHotload()
	{
		Root = new Before::TestClass1();

		var path = Sandbox.Hotload.ReferencePath.GetRoot( RootField );

		// The hotload walks Root, so it reuses this path until it clears them
		Hotload();

		Assert.AreNotSame( path, Sandbox.Hotload.ReferencePath.GetRoot( RootField ), "The reference paths were kept after the hotload" );
	}
}
