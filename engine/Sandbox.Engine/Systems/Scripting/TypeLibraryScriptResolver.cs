using Sandbox.Internal;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// Supplies the current context's exposed metadata. Breen owns binding and overload resolution.
/// </summary>
internal sealed class TypeLibraryScriptResolver( TypeLibrary library ) : Breen.IResolver, Breen.IEditorMetadata
{
	public Type ResolveType( string name )
	{
		// Scripts use C# type names. An editor alias such as "color" must not shadow Color.
		var types = library.GetTypes();
		return (types.FirstOrDefault( type => type.FullName == name )
			?? types.FirstOrDefault( type => type.Name == name )
			?? library.GetType( name ))?.TargetType;
	}

	public string GetDescription( MemberInfo member ) => library.GetType( member.DeclaringType )?.Members
		.FirstOrDefault( x => x.MemberInfo == member )?.Description;

	public IEnumerable<Breen.EditorType> GetTypes() => library.GetTypes()
		.OrderBy( type => type.Name, StringComparer.Ordinal )
		.Select( type => type.ToBreenEditorType() );

	// Reflected member suggestions are derived by Breen from the same catalog used for execution.
	IEnumerable<Breen.EditorMember> Breen.IEditorMetadata.GetMembers( Type type, bool isStatic ) => [];

	public void GetMembers( Type type, List<MemberInfo> members )
	{
		var description = library.GetType( type );
		if ( description is null ) return;

		foreach ( var member in description.Members )
		{
			// Private members may be registered for serialization. They are not script APIs.
			if ( member.MemberInfo is MethodInfo { IsPublic: true }
				or FieldInfo { IsPublic: true }
				or PropertyInfo )
			{
				// Breen checks public getter/setter access separately when binding properties.
				var info = member.MemberInfo;
				// TypeLibrary stores generic definitions; bind their approved members to the
				// actual receiver type (for example List<int>, rather than List<T>).
				if ( type.IsConstructedGenericType && info.DeclaringType.ContainsGenericParameters )
					info = type.GetMemberWithSameMetadataDefinitionAs( info );
				if ( IsScriptSafe( info ) )
					members.Add( info );
			}
		}

		// TypeDescription.Create exposes public constructors on registered types too.
		if ( type.IsByRefLike ) return;
		members.AddRange( type.GetConstructors().Where( IsScriptSafe ) );
	}

	// Breen can reuse argument temporaries, so nothing may hand a script a ref that outlives the call.
	static bool IsScriptSafe( MemberInfo member ) => member switch
	{
		MethodBase method => (method is not MethodInfo info || !IsRefLike( info.ReturnType ))
			&& method.GetParameters().All( p => !IsRefStruct( p.ParameterType ) ),
		PropertyInfo property => !IsRefLike( property.PropertyType )
			&& property.GetIndexParameters().All( p => !IsRefStruct( p.ParameterType ) ),
		FieldInfo field => !IsRefLike( field.FieldType ),
		_ => true
	};

	static bool IsRefLike( Type type ) => type.IsByRef || IsRefStruct( type );

	static bool IsRefStruct( Type type ) => (type.IsByRef ? type.GetElementType() : type).IsByRefLike;
}
