using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using static HotloadTests.MemberEqualityTest;

#pragma warning disable CS0067

namespace HotloadTests
{
	[TestClass]
	[DoNotParallelize]
	public class MemberEqualityTest
	{
		public class SomeAttribute : Attribute
		{
			public float ConstructorArg { get; }
			public string[] VarArg { get; }
			public int NamedArg { get; set; }

			public SomeAttribute( float constructorArg, params string[] varArg )
			{
				ConstructorArg = constructorArg;
				VarArg = varArg;
			}
		}

		public class ControlGroupA
		{
			public int SomeInt;
			[SomeAttribute( 123.456f, "Hello", "World", NamedArg = -82 )]
			public float SomeFloat;
			public Func<int, bool[]> SomeFunc;
			public event Action<string> SomeEvent;
		}

		public class ControlGroupB
		{
			public int SomeInt;
			[SomeAttribute( 123.456f, "Hello", "World", NamedArg = -82 )]
			public float SomeFloat;
			public Func<int, bool[]> SomeFunc;
			public event Action<string> SomeEvent;
		}

		[TestMethod]
		public void ControlGroup()
		{
			var comparer = new MemberEqualityComparer();
			Assert.IsTrue( comparer.AllMembersEqual( typeof( ControlGroupA ), typeof( ControlGroupB ) ) );
		}

		public class SwappedMembersA
		{
			public int SomeInt;
			public float SomeFloat;
		}

		public class SwappedMembersB
		{
			public float SomeFloat;
			public int SomeInt;
		}

		[TestMethod]
		public void SwappedMembers()
		{
			var comparer = new MemberEqualityComparer();
			Assert.IsFalse( comparer.AllMembersEqual( typeof( SwappedMembersA ), typeof( SwappedMembersB ) ) );
		}

		public class BaseType<T>
		{
			public T Value;
		}

		public class DifferentBaseTypeA : BaseType<int>
		{
			public int SomeInt;
			public float SomeFloat;
		}

		public class DifferentBaseTypeB : BaseType<float>
		{
			public int SomeInt;
			public float SomeFloat;
		}

		[TestMethod]
		public void DifferentBaseType()
		{
			var comparer = new MemberEqualityComparer();
			Assert.IsFalse( comparer.AllMembersEqual( typeof( DifferentBaseTypeA ), typeof( DifferentBaseTypeB ) ) );
		}

		[StructLayout( LayoutKind.Explicit )]
		public struct StructLayoutA
		{
			[FieldOffset( 0 )]
			public int A;

			[FieldOffset( 4 )]
			public float B;
		}

		[StructLayout( LayoutKind.Explicit )]
		public struct StructLayoutB
		{
			[FieldOffset( 4 )]
			public int A;

			[FieldOffset( 0 )]
			public float B;
		}

		[TestMethod]
		public void StructLayout()
		{
			var comparer = new MemberEqualityComparer();
			Assert.IsFalse( comparer.AllMembersEqual( typeof( StructLayoutA ), typeof( StructLayoutB ) ) );
		}
	}
}
