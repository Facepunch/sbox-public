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
				members.Add( info );
			}
		}

		// TypeDescription.Create exposes public constructors on registered types too.
		members.AddRange( type.GetConstructors() );
	}
}
