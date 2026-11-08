using Sandbox.Internal;

namespace ReflectionTests;

[TestClass]
[DoNotParallelize]
public class MemberWriteAccessTests
{
	[TestMethod]
	public void InheritedEngineSettersStayClosedOnDynamicTypes()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( InheritedSetterProbe ).Assembly, true );

		var type = library.GetType( typeof( InheritedSetterProbe ) );
		var probe = new InheritedSetterProbe();

		type.Properties.Single( x => x.Name == nameof( LibraryAttribute.Name ) ).SetValue( probe, "changed" );
		type.Properties.Single( x => x.Name == nameof( InheritedSetterProbe.Own ) ).SetValue( probe, "changed" );

		Assert.IsNull( probe.Name );
		Assert.AreEqual( "changed", probe.Own );
	}

	[TestMethod]
	public void ReadOnlyFieldsOnNonDynamicTypesCannotBeWritten()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( ReadOnlyFieldOwner ).Assembly, false );

		var owner = new ReadOnlyFieldOwner();
		var field = library.GetType( typeof( ReadOnlyFieldOwner ) ).Fields.Single( x => x.Name == nameof( ReadOnlyFieldOwner.Value ) );

		field.SetValue( owner, 2 );

		Assert.AreEqual( 1, owner.Value );
	}
}

public sealed class InheritedSetterProbe : LibraryAttribute
{
	public string Own { get; private set; }
}

[Expose]
public class ReadOnlyFieldOwner
{
	public readonly int Value = 1;
}
