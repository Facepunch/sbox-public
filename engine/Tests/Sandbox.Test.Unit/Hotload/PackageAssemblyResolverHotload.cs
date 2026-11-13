extern alias Before;

using System;
using CompilingTests.CodeAccess;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Sandbox;

namespace HotloadTests
{
	[TestClass]
	[DoNotParallelize]
	public class PackageAssemblyResolverHotloadTest : HotloadTestBase
	{
		/// <summary>
		/// Stands in for <c>PackageManager.AssemblyResolver</c>: a static the hotload walks while it resolves through it.
		/// </summary>
		[Reset]
		private static PackageAssemblyResolver Resolver;

		/// <summary>
		/// A package assembly added before a hotload is only parsed when the hotload first asks for it. That has to
		/// work mid-swap, while the hotload is also walking the resolver's own fields.
		/// </summary>
		[TestMethod]
		public void QueuedPackageAssembly_ResolvesDuringHotload()
		{
			const string gameName = "package.hotload.game";
			Resolver = new PackageAssemblyResolver();
			Resolver.AddAssembly( PackageAssemblyResolverTest.Compile( gameName, "public sealed class Game { }" ), gameName );

			// A lambda from the swapped assembly makes the hotload read its Cecil definitions
			Before::TestClass1.Instance = new Before::TestClass1();
			Before::TestClass1.Instance.ActionSimple = Before::TestClass1.Instance.CreateAction1( 50 );

			var hotload = CreateHotload();
			var resolver = new ResolveAlongside( hotload.AssemblyResolver, () => Resolver.Resolve( new AssemblyNameReference( gameName, new Version( 0, 0, 0, 0 ) ) ) );
			hotload.AssemblyResolver = resolver;

			var result = hotload.UpdateReferences();

			Assert.IsTrue( resolver.Calls > 0, "Hotload never resolved anything, so this tests nothing" );
			Assert.IsFalse( result.HasErrors, string.Join( "\n", result.Entries ) );
			Resolver.Dispose();
		}

		/// <summary>
		/// Resolves the package assembly every time the hotload resolves anything, like a game's lambdas do.
		/// </summary>
		private sealed class ResolveAlongside( IAssemblyResolver inner, Action alongside ) : IAssemblyResolver
		{
			public int Calls { get; private set; }

			public AssemblyDefinition Resolve( AssemblyNameReference name )
			{
				Calls++;
				alongside();
				return inner.Resolve( name );
			}

			public AssemblyDefinition Resolve( AssemblyNameReference name, ReaderParameters parameters ) => Resolve( name );

			public void Dispose() => inner.Dispose();
		}
	}
}
