using Sandbox.Internal;
using System.Reflection;

namespace ReflectionTests;

/// <summary>
/// Nested engine types inherit exposure only through public nesting; addons remain fully registered.
/// </summary>
[TestClass]
public class NestedExposureTests
{
	/// <summary>
	/// Checks visibility, explicit opt-ins and registration through multiple nesting levels.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NestedExposureRespectsVisibilityAndExplicitOptIns( bool isDynamic )
	{
		var library = new TypeLibrary();
		try
		{
			library.AddAssembly( typeof( NestedExposureTests ).Assembly, isDynamic );

			Assert.IsNotNull( library.GetType( typeof( Owner ) ) );

			var expectations = new (string Path, bool Exposed)[]
			{
				("PublicChild", true),
				("PublicChild+Grandchild", true),
				("InternalChild", false),
				("InternalChild+Grandchild", false),
				("InternalChild+RegisteredGrandchild", true),
				("PrivateChild", false),
				("ProtectedChild", false),
				("RegisteredPrivateChild", true),
				("RegisteredPrivateChild+PublicChild", true),
				("RegisteredPrivateChild+InternalChild", false),
				("RegisteredInternalChild", true),
				("RegisteredProtectedChild", true),
				("LibraryChild", true)
			};

			foreach ( var (path, exposed) in expectations )
			{
				var type = typeof( Owner );
				foreach ( var name in path.Split( '+' ) )
				{
					type = type.GetNestedType( name, BindingFlags.Public | BindingFlags.NonPublic );
					Assert.IsNotNull( type, path );
				}

				Assert.AreEqual( isDynamic || exposed, library.GetType( type ) is not null, path );
			}
		}
		finally
		{
			library.Dispose();
		}
	}

	[Expose]
	private class Owner
	{
		/// <summary>
		/// Inherits its explicitly exposed owner's registration despite that owner being private.
		/// </summary>
		public class PublicChild
		{
			/// <summary>
			/// Inherits registration through each public nesting level.
			/// </summary>
			public class Grandchild;
		}

		internal class InternalChild
		{
			/// <summary>
			/// Public visibility cannot inherit registration through an unregistered owner.
			/// </summary>
			public class Grandchild;

			/// <summary>
			/// Explicit registration works even when the containing type is unregistered.
			/// </summary>
			[Expose]
			public class RegisteredGrandchild;
		}

		private class PrivateChild;
		protected class ProtectedChild;

		[Expose]
		private class RegisteredPrivateChild
		{
			/// <summary>
			/// Public children inherit an explicitly registered private owner's exposure.
			/// </summary>
			public class PublicChild;

			internal class InternalChild;
		}

		[Expose]
		internal class RegisteredInternalChild;

		[Expose]
		protected class RegisteredProtectedChild;

		[Library]
		private class LibraryChild;
	}
}
