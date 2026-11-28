using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SystemTests;

[TestClass]
public class ManagedSizeCacheTest
{
	/// <summary>
	/// Sizing a type from an unloadable assembly must not keep that assembly alive. The hotloader sizes outgoing types,
	/// and a strong cache key pinned every unloaded game version for the rest of the session.
	/// </summary>
	[TestMethod]
	public void CollectibleTypeIsNotPinned()
	{
		var type = SizeCollectibleStruct( out var size );

		Assert.AreEqual( 3 * sizeof( int ), size );

		for ( var i = 0; type.IsAlive && i < 10; i++ )
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		Assert.IsFalse( type.IsAlive, "The cached size kept the collectible type, and its assembly, alive" );
	}

	/// <summary>
	/// Builds and sizes the type in its own frame, so nothing on the test's stack references it afterwards.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static WeakReference SizeCollectibleStruct( out int size )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "ManagedSizeCollectible" ), AssemblyBuilderAccess.RunAndCollect );
		var builder = assembly.DefineDynamicModule( "Main" ).DefineType( "Probe",
			TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof( ValueType ) );

		builder.DefineField( "A", typeof( int ), FieldAttributes.Public );
		builder.DefineField( "B", typeof( int ), FieldAttributes.Public );
		builder.DefineField( "C", typeof( int ), FieldAttributes.Public );

		var type = builder.CreateType();
		Assert.IsTrue( type.IsCollectible );

		size = type.GetManagedSize();
		return new WeakReference( type );
	}
}
