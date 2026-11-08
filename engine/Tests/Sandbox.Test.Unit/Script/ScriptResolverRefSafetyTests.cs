using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Internal;

namespace Sandbox.Tests;

[TestClass]
public class ScriptResolverRefSafetyTests
{
	[Expose]
	public ref struct RefHolder
	{
		public ref readonly int Value;

		public RefHolder( in int value ) { Value = ref value; }
		public RefHolder( int[] values ) { Value = ref values[0]; }
	}

	[Expose]
	public sealed class RefMembers
	{
		public int Plain;

		public RefMembers() { }
		public RefMembers( in int value ) { Plain = value; }

		public int Read( int value ) => value;
		public int ReadIn( in int value ) => value;
		public ref int Echo( int[] values ) => ref values[0];
		public Span<int> Wrap( int[] values ) => values;
	}

	static readonly Lazy<TypeLibrary> Library = new( () =>
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( RefMembers ).Assembly, false );
		return library;
	} );

	static List<MemberInfo> GetMembers( Type type )
	{
		var members = new List<MemberInfo>();
		new TypeLibraryScriptResolver( Library.Value ).GetMembers( type, members );
		return members;
	}

	[TestMethod]
	public void RefStructConstructorsAreNotExposed()
	{
		var members = GetMembers( typeof( RefHolder ) );

		Assert.IsFalse( members.OfType<ConstructorInfo>().Any() );
	}

	[TestMethod]
	public void ByRefReturnsAndRefStructsAreNotExposed()
	{
		var members = GetMembers( typeof( RefMembers ) );
		var constructors = members.OfType<ConstructorInfo>().ToArray();
		var methods = members.OfType<MethodInfo>().Select( x => x.Name ).ToArray();

		Assert.AreEqual( 2, constructors.Length );

		CollectionAssert.Contains( methods, nameof( RefMembers.Read ) );
		CollectionAssert.Contains( methods, nameof( RefMembers.ReadIn ) );
		CollectionAssert.DoesNotContain( methods, nameof( RefMembers.Echo ) );
		CollectionAssert.DoesNotContain( methods, nameof( RefMembers.Wrap ) );
		Assert.IsTrue( members.OfType<FieldInfo>().Any( x => x.Name == nameof( RefMembers.Plain ) ) );
	}
}
