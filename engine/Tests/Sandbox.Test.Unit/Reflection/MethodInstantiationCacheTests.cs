using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ReflectionTests;

[TestClass]
[DoNotParallelize]
public class MethodInstantiationCacheTests
{
	public class InstantiationBase
	{
		public T Get<T>() => default;
	}

	/// <summary>
	/// A type in a collectible assembly deriving from an engine type, as a game component derives from Component.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	static Type CollectibleDerivedType()
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "InstantiationCacheGame" ), AssemblyBuilderAccess.RunAndCollect );
		var module = assembly.DefineDynamicModule( "InstantiationCacheGame" );
		var type = module.DefineType( "GameComponent", TypeAttributes.Public | TypeAttributes.Class, typeof( InstantiationBase ) );
		type.DefineDefaultConstructor( MethodAttributes.Public );
		return type.CreateType();
	}

	/// <summary>
	/// A generic engine method instantiated over engine types but looked up through a game type is kept in the runtime's
	/// static instantiation cache, holding the game type (so its assembly can't unload) until the cache is emptied. Emptying
	/// it lets go of them; looking the method up again still works and compares equal to an earlier instantiation.
	/// </summary>
	[TestMethod]
	public void InstantiationsReflectedThroughCollectibleTypesAreForgotten()
	{
		var game = CollectibleDerivedType();
		Assert.IsTrue( game.Assembly.IsCollectible );

		var method = game.GetMethod( nameof( InstantiationBase.Get ) ).MakeGenericMethod( typeof( bool ) );
		Assert.AreEqual( game, method.ReflectedType );
		Assert.IsTrue( Sandbox.ReflectionUtility.MethodInstantiations().Any( m => m.ReflectedType == game ), "the runtime cached it with the game type" );

		Assert.IsTrue( Sandbox.ReflectionUtility.ForgetCollectibleMethodInstantiations() );
		Assert.IsFalse( Sandbox.ReflectionUtility.MethodInstantiations().Any( Sandbox.ReflectionUtility.TiedToCollectible ), "nothing tied to a collectible assembly is left" );
		Assert.IsFalse( Sandbox.ReflectionUtility.ForgetCollectibleMethodInstantiations(), "nothing more to forget" );

		var again = game.GetMethod( nameof( InstantiationBase.Get ) ).MakeGenericMethod( typeof( bool ) );
		Assert.AreEqual( method, again );
		Assert.IsFalse( (bool)again.Invoke( Activator.CreateInstance( game ), null ) );
	}
}
