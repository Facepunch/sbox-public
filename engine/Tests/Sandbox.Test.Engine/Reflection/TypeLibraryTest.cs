using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using static ReflectionTests.TypeLibraryTest;

namespace ReflectionTests;

[TestClass]
public class TypeLibraryTest
{
	System.Reflection.Assembly ThisAssembly => this.GetType().Assembly;

	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void EnrollingDynamic()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		Assert.AreNotEqual( 0, tl.Types.Count() );

		{
			var gc = tl.Create( "GarrysClass", typeof( object ) );
			Assert.IsNotNull( gc );
		}

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );
	}

	[TestMethod]
	public void EnrollingStatic()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, false );

		Assert.IsTrue( tl.Types.Count() > 0 );

		{
			var gc = tl.Create( "GarrysClass", typeof( object ) );
			Assert.IsNotNull( gc );
		}

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );
	}

	[TestMethod]
	public void Attribute()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, false );
		Assert.IsTrue( tl.Types.Count() > 0 );

		MyTypeAttribute attr;

		{
			var types = tl.GetAttributes<MyTypeAttribute>();
			Assert.AreEqual( 1, types.Count() );
			foreach ( var t in types )
			{
				Assert.IsNotNull( t );
				Assert.IsNotNull( t.TargetType );

			}

			attr = types.First();
			Assert.AreEqual( true, attr.Registered );
			Assert.AreEqual( false, attr.Unregistered );
		}

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );

		Assert.AreEqual( true, attr.Registered );
		Assert.AreEqual( true, attr.Unregistered );
	}

	public class MyTypeAttribute : System.Attribute, Sandbox.ITypeAttribute
	{
		public bool Registered;
		public bool Unregistered;

		public System.Type TargetType { get; set; }

		public void TypeRegister()
		{
			Registered = true;
		}

		public void TypeUnregister()
		{
			Unregistered = true;
		}
	}

	[Expose]
	public class TypeLibraryStreamReader : StreamReader
	{
		public TypeLibraryStreamReader( Stream stream ) : base( stream ) { }
	}

	[Expose]
	public delegate void MyDelegateType( Type x );

	[TestMethod]
	public void DontAllowDelegates()
	{
		var tl = new Sandbox.Internal.TypeLibrary();

		tl.AddAssembly( ThisAssembly, false );
		tl.AddAssembly( typeof( Sandbox.Internal.GlobalGameNamespace ).Assembly, false );

		Assert.IsNull( tl.GetType( "MyDelegateType" ) );
	}


	[TestMethod]
	public void ReusesRemovedType()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		Assert.AreNotEqual( 0, tl.Types.Count() );

		var typeA = tl.GetType( "GarrysClass" );
		Assert.IsNotNull( typeA );

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );

		var typeB = tl.GetType( "GarrysClass" );
		Assert.IsNull( typeB );

		tl.AddAssembly( ThisAssembly, true );
		var typeC = tl.GetType( "GarrysClass" );
		Assert.IsNotNull( typeC );
		Assert.AreEqual( typeA, typeC, "Should have re-populated the old type" );
	}

	[TestMethod]
	public void ReusesRemovedProperty()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		Assert.AreNotEqual( 0, tl.Types.Count() );

		var typeA = tl.GetType( "GarrysClass" );
		Assert.IsNotNull( typeA );
		var propA = typeA.GetProperty( "Value" );
		Assert.IsNotNull( propA );

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );

		var typeB = tl.GetType( "GarrysClass" );
		Assert.IsNull( typeB );

		tl.AddAssembly( ThisAssembly, true );
		var typeC = tl.GetType( "GarrysClass" );
		Assert.IsNotNull( typeC );

		var propC = typeC.GetProperty( "Value" );
		Assert.IsNotNull( propC );

		Assert.AreEqual( typeA, typeC, "Should have re-populated the old type" );
		Assert.AreEqual( propA, propC, "Should have re-populated the old type" );
	}

	[TestMethod]
	public void ReAddedTypeIsSame()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		Assert.AreNotEqual( 0, tl.Types.Count() );

		var typeA = tl.GetType( "GarrysClass" );
		Assert.IsNotNull( typeA );

		var interfaceCount = typeA.Interfaces.Count();
		var attributeCount = typeA.Attributes.Count();

		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );

		var typeB = tl.GetType( "GarrysClass" );
		Assert.IsNull( typeB );

		tl.AddAssembly( ThisAssembly, true );
		var typeC = tl.GetType( "GarrysClass" );
		Assert.IsNotNull( typeC );
		Assert.AreEqual( typeA, typeC, "Should have re-populated the old type" );
		Assert.AreEqual( interfaceCount, typeC.Interfaces.Count() );
		Assert.AreEqual( attributeCount, typeC.Attributes.Count() );
	}

	/// <summary>
	/// <see cref="GlobalContext.Current.DisableTypelibraryScope"/> must disable access to <see cref="Game.TypeLibrary"/>.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void DisableScope()
	{
		const string reason = "!Testing!";

		Game.TypeLibrary.GetType<GarrysClass>();

		using ( GlobalContext.Current.DisableTypelibraryScope( reason ) )
		{
			var exception = Assert.ThrowsException<InvalidOperationException>( () => Game.TypeLibrary.GetType<GarrysClass>() );

			Assert.IsTrue( exception.Message.Contains( reason ) );
		}

		Game.TypeLibrary.GetType<GarrysClass>();
	}

	/// <summary>
	/// Nested <see cref="TypeLibrary.DisableScope"/>s must disable access to <see cref="Game.TypeLibrary"/>.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void NestedDisableScope()
	{
		const string reason1 = "!Testing 1!";
		const string reason2 = "!Testing 2!";

		Game.TypeLibrary.GetType<GarrysClass>();

		using ( GlobalContext.Current.DisableTypelibraryScope( reason1 ) )
		{
			var exception1 = Assert.ThrowsException<InvalidOperationException>( () => Game.TypeLibrary.GetType<GarrysClass>() );

			Assert.IsTrue( exception1.Message.Contains( reason1 ) );

			using ( GlobalContext.Current.DisableTypelibraryScope( reason2 ) )
			{
				var exception2 = Assert.ThrowsException<InvalidOperationException>( () => Game.TypeLibrary.GetType<GarrysClass>() );

				Assert.IsTrue( exception2.Message.Contains( reason2 ) );
			}

			var exception3 = Assert.ThrowsException<InvalidOperationException>( () => Game.TypeLibrary.GetType<GarrysClass>() );

			Assert.IsTrue( exception3.Message.Contains( reason1 ) );
		}

		Game.TypeLibrary.GetType<GarrysClass>();
	}

	public interface IConstraintBreaker<T>
	{
		void DoSomething( T value );
	}



	/// <summary>
	/// 
	/// </summary>
	[TestMethod]
	public void ObeyGenericConstraints()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );
		tl.AddAssembly( typeof( Vector3 ).Assembly, false );

		var goodboy = tl.GetType( typeof( ConstraintBreaker<> ) )
									.CreateGeneric<IConstraintBreaker<Vector3>>( [typeof( Vector3 )] );
		Assert.IsNotNull( goodboy );

		var badboy = tl.GetType( typeof( ConstraintBreaker<> ) )
									.CreateGeneric<IConstraintBreaker<TypeWrapper>>( [typeof( TypeWrapper )] );
		Assert.IsNull( badboy );
	}

	const int MarkedTypes = 32;

	/// <summary>
	/// When one type throws while registering, the types that did register must still leave with the
	/// assembly. Otherwise they survive the hotload next to the incoming versions, and the editor
	/// lists every component and tool twice.
	/// </summary>
	[TestMethod]
	public void TypesFromFailedRegistrationAreRemovedWithTheAssembly()
	{
		RegistrationMarkerAttribute.Seen.Clear();

		var assembly = EmitPartiallyFailingAssembly();
		var tl = new Sandbox.Internal.TypeLibrary();

		var exception = Assert.ThrowsException<AggregateException>( () => tl.AddAssembly( assembly, true ) );
		Assert.IsInstanceOfType<InvalidOperationException>( exception.GetBaseException() );

		var registered = tl.GetTypes( null ).Where( x => x.TargetType.Assembly == assembly ).Select( x => x.TargetType ).ToArray();
		Assert.AreNotEqual( 0, registered.Length, "expected the types that didn't throw to register" );
		Assert.IsTrue( registered.All( tl.Types.Contains ), "registered types must be tracked in Types" );

		tl.RemoveAssembly( assembly );

		Assert.IsFalse( tl.GetTypes( null ).Any( x => x.TargetType.Assembly == assembly ) );
		Assert.IsFalse( tl.Types.Any( x => x.Assembly == assembly ) );
	}

	/// <summary>
	/// Many marked types and one whose registration throws - but only after every marked type has
	/// started registering, so some always succeed first.
	/// </summary>
	static Assembly EmitPartiallyFailingAssembly()
	{
		var builder = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( $"PartialRegistration{Guid.NewGuid():N}" ), AssemblyBuilderAccess.Run );
		var module = builder.DefineDynamicModule( "Module" );

		var marker = typeof( RegistrationMarkerAttribute ).GetConstructor( [typeof( int )] );
		var failing = typeof( FailsRegistrationAttribute ).GetConstructor( Type.EmptyTypes );

		for ( int i = 0; i < MarkedTypes; i++ )
		{
			var type = module.DefineType( $"Marked{i}", TypeAttributes.Public | TypeAttributes.Class );
			type.SetCustomAttribute( new CustomAttributeBuilder( marker, [i] ) );
			type.CreateType();
		}

		var failingType = module.DefineType( "Failing", TypeAttributes.Public | TypeAttributes.Class );
		failingType.SetCustomAttribute( new CustomAttributeBuilder( failing, [] ) );

		// The created types report the runtime assembly, not the builder
		return failingType.CreateType().Assembly;
	}

	[AttributeUsage( AttributeTargets.Class )]
	public sealed class RegistrationMarkerAttribute : Attribute
	{
		public static readonly ConcurrentDictionary<int, bool> Seen = new();

		public RegistrationMarkerAttribute( int index ) => Seen[index] = true;
	}

	[AttributeUsage( AttributeTargets.Class )]
	public sealed class FailsRegistrationAttribute : Attribute
	{
		public FailsRegistrationAttribute()
		{
			SpinWait.SpinUntil( () => RegistrationMarkerAttribute.Seen.Count >= MarkedTypes, TimeSpan.FromSeconds( 10 ) );
			throw new InvalidOperationException( "Registration failed" );
		}
	}
}

[Expose, MyType]
public class GarrysClass
{
	public string Value { get; set; }
}

[Expose]
public readonly record struct TypeWrapper( Type Value );

[Expose]
public class ConstraintBreaker<T> : IConstraintBreaker<T> where T : unmanaged
{
	public void DoSomething( T value )
	{

	}
}
