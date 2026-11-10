extern alias After;
extern alias Before;

using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

// ReSharper disable PossibleNullReferenceException

namespace HotloadTests
{
	[TestClass]
	[DoNotParallelize]
	public class SyncVarTest : HotloadTestBase
	{
		[Reset]
		public static object Instance;

		/// <summary>
		/// Adding a [Sync] attribute to a property across a hotload: the live
		/// instance held in a static root must be swapped to the After version
		/// of the type, and querying sync properties on the instance's runtime
		/// type must pick up the added attribute once the type cache is cleared.
		/// </summary>
		[TestMethod]
		public void AddedSyncVar()
		{
			Instance = new Before::TestClass47();

			var properties = ReflectionQueryCache.SyncProperties( Instance.GetType() );
			Assert.AreEqual( 0, properties.Count() );

			Hotload();

			// The instance should have been swapped to the new assembly's version of the type
			Assert.IsInstanceOfType( Instance, typeof( After::TestClass47 ) );

			// Clear the type cache so next time we'll pick up the added [Sync] attribute
			ReflectionQueryCache.ClearTypeCache();

			properties = ReflectionQueryCache.SyncProperties( Instance.GetType() );
			Assert.AreEqual( 1, properties.Count() );
		}
	}
}
