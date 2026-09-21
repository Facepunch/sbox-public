using System;
using Sandbox.Internal;

namespace Sandbox.Tests;

[TestClass]
public class OperatorMetadataTests
{
	[Expose]
	public readonly struct Operand( int value )
	{
		public int Value => value;
		public static Operand operator +( Operand a, Operand b ) => new( a.Value + b.Value );
		public static Operand operator checked +( Operand a, Operand b ) => new( checked(a.Value + b.Value) );
		public static implicit operator int( Operand a ) => a.Value;
		public static Operand operator -( Operand a ) => new( -a.Value );
		// A similarly named ordinary method must not be classified as an operator.
		public static int op_Multiply( int a, int b ) => a * b;
		private static int Hidden() => 0;
	}

	[Expose]
	public sealed class MutableOperand
	{
		public int Value { get; private set; }
		public void operator +=( int amount ) => Value += amount;
	}

	[TestMethod]
	public void InstanceAssignmentOperatorsAreExposed()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( MutableOperand ).Assembly, false );
		var addition = library.GetType( typeof( MutableOperand ) ).Operators.Single();
		Assert.AreEqual( OperatorKind.AdditionAssignment, addition.OperatorKind );
		Assert.IsFalse( addition.IsStatic );
		var instance = new MutableOperand();
		addition.Invoke( instance, [5] );
		Assert.AreEqual( 5, instance.Value );
	}

	[TestMethod]
	public void OperatorsUseNormalMemberMetadataAndInvocation()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( Operand ).Assembly, false );
		var type = library.GetType( typeof( Operand ) );
		Assert.AreEqual( 4, type.Operators.Length );
		var addition = type.Operators.Single( m => m.OperatorKind == OperatorKind.Addition );
		Assert.IsTrue( addition.IsOperator );
		Assert.IsTrue( addition.IsStatic );
		Assert.AreEqual( typeof( Operand ), addition.ReturnType );
		Assert.AreEqual( 2, addition.Parameters.Length );
		Assert.AreEqual( 7, ((Operand)addition.Invoke( null, [new Operand( 3 ), new Operand( 4 )] )).Value );
		Assert.AreSame( addition, library.GetMemberByIdent( addition.Identity ) );
		Assert.IsTrue( type.Methods.Contains( addition ) && type.Members.Contains( addition ) && type.DeclaredMembers.Contains( addition ) );
		Assert.IsTrue( type.Operators.Any( m => m.OperatorKind == OperatorKind.CheckedAddition ) );
		Assert.IsTrue( type.Operators.Any( m => m.OperatorKind == OperatorKind.ImplicitConversion ) );
		Assert.IsFalse( type.Methods.Single( m => m.Name == "op_Multiply" ).IsOperator );
		Assert.IsFalse( type.Methods.Any( m => m.Name is "get_Value" or "Hidden" ) );
	}

	[TestMethod]
	public void OperatorDescriptionsAreReusedWhenAnAssemblyIsReloaded()
	{
		var library = new TypeLibrary();
		var assembly = typeof( Operand ).Assembly;
		library.AddAssembly( assembly, false );
		var type = library.GetType( typeof( Operand ) );
		var addition = type.Operators.Single( m => m.OperatorKind == OperatorKind.Addition );
		library.RemoveAssembly( assembly );
		Assert.IsFalse( type.IsValid );
		library.AddAssembly( assembly, false );
		Assert.AreSame( type, library.GetType( typeof( Operand ) ) );
		Assert.AreSame( addition, type.Operators.Single( m => m.OperatorKind == OperatorKind.Addition ) );
		Assert.AreEqual( 5, ((Operand)addition.Invoke( null, [new Operand( 2 ), new Operand( 3 )] )).Value );
	}

	[TestMethod]
	public void SystemTypeExposurePolicyStillAppliesToOperators()
	{
		var library = new TypeLibrary();
		library.AddIntrinsicTypes();
		Assert.IsNotNull( library.GetType( typeof( int ) ) );
		Assert.IsNull( library.GetType( typeof( decimal ) ) );
		Assert.IsFalse( library.GetType( typeof( string ) ).Methods.Any( m => m.Name.StartsWith( "get_", StringComparison.Ordinal ) ) );
	}
}
