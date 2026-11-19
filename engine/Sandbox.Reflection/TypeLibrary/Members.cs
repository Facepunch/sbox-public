using System.Collections.Immutable;

namespace Sandbox.Internal;

public partial class TypeLibrary
{
	/// <summary>
	/// If set, will be used to determine whether a private member should be exposed.
	/// </summary>
	internal Func<MemberInfo, bool> ShouldExposePrivateMember;

	/// <summary>
	/// Find all methods with given attribute, optionally non static
	/// </summary>
	public IReadOnlyList<(MethodDescription Method, T Attribute)> GetMethodsWithAttribute<T>( bool onlyStatic = true ) where T : Attribute
	{
		return Cached( HashCode.Combine( "GetMethodsWithAttribute", typeof( T ), onlyStatic ), (Library: this, OnlyStatic: onlyStatic),
						static s => s.Library.typedata.Values.SelectMany( x => x.Members )
								.OfType<MethodDescription>()
								.Where( x => x.IsStatic || !s.OnlyStatic )
								.SelectMany( x => x.Attributes.OfType<T>().Select<T, (MethodDescription Method, T Attribute)>( y => new( x, y ) ) )
								.ToImmutableList() );
	}

	/// <summary>
	/// Find all static methods with given name.
	/// </summary>
	public IEnumerable<MethodDescription> FindStaticMethods( string methodName )
	{
		return typedata.Values.SelectMany( x => x.Members ).OfType<MethodDescription>().Where( x => x.IsStatic && x.IsNamed( methodName ) );
	}

	/// <summary>
	/// Find all static methods with given name and given attribute.
	/// </summary>
	public IEnumerable<MethodDescription> FindStaticMethods<T>( string methodName ) where T : Attribute
	{
		return typedata.Values.SelectMany( x => x.Members ).OfType<MethodDescription>().Where( x => x.IsStatic && x.IsNamed( methodName ) && x.Attributes.OfType<T>().Any() );
	}

	/// <summary>
	/// Find all member attributes (instances) with given attribute type.
	/// </summary>
	public IEnumerable<T> GetMemberAttributes<T>() where T : Attribute
	{
		return typedata.Values.SelectMany( x => x.Members ).SelectMany( x => x.Attributes ).OfType<T>();
	}

	/// <summary>
	/// Find all static members with given attribute.
	/// </summary>
	internal IEnumerable<(MemberDescription Member, T Attribute)> GetMembersWithAttribute<T>() where T : Attribute
	{
		return typedata.Values.SelectMany( x => x.Members )
			.OfType<MemberDescription>()
			.Where( x => x.IsStatic )
			.SelectMany( x => x.Attributes.OfType<T>().Select<T, (MemberDescription Member, T Attribute)>( y => new( x, y ) ) );
	}

	/// <summary>
	/// Find all static or non static only member attributes (instances) with given attribute type.
	/// </summary>
	public IEnumerable<T> GetMemberAttributes<T>( bool staticMembers ) where T : Attribute
	{
		return typedata.Values.SelectMany( x => x.Members ).Where( x => x.IsStatic == staticMembers ).SelectMany( x => x.Attributes ).OfType<T>();
	}
}
