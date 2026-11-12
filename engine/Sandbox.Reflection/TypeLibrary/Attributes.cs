using System.Collections.Immutable;

namespace Sandbox.Internal;

public partial class TypeLibrary
{
	/// <summary>
	/// Get all attributes of this type
	/// </summary>
	public IReadOnlyList<T> GetAttributes<T>() where T : Attribute
	{
		return Cached( HashCode.Combine( "GetAttributes", typeof( T ) ), this, static lib => lib.typedata.Values.SelectMany( x => x.Attributes.OfType<T>() ).ToImmutableList() );
	}

	/// <summary>
	/// Get all attributes of this type. Returns the type description along with the attribute. This will 
	/// also return types that inherit the attribute from base classes too.
	/// </summary>
	public IReadOnlyList<(TypeDescription Type, T Attribute)> GetTypesWithAttribute<T>() where T : Attribute
	{
		return Cached( HashCode.Combine( "GetTypesWithAttribute", typeof( T ) ), this,
					static lib => lib.typedata.Values
									.SelectMany( type => type.Attributes.OfType<T>()
															.Select( a => (Type: type, Attribute: a) )
												)
												.ToImmutableList()
									);
	}

	/// <summary>
	/// Get all attributes of this type. Returns the type description along with the attribute.
	/// If inherited is false, we will return only classes that contain this attribute directly.
	/// </summary>
	public IReadOnlyList<(TypeDescription Type, T Attribute)> GetTypesWithAttribute<T>( bool inherited ) where T : Attribute
	{
		return Cached( HashCode.Combine( "GetTypesWithAttributeB", typeof( T ), inherited ), (Library: this, Inherited: inherited),
			static s => s.Library.typedata.Values.SelectMany( type =>
						(s.Inherited ? type.Attributes : type.OwnAttributes)
							.OfType<T>()
							.Select( a => (type, a) ) )
							.ToImmutableList()
							);
	}

	/// <summary>
	/// Get single attribute of type, from type
	/// </summary>
	public T GetAttribute<T>( Type t ) where T : Attribute
	{
		var data = GetType( t );
		if ( data == null ) return default;

		return data.Attributes.OfType<T>().FirstOrDefault();
	}

	/// <summary>
	/// Get all attribute of type, from all types assignable to type
	/// </summary>
	public IReadOnlyList<T> GetAttributes<T>( Type t ) where T : Attribute
	{
		return Cached( HashCode.Combine( "GetAttributesAssignable", typeof( T ), t ), (Library: this, Type: t), static s => s.Library.typedata.Values
					.Where( x => x.TargetType.IsAssignableTo( s.Type ) )
					.SelectMany( x => x.Attributes )
					.OfType<T>().ToImmutableList() );
	}
}

