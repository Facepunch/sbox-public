namespace ReflectionTests;

[TestClass]
public class InheritedMembersTest
{
	/// <summary>
	/// A derived type's description of an inherited member holds the member reflected through the derived type, the one
	/// reflection gives for it (script permissions compare members by equality, so a native-base hotload depends on it),
	/// while the attributes stay shared with the base type's description.
	/// </summary>
	[TestMethod]
	public void InheritedMembersAreReflectedThroughTheDerivedType()
	{
		var assembly = GetType().Assembly;
		var tl = new Sandbox.Internal.TypeLibrary();
		tl.AddAssembly( assembly, false );

		try
		{
			var baseType = tl.GetType<InheritedMembersBase>();
			var derived = tl.GetType<InheritedMembersDerived>();

			var property = derived.GetProperty( nameof( InheritedMembersBase.BaseValue ) );
			Assert.AreEqual( typeof( InheritedMembersDerived ).GetProperty( nameof( InheritedMembersBase.BaseValue ) ), property.MemberInfo );
			Assert.AreSame( typeof( InheritedMembersDerived ), property.MemberInfo.ReflectedType );
			Assert.AreSame( baseType.GetProperty( nameof( InheritedMembersBase.BaseValue ) ).Attributes, property.Attributes );

			var method = derived.GetMethod( nameof( InheritedMembersBase.BaseMethod ) );
			Assert.AreEqual( typeof( InheritedMembersDerived ).GetMethod( nameof( InheritedMembersBase.BaseMethod ) ), method.MemberInfo );
			Assert.AreSame( baseType.GetMethod( nameof( InheritedMembersBase.BaseMethod ) ).Attributes, method.Attributes );

			var field = derived.Fields.Single( x => x.Name == nameof( InheritedMembersBase.BaseField ) );
			Assert.AreEqual( typeof( InheritedMembersDerived ).GetField( nameof( InheritedMembersBase.BaseField ) ), field.MemberInfo );
			Assert.AreSame( baseType.Fields.Single( x => x.Name == nameof( InheritedMembersBase.BaseField ) ).Attributes, field.Attributes );
		}
		finally
		{
			tl.RemoveAssembly( assembly );
		}
	}
}

[Expose]
public class InheritedMembersBase
{
	[Title( "Base value" )]
	public int BaseValue { get; set; }

	[Title( "Base field" )]
	public int BaseField;

	[Title( "Base method" )]
	public int BaseMethod() => BaseValue;
}

[Expose]
public class InheritedMembersDerived : InheritedMembersBase
{
	public int DerivedValue { get; set; }
}
