using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Internal;

namespace Sandbox.Tests;

/// <summary>
/// Verifies that the scripting catalog excludes members whose references can escape call temporaries.
/// </summary>
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

	/// <summary>
	/// Supplies protected component APIs with both supported scalar parameters and excluded ref-like signatures.
	/// </summary>
	[Expose]
	public class RefComponent : Component
	{
		protected int Read( int value ) => value;
		protected int ReadIn( in int value ) => value;
		protected void WriteRef( ref int value ) => value++;
		protected ref int Echo( int[] values ) => ref values[0];
		protected Span<int> Wrap( int[] values ) => values;
		protected int Consume( Span<int> values ) => values.Length;
		protected int ConsumeReadOnly( ReadOnlySpan<int> values ) => values.Length;
		protected Span<int> Buffer => default;
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

	/// <summary>
	/// Derived scripts retain ordinary protected APIs while applying the same reference-safety policy as public calls.
	/// </summary>
	[TestMethod]
	public void ProtectedComponentMembersUseTheSameReferenceSafetyRules()
	{
		var members = new List<MemberInfo>();
		new TypeLibraryScriptResolver( Library.Value ).GetBaseMembers( typeof( RefComponent ), members );
		var declared = members.Where( member => member.DeclaringType == typeof( RefComponent ) ).ToArray();

		Assert.IsTrue( declared.OfType<ConstructorInfo>().Any() );
		CollectionAssert.AreEquivalent( new[] { "Read", "ReadIn", "WriteRef" }, declared.OfType<MethodInfo>().Select( method => method.Name ).ToArray() );
		Assert.IsFalse( declared.OfType<PropertyInfo>().Any() );
	}
}
