using Sandbox;
using System;
using System.Linq;

namespace Editor.Mcp;

[McpToolset( "component", "Component type documentation - what components exist and what properties they expose" )]
public static partial class ComponentTools
{
	/// <summary>
	/// Documentation for a component type by name - its class name, display title, description, group, and
	/// every inspector property with title, description, type and read-only state. Matches exact names first,
	/// then substrings; multiple substring matches come back as a candidate list.
	/// </summary>
	/// <param name="name">The component's type name, e.g. 'ModelRenderer'. Case insensitive, substrings work.</param>
	[McpTool.ReadOnly( "get_component_type" )]
	public static object GetComponentType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( "Give a component type name, e.g. 'ModelRenderer'" );

		var all = TypeLibrary.GetTypes<Component>().Where( x => !x.IsAbstract && !x.IsInterface ).ToArray();

		var type = all.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( x.ClassName, name, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( x.Title, name, StringComparison.OrdinalIgnoreCase ) );

		if ( type is null )
		{
			var candidates = all.Where( x => x.Name.Contains( name, StringComparison.OrdinalIgnoreCase ) ).ToArray();

			if ( candidates.Length == 0 )
				throw new Exception( $"No component type matches '{name}'" );

			if ( candidates.Length > 1 )
				return new { Matches = candidates.Select( x => x.Name ).OrderBy( x => x ).ToArray() };

			type = candidates[0];
		}

		return new
		{
			ClassName = type.Name,
			Namespace = type.Namespace,
			Title = type.Title,
			Description = type.Description,
			Group = type.Group,
			Icon = type.Icon,
			BaseType = type.TargetType.BaseType?.Name,
			Properties = type.Properties
				.Where( x => !x.IsStatic && x.HasAttribute<Sandbox.PropertyAttribute>() )
				.Select( x => new
				{
					x.Name,
					Title = x.Title,
					Description = x.Description,
					Type = PrettyTypeName( x.PropertyType ),
					Group = x.Group,
					ReadOnly = !x.CanWrite
				} ).ToArray()
		};
	}

	/// <summary>
	/// Find a single component type by name - exact match first, then a lone substring match.
	/// Throws when unknown or ambiguous.
	/// </summary>
	internal static TypeDescription FindComponentType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new Exception( "Give a component type name, e.g. 'ModelRenderer'" );

		var all = TypeLibrary.GetTypes<Component>().Where( x => !x.IsAbstract && !x.IsInterface ).ToArray();

		var type = all.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( x.ClassName, name, StringComparison.OrdinalIgnoreCase ) );

		if ( type is not null )
			return type;

		var candidates = all.Where( x => x.Name.Contains( name, StringComparison.OrdinalIgnoreCase ) ).ToArray();

		if ( candidates.Length == 1 )
			return candidates[0];

		if ( candidates.Length > 1 )
			throw new Exception( $"'{name}' matches several component types: {string.Join( ", ", candidates.Select( x => x.Name ).Order().Take( 10 ) )}" );

		throw new Exception( $"No component type matches '{name}' - get_component_type documents what exists" );
	}

	private static string PrettyTypeName( Type type )
	{
		if ( type is null ) return null;

		if ( Nullable.GetUnderlyingType( type ) is Type inner )
			return $"{PrettyTypeName( inner )}?";

		if ( type.IsGenericType )
		{
			var args = string.Join( ", ", type.GetGenericArguments().Select( PrettyTypeName ) );
			var tick = type.Name.IndexOf( '`' );
			return $"{(tick < 0 ? type.Name : type.Name[..tick])}<{args}>";
		}

		return type.Name;
	}
}
