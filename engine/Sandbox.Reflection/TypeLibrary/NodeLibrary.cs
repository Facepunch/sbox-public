using Facepunch.ActionGraphs;
using Sandbox.Internal;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sandbox.ActionGraphs
{
	public static class ActionGraphExtensions
	{
		public static object GetEmbeddedTarget( this ActionGraph actionGraph )
			=> actionGraph.Inputs.Values.FirstOrDefault( x => x.IsTarget )?.Default;

		public static object GetEmbeddedTarget( this IActionGraphDelegate actionGraph )
			=> actionGraph.Graph.Inputs.Values.FirstOrDefault( x => x.IsTarget ) is { } targetInput
				? actionGraph.Defaults.TryGetValue( targetInput.Name, out var target )
					? target
					: targetInput.Default
				: null;

		public static Type GetTargetType( this ActionGraph actionGraph )
			=> actionGraph.Inputs.Values.FirstOrDefault( x => x.IsTarget )?.Type;

		public static Type GetTargetType( this IActionGraphDelegate actionGraph )
			=> actionGraph.Graph.GetTargetType();

		public static bool CanActionGraphRead( this PropertyDescription property, NodeLibrary nodeLibrary )
			=> ((TypeLoader)nodeLibrary.TypeLoader).CanRead( (PropertyInfo)property.MemberInfo );

		public static bool CanActionGraphWrite( this PropertyDescription property, NodeLibrary nodeLibrary )
			=> ((TypeLoader)nodeLibrary.TypeLoader).CanWrite( (PropertyInfo)property.MemberInfo );

		public static bool CanActionGraphRead( this FieldDescription field, NodeLibrary nodeLibrary )
			=> ((TypeLoader)nodeLibrary.TypeLoader).CanRead( (FieldInfo)field.MemberInfo );

		public static bool CanActionGraphWrite( this FieldDescription field, NodeLibrary nodeLibrary )
			=> ((TypeLoader)nodeLibrary.TypeLoader).CanWrite( (FieldInfo)field.MemberInfo );

		public static bool IsPure( this MethodDescription methodDesc, NodeLibrary nodeLibrary )
		{
			return nodeLibrary.IsPure( (MethodInfo)methodDesc.MemberInfo );
		}

		public static bool AreParametersActionGraphSafe( this MethodDescription methodDesc )
		{
			return ((MethodBase)methodDesc.MemberInfo).AreParametersActionGraphSafe();
		}

		public static bool AreParametersActionGraphSafe( this MethodBase methodBase )
		{
			foreach ( var parameter in methodBase.GetParameters() )
			{
				var parameterType = parameter.ParameterType;

				if ( parameterType.IsByRef )
				{
					if ( parameter.IsOut == parameter.IsIn )
					{
						return false;
					}

					parameterType = parameterType.GetElementType()!;
				}

				if ( parameterType.IsArray )
				{
					parameterType = parameterType.GetElementType()!;
				}

				if ( parameterType.IsByRefLike )
				{
					// ref struct (Span<> etc)

					return false;
				}

				if ( parameterType.IsPointer )
				{
					return false;
				}

				if ( typeof( Delegate ).IsAssignableFrom( parameterType ) )
				{
					// Delegate parameters become output signals, and must return a Task or void
					// TODO: can relax this when we support inline lambdas for stuff like Linq

					var invokeMethod = parameterType.GetMethod( "Invoke" );
					var returnType = invokeMethod?.ReturnType;

					if ( returnType != typeof( void ) && returnType != typeof( Task ) )
					{
						return false;
					}

					if ( !AreParametersActionGraphSafe( invokeMethod ) )
					{
						return false;
					}
				}
			}

			return true;
		}

		internal static bool IsActionGraphIncluded( this MemberInfo member )
		{
			if ( member is null )
			{
				return false;
			}

			var inherit = member is not Type;

			return member.GetCustomAttributes( inherit )
				.Any( x => x is ActionGraphIncludeAttribute or INodeAttribute );
		}

		internal static bool IsActionGraphIgnored( this MemberInfo member )
		{
			if ( member is null )
			{
				return true;
			}

			var inherit = member is not Type;

			return member.GetCustomAttribute<ActionGraphIgnoreAttribute>( inherit ) is not null
				|| member.GetCustomAttribute<ActionGraphIncludeAttribute>( inherit ) is null
				&& member.DeclaringType != null && IsActionGraphIgnored( member.DeclaringType );
		}

		public static bool IsActionGraphIgnored( this MemberDescription memberDesc )
		{
			return IsActionGraphIgnored( memberDesc.MemberInfo );
		}

		public static bool IsActionGraphIgnored( this TypeDescription typeDesc )
		{
			return IsActionGraphIgnored( typeDesc.TargetType );
		}

		private const string ReferencedComponentTypesKey = "ReferencedComponentTypes";

		public static void UpdateReferences( this ActionGraph graph )
		{
			var set = new HashSet<Type>();

			foreach ( var node in graph.Nodes.Values )
			{
				switch ( node.Definition.Identifier )
				{
					case "scene.get":
						{
							if ( node.Definition.Identifier != "scene.get" )
							{
								break;
							}

							if ( !node.Properties.TryGetValue( "T", out var typeProperty ) )
							{
								break;
							}

							if ( typeProperty.Value is Type type )
							{
								set.Add( type );
							}

							break;
						}

					case "graph":
						{
							if ( !node.Properties.TryGetValue( "graph", out var graphProperty ) )
							{
								break;
							}

							if ( graphProperty.Value is not string graphPath )
							{
								break;
							}

							var referencedGraph = graph.NodeLibrary.GraphLoader.LoadGraph( graphPath );

							if ( referencedGraph == graph )
							{
								break;
							}

							foreach ( var type in referencedGraph.GetReferencedComponentTypes() )
							{
								set.Add( type );
							}

							break;
						}
				}
			}

			var options = new JsonSerializerOptions( JsonSerializerOptions.Default )
			{
				Converters = { new Facepunch.ActionGraphs.TypeConverter( graph.NodeLibrary.TypeLoader ) }
			};

			graph.UserData[ReferencedComponentTypesKey] = new JsonArray( set
				.Select( x => JsonSerializer.SerializeToNode( x, options ) )
				.ToArray() );
		}

		/// <summary>
		/// Gets all component types referenced using "scene.get" nodes. These components are expected
		/// to be on the GameObject containing the graph.
		/// </summary>
		public static IReadOnlyCollection<Type> GetReferencedComponentTypes( this ActionGraph graph )
		{
			if ( !graph.UserData.TryGetPropertyValue( ReferencedComponentTypesKey, out var referencedTypes ) )
			{
				return Array.Empty<Type>();
			}

			if ( referencedTypes is not JsonArray { Count: > 0 } array )
			{
				return Array.Empty<Type>();
			}

			var options = new JsonSerializerOptions( JsonSerializerOptions.Default )
			{
				Converters = { new Facepunch.ActionGraphs.TypeConverter( graph.NodeLibrary.TypeLoader ) }
			};

			return array
				.Select( x => x.Deserialize<Type>( options ) )
				.Where( x => x != null )
				.ToImmutableHashSet();
		}
	}

	/// <summary>
	/// All action graph reflection goes through here, so we can control what people can access.
	/// </summary>
	internal sealed class TypeLoader : ITypeLoader
	{
		private readonly Func<TypeLibrary> _getTypeLibrary;

		public TypeLibrary TypeLibrary => _getTypeLibrary();

		public TypeLoader( Func<TypeLibrary> getTypeLibrary )
		{
			_getTypeLibrary = getTypeLibrary;
		}

		/// <summary>
		/// Used when an action graph serializes a <see cref="Type"/>. Must match <see cref="TypeFromIdentifier"/>.
		/// </summary>
		public string TypeToIdentifier( Type type )
		{
			// We don't get ClassName through TypeLibrary (which defaults to Type.Name), because we only want
			// specifically set class names with this attribute to avoid name conflicts.

			if ( type.GetCustomAttribute<ClassNameAttribute>( false ) is { Value: { } className } && className != type.Name )
			{
				return className;
			}

			return type.FullName!;
		}

		private static Regex LegacyTypeNamePattern { get; } = new( @"^(?<assembly>[^/]+)/(?<type>.+)$" );

		/// <summary>
		/// Used when an action graph deserializes a <see cref="Type"/>. Must match <see cref="TypeToIdentifier"/>.
		/// </summary>
		public Type TypeFromIdentifier( string value )
		{
			var matches = TypeLibrary
				.GetTypes()
				.Where( x => x.ClassName == value )
				.ToArray();

			if ( matches.Length == 1 )
			{
				return matches[0].TargetType;
			}

			if ( matches.Length > 1 )
			{
				// Legacy support for when we serialized only ClassName, but that was ambiguous!
				// Let's prefer the shortest full name

				return matches
					.MinBy( x => x.FullName.Length )
					.TargetType;
			}

			if ( LegacyTypeNamePattern.Match( value ) is { Success: true } match )
			{
				// Legacy support for assembly qualified type names

				value = match.Groups["type"].Value;
			}

			return TypeLibrary.GetTypes()
				.FirstOrDefault( x => x.TargetType?.FullName == value )
				?.TargetType
				?? throw new Exception( $"Unable to find type '{value}'." );
		}

		public PropertyInfo GetProperty( Type declaringType, string name )
		{
			return ResolveGenericTypeMember( declaringType,
				TypeLibrary.GetType( declaringType )?.Properties
					.FirstOrDefault( x => x.Name == name )?.MemberInfo as PropertyInfo );
		}

		public FieldInfo GetField( Type declaringType, string name )
		{
			return ResolveGenericTypeMember( declaringType, TypeLibrary.GetType( declaringType )?.Fields
				.FirstOrDefault( x => x.Name == name )?.MemberInfo as FieldInfo );
		}

		public bool CanRead( PropertyInfo property )
		{
			if ( !property.CanRead )
			{
				return false;
			}

			if ( property.GetMethod!.GetCustomAttribute<ActionGraphIgnoreAttribute>() is not null )
			{
				return false;
			}

			if ( property.GetMethod!.GetCustomAttribute<ActionGraphIncludeAttribute>() is not null )
			{
				return true;
			}

			return property.GetMethod.IsPublic;
		}

		public bool CanWrite( PropertyInfo property )
		{
			if ( !property.CanWrite )
			{
				return false;
			}

			if ( property.DeclaringType!.IsEnum )
			{
				return false;
			}

			if ( property.SetMethod!.ReturnParameter!.GetRequiredCustomModifiers().Contains( typeof( IsExternalInit ) ) )
			{
				// Apparently this is how you check for { init; }
				return false;
			}

			if ( property.SetMethod!.GetCustomAttribute<ActionGraphIgnoreAttribute>() is not null )
			{
				return false;
			}

			if ( property.SetMethod!.GetCustomAttribute<ActionGraphIncludeAttribute>() is not null )
			{
				return true;
			}

			if ( property.GetCustomAttribute<ReadOnlyAttribute>() is not null )
			{
				return false;
			}

			return property.SetMethod.IsPublic;
		}

		public bool CanRead( FieldInfo field )
		{
			if ( field.DeclaringType!.IsEnum )
			{
				return false;
			}

			if ( field.GetCustomAttribute<ActionGraphIgnoreAttribute>() is not null )
			{
				return false;
			}

			if ( field.GetCustomAttribute<ActionGraphIncludeAttribute>() is not null )
			{
				return true;
			}

			return field.IsPublic;
		}

		public bool CanWrite( FieldInfo field )
		{
			return !field.IsInitOnly && CanRead( field ) && field.GetCustomAttribute<ReadOnlyAttribute>() is null;
		}

		private bool IsUserType( Type type )
		{
			if ( !TypeLibrary.TryGetType( type, out var typeDesc ) )
			{
				return false;
			}

			// Engine types will have their constructors exposed as nodes already

			if ( !typeDesc.IsDynamicAssembly )
			{
				return false;
			}

			// Don't allow creating instances of types derived from engine types, like Component

			return type.BaseType == typeof( object ) || IsUserType( type.BaseType );
		}

		public IReadOnlyList<ConstructorInfo> GetConstructors( Type declaringType )
		{
			if ( !IsUserType( declaringType ) )
			{
				// Not a user-created type, so only return explicitly exposed constructors

				return declaringType.GetConstructors()
					.Where( x => x.AreParametersActionGraphSafe() && x.IsActionGraphIncluded() )
					.ToArray();
			}

			// User type, return everything that isn't explicitly excluded

			return declaringType.GetConstructors()
				.Where( x => x.AreParametersActionGraphSafe() && !x.IsActionGraphIgnored() )
				.ToArray();
		}

		public IReadOnlyList<MethodInfo> GetMethods( Type declaringType, string name )
		{
			return TypeLibrary.GetType( declaringType )?.Methods
				.Where( x => x.Name == name && x.AreParametersActionGraphSafe() && !x.IsActionGraphIgnored() )
				.Select( x => (MethodInfo)x.MemberInfo )
				.Select( x => ResolveGenericTypeMember( declaringType, x ) )
				.ToArray() ?? Array.Empty<MethodInfo>();
		}

		private static T ResolveGenericTypeMember<T>( Type declaringType, T member )
			where T : MemberInfo
		{
			if ( member == null )
			{
				return null;
			}

			if ( !declaringType.IsConstructedGenericType || member.DeclaringType != declaringType.GetGenericTypeDefinition() )
			{
				return member;
			}

			return declaringType.GetMemberWithSameMetadataDefinitionAs( member ) as T;
		}

		public Type GetNestedType( Type declaringType, string name )
		{
			var nested = declaringType.GetNestedType( name );
			TypeLibrary.AssertType( nested );
			return nested;
		}

		public Type MakeArrayType( Type elementType, int? rank )
		{
			TypeLibrary.AssertType( elementType );
			return rank == null ? elementType.MakeArrayType() : elementType.MakeArrayType( rank.Value );
		}

		public Type MakeGenericType( Type genericTypeDefinition, Type[] genericArguments )
		{
			return TypeLibrary.GetType( genericTypeDefinition ).MakeGenericType( genericArguments );
		}
	}

	internal sealed class GraphLoader : IGraphLoader
	{
		public static Func<string, ActionGraph> OnLoadGraph { get; set; }

		public ActionGraph LoadGraph( string path )
		{
			return (OnLoadGraph ?? throw new Exception( $"{nameof( TypeLoader )}.{nameof( OnLoadGraph )} not set." ))
				.Invoke( path );
		}
	}
}
