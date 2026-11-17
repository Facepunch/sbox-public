namespace Sandbox;

internal sealed partial class TypeLibraryScriptResolver
{
	private IReadOnlyList<TypeDescription> indexedCatalog;
	private TypeNameIndex typeNames;

	/// <summary>
	/// Keeps one immutable CLR-name index per resolver/catalog generation. The existing catalog snapshot
	/// changes on registration or removal; changing incoming assemblies also clears this index.
	/// No script-supplied query names or missing-name results are retained.
	/// </summary>
	private TypeNameIndex GetTypeNames( IReadOnlyList<TypeDescription> catalog )
	{
		if ( typeNames is not null && ReferenceEquals( indexedCatalog, catalog ) )
		{
			return typeNames;
		}

		var types = incomingTypes.Concat( catalog
			.Where( type => incomingAssemblyNames.Count == 0 || !incomingAssemblyNames.Contains( type.TargetType.Assembly.GetName().Name ) )
			.Select( type => type.TargetType ) ).ToArray();
		var replacement = new TypeNameIndex( types );
		indexedCatalog = catalog;
		typeNames = replacement;
		return replacement;
	}

	/// <summary>
	/// Preserves exact-name, top-level-name and nested-name precedence, including catalog order for ties.
	/// Only supplied types participate; walking a nested name cannot discover unregistered CLR types.
	/// </summary>
	private sealed class TypeNameIndex
	{
		private readonly Dictionary<string, Type> names;
		private readonly Dictionary<(Type Owner, string Name), Type> nested;

		internal TypeNameIndex( Type[] types )
		{
			int qualifiedAliases = 0;
			int publicNested = 0;
			foreach ( var type in types )
			{
				qualifiedAliases += type.FullName.Contains( '+' ) ? 1 : 0;
				publicNested += type.IsNestedPublic ? 1 : 0;
			}

			// At most one full name, one simple name and one normalized name per type. Reserve once
			// rather than repeatedly growing large dictionaries during every catalog publication.
			names = new( types.Length * 2 + qualifiedAliases, StringComparer.Ordinal );
			nested = new( publicNested );

			foreach ( var type in types )
			{
				names.TryAdd( type.FullName, type );
				names.TryAdd( type.FullName.Replace( '+', '.' ), type );
				if ( type.IsNestedPublic )
				{
					nested.TryAdd( (type.DeclaringType, type.Name), type );
				}
			}

			foreach ( var type in types )
			{
				if ( !type.IsNested )
				{
					names.TryAdd( type.Name, type );
				}
			}

			foreach ( var type in types )
			{
				names.TryAdd( type.Name, type );
			}
		}

		internal Type Find( string name ) => names.GetValueOrDefault( name );
		internal Type FindNested( Type owner, string name ) => nested.GetValueOrDefault( (owner, name) );
	}
}
