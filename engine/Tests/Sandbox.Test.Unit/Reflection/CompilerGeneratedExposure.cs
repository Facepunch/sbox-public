using Sandbox.Internal;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ReflectionTests;

[TestClass]
[DoNotParallelize]
public class CompilerGeneratedExposureTests
{
	[TestMethod]
	public void CompilerGeneratedNestedTypesAreNotExposed()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( LambdaCacheOwner ).Assembly, false );

		Assert.IsNotNull( library.GetType( typeof( LambdaCacheOwner ) ) );

		var generated = typeof( LambdaCacheOwner )
			.GetNestedTypes( BindingFlags.NonPublic | BindingFlags.Public )
			.Where( x => x.IsDefined( typeof( CompilerGeneratedAttribute ), false ) )
			.ToArray();

		Assert.AreNotEqual( 0, generated.Length );

		foreach ( var type in generated )
		{
			Assert.IsNull( library.GetType( type ), type.FullName );
		}
	}

	[TestMethod]
	public void StaticFieldsOnNonDynamicTypesCannotBeWritten()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( StaticFieldOwner ).Assembly, false );

		StaticFieldOwner.Value = 1;
		var field = library.GetType( typeof( StaticFieldOwner ) ).Fields.Single( x => x.Name == nameof( StaticFieldOwner.Value ) );

		field.SetValue( null, 2 );

		Assert.AreEqual( 1, StaticFieldOwner.Value );
	}
}

[Expose]
public class LambdaCacheOwner
{
	public bool AnyPositive( List<int> values ) => values.Any( x => x > 0 );
}

[Expose]
public static class StaticFieldOwner
{
	public static int Value;
}
