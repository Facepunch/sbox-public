using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using static Reflection.TypeCollection;

namespace Reflection;

[TestClass]
public class TypeCollection
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

	//[TestMethod]
	public void Fill_From_Addon()
	{
		var baseLibrary = Assembly.LoadFrom( $"{System.Environment.CurrentDirectory}/unittest/package.base.dll" );
		var addonLibrary = Assembly.LoadFrom( $"{System.Environment.CurrentDirectory}/unittest/package.facepunch.sandbox.dll" );

		var sw = Stopwatch.StartNew();

		//for ( int i = 0; i < 100; i++ )
		{
			using ( var gr = new HeavyGarbageRegion() )
			{
				var tl = new Sandbox.Internal.TypeLibrary();
				tl.AddAssembly( baseLibrary, true );
				tl.AddAssembly( addonLibrary, true );
				tl.Dispose();
			}
		}

		Console.WriteLine( $"Took {sw.Elapsed.TotalMilliseconds:0.00}ms" );
	}

	//[TestMethod]
	public void MemberAccess()
	{
		var tl = new Sandbox.Internal.TypeLibrary();

		tl.AddIntrinsicTypes();

		foreach ( var type in tl.GetTypes().OrderBy( x => x.FullName ) )
		{
			Console.WriteLine( type );

			foreach ( var member in type.Members.OrderBy( x => x.Name ) )
			{
				Console.WriteLine( $"  {member}" );
			}
		}

		tl.AddAssembly( ThisAssembly, false );
		tl.AddAssembly( typeof( Sandbox.Http ).Assembly, false ); // Sandbox.Game

		{
			var type = tl.GetType<TypeLibraryStreamReader>();
			Assert.IsNull( type.GetMethod( "DiscardBufferedData" ) ); // system public
		}

		{
			var type = tl.GetType<object>();
			Assert.IsNotNull( type.GetMethod( "ToString" ) );
		}

		{
			var type = tl.GetType<FileInfo>();
			Assert.IsNull( type );
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
	public void AssemblyGetsRemoved()
	{
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( ThisAssembly, true );

		Assert.AreNotEqual( 0, tl.Types.Count() );
		tl.RemoveAssembly( ThisAssembly );
		Assert.AreEqual( 0, tl.Types.Count() );
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
