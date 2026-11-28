using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Sandbox.Internal;
using Sandbox.Upgraders;

namespace Sandbox
{
	/// <summary>
	/// See Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNameKind
	/// </summary>
	internal enum GeneratedNameKind
	{
		None = 0,

		// Used by EE:
		ThisProxyField = '4',
		HoistedLocalField = '5',
		DisplayClassLocalOrField = '8',
		LambdaMethod = 'b',
		LambdaDisplayClass = 'c',
		StateMachineType = 'd',
		LocalFunction = 'g', // note collision with Deprecated_InitializerLocal, however this one is only used for method names

		// Used by EnC:
		AwaiterField = 'u',
		HoistedSynthesizedLocalField = 's',

		// Currently not parsed:
		StateMachineStateField = '1',
		IteratorCurrentBackingField = '2',
		StateMachineParameterProxyField = '3',
		ReusableHoistedLocalField = '7',
		LambdaCacheField = '9',
		FixedBufferField = 'e',
		Extension = 'E',
		FileType = 'F',
		AnonymousType = 'f',
		TransparentIdentifier = 'h',
		AnonymousTypeField = 'i',
		StateMachineStateIdField = 'I',
		AnonymousTypeTypeParameter = 'j',
		AutoPropertyBackingField = 'k',
		IteratorCurrentThreadIdField = 'l',
		IteratorFinallyMethod = 'm',
		BaseMethodWrapper = 'n',
		AsyncBuilderField = 't',
		DelegateCacheContainerType = 'O',
		DynamicCallSiteContainerType = 'o',
		PrimaryConstructorParameter = 'P',
		DynamicCallSiteField = 'p',
		AsyncIteratorPromiseOfValueOrEndBackingField = 'v',
		DisposeModeField = 'w',
		CombinedTokensField = 'x',
		InlineArrayType = 'y',
		ReadOnlyListType = 'z', // last
	}

	/// <summary>
	/// See Microsoft.CodeAnalysis.CSharp.Symbols.GeneratedNames.MakeMethodScopedSynthesizedName
	/// </summary>
	internal record struct GeneratedName(
		string ScopeName, GeneratedNameKind Kind,
		string Suffix,
		int Ordinal1, int Generation1,
		int Ordinal2, int Generation2,
		int TypeParameterCount )
	{

		private static readonly Regex Regex =
			new Regex( @"
				^<(?<scopeName>[^>]+)?>
				(?<kind>[a-z0-9])
				(
					__(?:(?<suffix>[A-Za-z_][A-Za-z0-9_]+)\||(?<suffix>[A-Za-z_]+))?
					(
						(?<ordinal1>[0-9]+)(\#(?<generation1>[0-9]+))?
						(
							_(?<ordinal2>[0-9]+)(\#(?<generation2>[0-9]+))?
						)?
					)?
				)?(`(?<typeParams>[0-9]+))?$
			", RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace );

		public static bool TryParse( string value, out GeneratedName result )
		{
			var match = Regex.Match( value );

			if ( !match.Success )
			{
				result = default;
				return false;
			}

			result = new GeneratedName(
				ScopeName: match.Groups["scopeName"].Success ? match.Groups["scopeName"].Value : null,
				Kind: (GeneratedNameKind)match.Groups["kind"].Value[0],
				Suffix: match.Groups["suffix"].Success ? match.Groups["suffix"].Value : null,
				Ordinal1: match.Groups["ordinal1"].Success ? int.Parse( match.Groups["ordinal1"].Value ) : -1,
				Generation1: match.Groups["generation1"].Success ? int.Parse( match.Groups["generation1"].Value ) : -1,
				Ordinal2: match.Groups["ordinal2"].Success ? int.Parse( match.Groups["ordinal2"].Value ) : -1,
				Generation2: match.Groups["generation2"].Success ? int.Parse( match.Groups["generation2"].Value ) : -1,
				TypeParameterCount: match.Groups["typeParams"].Success ? int.Parse( match.Groups["typeParams"].Value ) : 0 );
			return true;
		}
	}

	partial class Hotload
	{
		private Dictionary<MethodBase, int> ScopeMethodOrdinals { get; } = new Dictionary<MethodBase, int>();

		private static IEnumerable<MethodBase> GetMethods( Type type, string methodName )
		{
			var bFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			switch ( methodName )
			{
				case null:
					return type.GetConstructors( bFlags | BindingFlags.Instance | BindingFlags.Static )
						.Cast<MethodBase>()
						.Union( type.GetMethods( bFlags | BindingFlags.Instance | BindingFlags.Static ) );

				case ".ctor":
					return type.GetConstructors( bFlags | BindingFlags.Instance );

				case ".cctor":
					return type.GetConstructors( bFlags | BindingFlags.Static );

				default:
					return type.GetMethods( bFlags | BindingFlags.Static | BindingFlags.Instance )
						.Where( x => x.Name == methodName );
			}
		}

		private static bool MatchesScopeMethodName( MethodBase method, string scopeMethodName )
		{
			if ( scopeMethodName == null )
			{
				return true;
			}

			if ( method.Name == scopeMethodName )
			{
				return true;
			}

			if ( !DelegateUpgrader.IsCompilerGenerated( method.Name ) )
			{
				return false;
			}

			if ( !GeneratedName.TryParse( method.Name, out var name ) || name.Kind != GeneratedNameKind.LocalFunction )
			{
				return false;
			}

			return name.ScopeName == scopeMethodName;
		}

		private MethodBase FindScopeMethod( Type declaringType, string scopeMethodName, int scopeMethodOrdinal )
		{
			Assert.True( scopeMethodOrdinal >= 0 );

			var methods = GetMethods( declaringType, scopeMethodName );

			foreach ( var method in methods )
			{
				var ordinal = GetScopeMethodOrdinal( method );

				if ( ordinal == scopeMethodOrdinal )
				{
					return method;
				}
			}

			Log( HotloadEntryType.Warning, $"Unable to find scope method (Name: {scopeMethodName}, Ordinal: {scopeMethodOrdinal})", declaringType );
			return null;
		}

		private static int ParseScopeMethodOrdinal( MethodReference lambdaMethodRef )
		{
			if ( !DelegateUpgrader.GetLambdaMethodInfo( lambdaMethodRef, out _, out _, out var nameInfo ) )
			{
				return -1;
			}

			return nameInfo.ScopeMethodOrdinal;
		}

		private int GetScopeMethodOrdinal( MethodBase scopeMethod )
		{
			if ( ScopeMethodOrdinals.TryGetValue( scopeMethod, out var ordinal ) )
			{
				return ordinal;
			}

			ordinal = -1;

			Assert.NotNull( scopeMethod.DeclaringType );

			if ( scopeMethod.GetCustomAttribute<StateMachineAttribute>() is { } stateMachineAttribute )
			{
				if ( GeneratedName.TryParse( stateMachineAttribute.StateMachineType.Name, out var stateMachineTypeName ) )
				{
					Assert.AreEqual( GeneratedNameKind.StateMachineType, stateMachineTypeName.Kind );
					Assert.True( stateMachineTypeName.Ordinal1 >= 0 );

					return stateMachineTypeName.Ordinal1;
				}
			}

			var scopeMethodDef = GetMethodDefinition( scopeMethod );

			if ( scopeMethodDef is { HasBody: true } )
			{
				foreach ( var inst in scopeMethodDef.Body.Instructions )
				{
					if ( inst.Operand is not MethodReference methodRef )
					{
						continue;
					}

					var parsedOrdinal = ParseScopeMethodOrdinal( methodRef );

					if ( parsedOrdinal == -1 )
					{
						continue;
					}

					Assert.True( ordinal == -1 || ordinal == parsedOrdinal );

					ordinal = parsedOrdinal;
				}
			}

			return ScopeMethodOrdinals[scopeMethod] = ordinal;
		}

		private Type GetNewNestedGeneratedType( Type oldType, GeneratedName oldName )
		{
			var oldDeclaringType = oldType.DeclaringType;
			var newDeclaringType = GetNewType( oldDeclaringType );

			if ( newDeclaringType == null )
			{
				return null;
			}

			if ( oldName.Ordinal1 == -1 && oldName.Ordinal2 == -1 )
			{
				return newDeclaringType.GetNestedType( oldType.Name, BindingFlags.Public | BindingFlags.NonPublic );
			}

			var oldScopeMethod = FindScopeMethod( oldDeclaringType, null, oldName.Ordinal1 );

			Assert.NotNull( oldScopeMethod );

			var newScopeMethod = GetNewInstance( oldScopeMethod ) as MethodBase;

			if ( newScopeMethod == null )
			{
				return null;
			}

			var newScopeOrdinal = GetScopeMethodOrdinal( newScopeMethod );

			return newDeclaringType
				.GetNestedTypes( BindingFlags.Public | BindingFlags.NonPublic )
				.FirstOrDefault( x => GeneratedName.TryParse( x.Name, out var newName )
					&& newName.Kind == oldName.Kind
					&& newName.Suffix == oldName.Suffix
					&& newName.Ordinal1 == newScopeOrdinal
					&& newName.Ordinal2 == oldName.Ordinal2 );
		}

		private Type GetNewReadOnlyListType( Type oldType )
		{
			// This method expects a generic type definition because readonly list types are always generic.
			// Passing a constructed type or non-generic type would be incorrect and may cause runtime errors.
			Assert.True( oldType.IsGenericTypeDefinition );

			// Assembly not hotloaded, so type hasn't changed
			if ( !TryGetTypeReplacement( oldType.Assembly, out var newAsm ) ) return oldType;

			// Assembly was unloaded, so replace with null
			if ( newAsm is null ) return null;

			// Just match by name, easy
			return oldType.FullName is not { } fullName
				? null
				: newAsm.GetType( fullName );
		}

		/// <summary>
		/// Anonymous types are generic types with a type parameter for each property's type.
		/// That means the compiler will reuse the same type definition as long as property names match.
		/// </summary>
		/// <param name="Type">Generic type definition for the anonymous type.</param>
		/// <param name="PropertyNames">Names of the anonymous type's properties.</param>
		private readonly record struct AnonymousTypeInfo(
			Type Type,
			ImmutableArray<string> PropertyNames )
		{
			/// <summary>
			/// Anonymous types are equivalent as long as the property names match.
			/// </summary>
			public bool HasMatchingPropertyNames( AnonymousTypeInfo other )
			{
				if ( PropertyNames.Length != other.PropertyNames.Length ) return false;

				for ( var i = 0; i < PropertyNames.Length; ++i )
				{
					var a = PropertyNames[i];
					var b = other.PropertyNames[i];

					if ( !a.Equals( b, StringComparison.Ordinal ) ) return false;
				}

				return true;
			}
		}

		[GeneratedRegex( "^<(?<name>[^>]+)>j__TPar$" )]
		private static partial Regex AnonymousTypeParamNameRegex { get; }

		/// <summary>
		/// If <paramref name="type"/> is an anonymous type, gets a description of its properties.
		/// Otherwise, returns null.
		/// </summary>
		private AnonymousTypeInfo? GetAnonymousTypeInfo( Type type )
		{
			if ( !type.IsGenericTypeDefinition ) return null;
			if ( !type.Name.StartsWith( "<>f__AnonymousType" ) ) return null;
			if ( type.GetGenericArguments() is not { Length: > 0 } genericArgs ) return null;

			var propertyNames = new string[genericArgs.Length];

			for ( var i = 0; i < genericArgs.Length; ++i )
			{
				if ( AnonymousTypeParamNameRegex.Match( genericArgs[i].Name ) is not { Success: true } match )
				{
					return null;
				}

				propertyNames[i] = match.Groups["name"].Value;
			}

			return new AnonymousTypeInfo( type, [.. propertyNames] );
		}

		private Dictionary<Assembly, ImmutableArray<AnonymousTypeInfo>> AnonymousTypes { get; } = new();

		private ImmutableArray<AnonymousTypeInfo> GetAnonymousTypes( Assembly asm )
		{
			if ( AnonymousTypes.TryGetValue( asm, out var types ) ) return types;

			return AnonymousTypes[asm] =
			[
				..asm.GetTypes()
					.Select( GetAnonymousTypeInfo )
					.Where( x => x is not null )
					.Select( x => x!.Value )
			];
		}

		/// <summary>
		/// Find an anonymous type in a hotloaded assembly with matching property names, or null if not found.
		/// </summary>
		private Type GetNewAnonymousType( Type oldType )
		{
			Assert.True( oldType.IsGenericTypeDefinition );

			// Assembly not hotloaded, so type hasn't changed
			if ( !TryGetTypeReplacement( oldType.Assembly, out var newAsm ) ) return oldType;

			// Assembly was unloaded, so replace with null
			if ( newAsm is null ) return null;

			if ( GetAnonymousTypeInfo( oldType ) is not { } oldTypeInfo ) return null;

			foreach ( var newTypeInfo in GetAnonymousTypes( newAsm ) )
			{
				if ( newTypeInfo.HasMatchingPropertyNames( oldTypeInfo ) )
				{
					return newTypeInfo.Type;
				}
			}

			// No matches found, will have to replace with null

			Log( HotloadEntryType.Warning,
				$"Couldn't find exact replacement for anonymous type {{ {string.Join( ", ", oldTypeInfo.PropertyNames )} }}.",
				oldType );

			return null;
		}
	}

	internal static partial class HotloadReflectionExtensions
	{
		public static bool IsBackingField( this FieldInfo fieldInfo )
		{
			return GeneratedName.TryParse( fieldInfo.Name, out var name )
				&& name.Kind == GeneratedNameKind.AutoPropertyBackingField;
		}

		public static bool TryGetBackedProperty( this FieldInfo fieldInfo, out PropertyInfo propertyInfo )
		{
			if ( !GeneratedName.TryParse( fieldInfo.Name, out var name )
				|| name.Kind != GeneratedNameKind.AutoPropertyBackingField )
			{
				propertyInfo = default;
				return false;
			}

			propertyInfo = fieldInfo.DeclaringType!.GetProperty( name.ScopeName,
				BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly );

			return propertyInfo != null;
		}

	}

#if NET_CORE

	internal static partial class ReflectionExtensions
	{
		private static AssemblyNameReference GetAssemblyName( this IMetadataScope scope )
		{
			switch ( scope.MetadataScopeType )
			{
				case MetadataScopeType.AssemblyNameReference:
					return (AssemblyNameReference)scope;
				case MetadataScopeType.ModuleDefinition:
					return ((ModuleDefinition)scope).Assembly.Name;
				default:
					throw new NotImplementedException();
			}
		}

		private static string GetAssemblyFullName( this IMetadataScope scope )
		{
			switch ( scope.MetadataScopeType )
			{
				case MetadataScopeType.AssemblyNameReference:
					return ((AssemblyNameReference)scope).FullName;
				case MetadataScopeType.ModuleDefinition:
					return ((ModuleDefinition)scope).Assembly.FullName;
				default:
					throw new NotImplementedException();
			}
		}

		private static bool IsMatchingAssembly( Assembly asm, AssemblyName name )
		{
			var asmName = asm.GetName();

			return asmName.Name == name.Name && (asmName.Version?.Equals( name.Version ) ?? false);
		}

		public static Type ResolveType( this Module module, TypeReference typeRef )
		{
			Assembly asm;
			var asmNameRef = typeRef.Scope.GetAssemblyName();

			if ( module.Assembly.GetName().Name == asmNameRef.Name )
			{
				asm = module.Assembly;
			}
			else
			{
				// Prefer to exactly match an assembly version referenced by this module

				var matchingName = module.Assembly.GetReferencedAssemblies()
					.FirstOrDefault( x => x.Name == asmNameRef.Name );

				var asmName = matchingName ?? new AssemblyName( asmNameRef.FullName );
				var loadCtx = AssemblyLoadContext.GetLoadContext( module.Assembly )!;

				asm = loadCtx.Assemblies.FirstOrDefault( x => IsMatchingAssembly( x, asmName ) )
					?? loadCtx!.LoadFromAssemblyName( asmName );
			}

			if ( typeRef.IsGenericInstance && typeRef is GenericInstanceType genericType )
			{
				var elemType = module.ResolveType( genericType.ElementType );
				var typeArgs = genericType.GenericArguments.Select( x => module.ResolveType( x ) ).ToArray();

				return elemType.MakeGenericType( typeArgs );
			}

			if ( typeRef.IsArray && typeRef is ArrayType arrayType )
			{
				var elemType = module.ResolveType( arrayType.ElementType );

				return arrayType.Rank == 1
					? elemType.MakeArrayType()
					: elemType.MakeArrayType( arrayType.Rank );
			}

			if ( typeRef.IsByReference && typeRef is ByReferenceType byRefType )
			{
				var elemType = module.ResolveType( byRefType.ElementType );

				return elemType.MakeByRefType();
			}

			if ( typeRef.IsNested )
			{
				var declType = module.ResolveType( typeRef.DeclaringType );
				return declType.GetNestedType( typeRef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly );
			}

			return asm.GetType( typeRef.FullName, true, false );
		}

		public static FieldInfo ResolveField( this Module module, FieldReference fieldRef )
		{
			var declaringType = module.ResolveType( fieldRef.DeclaringType );
			return declaringType.GetField( fieldRef.Name,
				BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly );
		}

		public static MethodBase ResolveMethod( this Module module, MethodReference methodRef )
		{
			var declaringType = module.ResolveType( methodRef.DeclaringType );

			MethodBase[] candidates;

			var isGenericInstance = false;
			var elementMethodRef = methodRef;

			if ( methodRef is GenericInstanceMethod genericInstance )
			{
				isGenericInstance = true;
				elementMethodRef = genericInstance.ElementMethod;
			}

			switch ( methodRef.Name )
			{
				case ".ctor":
					candidates = declaringType
						.GetConstructors( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly )
						.Where( x => x.GetParameters().Length == methodRef.Parameters.Count )
						.ToArray();
					break;

				case ".cctor":
					candidates = declaringType
						.GetConstructors( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly )
						.Where( x => x.GetParameters().Length == methodRef.Parameters.Count )
						.ToArray();
					break;

				default:
					candidates = declaringType
						.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly )
						.Where( x => x.Name == methodRef.Name && x.GetParameters().Length == methodRef.Parameters.Count && isGenericInstance == x.IsGenericMethodDefinition )
						.ToArray();
					break;
			}

			var resolvedParamerTypes = elementMethodRef.Parameters
				.Select( x => Hotload.ResolveGenericArguments( elementMethodRef, x.ParameterType ) )
				.ToArray();

			var match = candidates
				.OrderBy( x => x.GetParameters().Count( y => y.ParameterType.IsGenericParameter ) )
				.FirstOrDefault( candidate =>
			{
				var parameters = candidate.GetParameters();

				for ( var i = 0; i < parameters.Length; i++ )
				{
					if ( !Hotload.IsMatchingType( resolvedParamerTypes[i], parameters[i].ParameterType ) )
					{
						return false;
					}
				}

				return true;
			} );

			if ( match == null )
			{
				throw new Exception( $"Unable to resolve method.\n  Method Ref: {methodRef}\n  Candidates:\n{string.Join( "\n", candidates.Select( x => $"    {x}" ) )}" );
			}

			if ( match is MethodInfo { IsGenericMethodDefinition: true } method && methodRef is GenericInstanceMethod genericMethodRef )
			{
				var typeArgs = genericMethodRef.GenericArguments
					.Select( module.ResolveType )
					.ToArray();

				return method.MakeGenericMethod( typeArgs );
			}

			return match;
		}

		public static bool HasAttribute<T>( this FieldInfo fieldInfo )
			where T : Attribute
		{
			if ( fieldInfo == null ) return false;
			if ( fieldInfo.GetCustomAttribute<T>() != null ) return true;

			if ( !fieldInfo.IsPrivate || fieldInfo.GetCustomAttribute<CompilerGeneratedAttribute>() == null )
			{
				return false;
			}

			// If this is a backing field, check the property for a SkipHotload attrib
			var match = DefaultUpgrader.BackingFieldRegex.Match( fieldInfo.Name );
			if ( !match.Success ) return false;

			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
						| (fieldInfo.IsStatic ? BindingFlags.Static : BindingFlags.Instance);

			var property = fieldInfo.DeclaringType?.GetProperty( match.Groups["name"].Value, flags );

			return property != null && property.GetCustomAttribute<T>() != null;
		}
	}

	public class MemberEqualityComparer : IEqualityComparer<MemberInfo>
	{
		private readonly ConcurrentDictionary<(MemberInfo X, MemberInfo Y), bool> _cache = new();

		public bool Equals( MemberInfo x, MemberInfo y )
		{
			if ( x == y )
			{
				return true;
			}

			if ( x == null || y == null )
			{
				return false;
			}

			if ( _sInProgress.Contains( (x, y) ) )
			{
				return true;
			}

			if ( x.Name != y.Name )
			{
				return false;
			}

			if ( x.Module.Assembly.GetName().Name != y.Module.Assembly.GetName().Name )
			{
				return false;
			}

			if ( !Equals( x.DeclaringType, y.DeclaringType ) )
			{
				return false;
			}

			if ( _cache.TryGetValue( (x, y), out var cached ) )
			{
				return cached;
			}

			return _cache[(x, y)] = _cache[(y, x)] = EqualsUncached( x, y );
		}

		[ThreadStatic]
		private static HashSet<(MemberInfo X, MemberInfo Y)> _sInProgress;

		public bool AllMembersEqual( Type x, Type y )
		{
			if ( x == y )
			{
				return true;
			}

			const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.Instance | BindingFlags.Static
				| BindingFlags.DeclaredOnly;

			// Generated initializers for added fields only exist in the compile that added them

			var xMembers = x.GetMembers( AllDeclared ).Where( m => !Hotload.IsAddedFieldInitializer( m ) ).ToArray();
			var yMembers = y.GetMembers( AllDeclared ).Where( m => !Hotload.IsAddedFieldInitializer( m ) ).ToArray();

			if ( xMembers.Length != yMembers.Length )
			{
				return false;
			}

			_sInProgress ??= new HashSet<(MemberInfo, MemberInfo)>();
			Assert.True( _sInProgress.Add( (x, y) ) );

			try
			{
				for ( var i = 0; i < xMembers.Length; i++ )
				{
					var xMember = xMembers[i];
					var yMember = yMembers[i];

					if ( !Equals( xMember, yMember ) )
					{
						return false;
					}
				}
			}
			finally
			{
				_sInProgress.Remove( (x, y) );
			}

			return Equals( x.BaseType, y.BaseType );
		}

		private bool EqualsUncached( MemberInfo x, MemberInfo y )
		{
			_sInProgress ??= new HashSet<(MemberInfo, MemberInfo)>();

			// Avoid stack overflow if we recurse and check the same thing again
			if ( !_sInProgress.Add( (x, y) ) )
			{
				return true;
			}

			try
			{
				return AllAttributesEqual( x, y ) && x switch
				{
					Type typeX when y is Type typeY => EqualsUncached( typeX, typeY ),
					FieldInfo fieldX when y is FieldInfo fieldY => EqualsUncached( fieldX, fieldY ),
					MethodBase methodX when y is MethodBase methodY => EqualsUncached( methodX, methodY ),
					PropertyInfo propertyX when y is PropertyInfo propertyY => EqualsUncached( propertyX, propertyY ),
					EventInfo eventX when y is EventInfo eventY => EqualsUncached( eventX, eventY ),
					_ => false
				};
			}
			finally
			{
				_sInProgress.Remove( (x, y) );
			}
		}

		private static HashSet<Type> IgnoredAttributeTypes { get; } = new()
		{
			typeof(SupportsILHotloadAttribute),
			typeof(MethodBodyChangeAttribute),
			typeof(PropertyAccessorBodyChangeAttribute),
			typeof(SourceLocationAttribute),
			typeof(ClassFileLocationAttribute)
		};

		private bool AllAttributesEqual( MemberInfo x, MemberInfo y )
		{
			var xAttribs = x.GetCustomAttributesData()
				.Where( attrib => !IgnoredAttributeTypes.Contains( attrib.AttributeType ) )
				.ToArray();

			var yAttribs = y.GetCustomAttributesData()
				.Where( attrib => !IgnoredAttributeTypes.Contains( attrib.AttributeType ) )
				.ToArray();

			if ( xAttribs.Length != yAttribs.Length )
			{
				return false;
			}

			for ( var i = 0; i < xAttribs.Length; ++i )
			{
				var xAttrib = xAttribs[i];
				var yAttrib = yAttribs[i];

				if ( !Equals( xAttrib.AttributeType, yAttrib.AttributeType ) )
				{
					return false;
				}

				if ( !Equals( xAttrib.Constructor, yAttrib.Constructor ) )
				{
					return false;
				}

				if ( xAttrib.ConstructorArguments.Count != yAttrib.ConstructorArguments.Count )
				{
					return false;
				}

				for ( var j = 0; j < xAttrib.ConstructorArguments.Count; ++j )
				{
					var xArg = xAttrib.ConstructorArguments[j];
					var yArg = yAttrib.ConstructorArguments[j];

					if ( !AttribValueEquals( xArg, yArg ) )
					{
						return false;
					}
				}

				if ( xAttrib.NamedArguments?.Count != yAttrib.NamedArguments?.Count )
				{
					return false;
				}

				if ( xAttrib.NamedArguments == null || yAttrib.NamedArguments == null )
				{
					continue;
				}

				for ( var j = 0; j < xAttrib.NamedArguments.Count; ++j )
				{
					var xArg = xAttrib.NamedArguments[j];
					var yArg = yAttrib.NamedArguments[j];

					if ( !Equals( xArg.MemberInfo, yArg.MemberInfo ) )
					{
						return false;
					}

					if ( !AttribValueEquals( xArg.TypedValue, yArg.TypedValue ) )
					{
						return false;
					}
				}
			}

			return true;
		}

		private bool AttribValueEquals( object x, object y )
		{
			if ( x is CustomAttributeTypedArgument xArg && y is CustomAttributeTypedArgument yArg )
			{
				if ( !Equals( xArg.ArgumentType, yArg.ArgumentType ) )
				{
					return false;
				}

				x = xArg.Value;
				y = yArg.Value;
			}

			if ( x is IList xArray && y is IList yArray )
			{
				if ( xArray.Count != yArray.Count )
				{
					return false;
				}

				for ( var i = 0; i < xArray.Count; ++i )
				{
					if ( !AttribValueEquals( xArray[i], yArray[i] ) )
					{
						return false;
					}
				}

				return true;
			}

			if ( x is Type xType && y is Type yType )
			{
				return Equals( xType, yType );
			}

			if ( x is string xString && y is string yString )
			{
				return string.Equals( xString, yString, StringComparison.Ordinal );
			}

			return Equals( x, y );
		}

		private bool EqualsUncached( Type x, Type y )
		{
			if ( x.Attributes != y.Attributes )
			{
				return false;
			}

			if ( x.IsValueType != y.IsValueType || x.IsNested != y.IsNested )
			{
				return false;
			}

			if ( x.IsArray || y.IsArray )
			{
				return x.IsArray && y.IsArray
					&& x.GetArrayRank() == y.GetArrayRank()
					&& Equals( x.GetElementType(), y.GetElementType() );
			}

			if ( x.IsByRef || y.IsByRef )
			{
				return x.IsByRef && y.IsByRef
					&& Equals( x.GetElementType(), y.GetElementType() );
			}

			if ( x.IsPointer || y.IsPointer )
			{
				return x.IsPointer && y.IsPointer
					&& Equals( x.GetElementType(), y.GetElementType() );
			}

			if ( x.IsConstructedGenericType || y.IsConstructedGenericType )
			{
				return x.IsConstructedGenericType && y.IsConstructedGenericType
					&& Equals( x.GetGenericTypeDefinition(), y.GetGenericTypeDefinition() )
					&& EqualsUncached( x.GetGenericArguments(), y.GetGenericArguments() );
			}

			if ( x.HasElementType || y.HasElementType )
			{
				// TODO: better safe than sorry
				return false;
			}

			if ( x.IsGenericTypeParameter || y.IsGenericTypeParameter )
			{
				return x.IsGenericTypeParameter && y.IsGenericTypeParameter
					&& x.GenericParameterPosition == y.GenericParameterPosition;
			}

			if ( x.IsGenericMethodParameter || y.IsGenericMethodParameter )
			{
				return x.IsGenericMethodParameter && y.IsGenericMethodParameter
					&& x.GenericParameterPosition == y.GenericParameterPosition
					&& Equals( x.DeclaringMethod, y.DeclaringMethod );
			}

			if ( x.IsGenericTypeDefinition || y.IsGenericTypeDefinition )
			{
				return x.IsGenericTypeDefinition && y.IsGenericTypeDefinition
					&& EqualsUncached( x.GetGenericArguments(), y.GetGenericArguments() );
			}

			return true;
		}

		private bool EqualsUncached( FieldInfo x, FieldInfo y )
		{
			if ( x.Attributes != y.Attributes )
			{
				return false;
			}

			if ( !Equals( x.FieldType, y.FieldType ) )
			{
				return false;
			}

			return true;
		}

		private bool EqualsUncached( MethodBase x, MethodBase y )
		{
			if ( x.Attributes != y.Attributes )
			{
				return false;
			}

			if ( x is MethodInfo || y is MethodInfo )
			{
				if ( x is not MethodInfo methodX || y is not MethodInfo methodY )
				{
					return false;
				}

				if ( !Equals( methodX.ReturnType, methodY.ReturnType ) )
				{
					return false;
				}
			}

			if ( x.IsAbstract != y.IsAbstract )
			{
				return false;
			}

			var paramsX = x.GetParameters();
			var paramsY = y.GetParameters();

			if ( paramsX.Length != paramsY.Length )
			{
				return false;
			}

			for ( var i = 0; i < paramsX.Length; ++i )
			{
				if ( !Equals( paramsX[i], paramsY[i] ) )
				{
					return false;
				}
			}

			return true;
		}

		private bool Equals( ParameterInfo x, ParameterInfo y )
		{
			if ( x.Attributes != y.Attributes )
			{
				return false;
			}

			if ( x.Name != y.Name )
			{
				return false;
			}

			if ( !Equals( x.ParameterType, y.ParameterType ) )
			{
				return false;
			}

			return true;
		}

		private bool EqualsUncached( PropertyInfo x, PropertyInfo y )
		{
			if ( x.Attributes != y.Attributes )
			{
				return false;
			}

			if ( !Equals( x.PropertyType, y.PropertyType ) )
			{
				return false;
			}

			return true;
		}

		private bool EqualsUncached( EventInfo x, EventInfo y )
		{
			if ( x.Attributes != y.Attributes )
			{
				return false;
			}

			if ( !Equals( x.EventHandlerType, y.EventHandlerType ) )
			{
				return false;
			}

			return true;
		}

		private bool EqualsUncached( IReadOnlyList<Type> x, IReadOnlyList<Type> y )
		{
			if ( x.Count != y.Count )
			{
				return false;
			}

			for ( var i = 0; i < x.Count; ++i )
			{
				if ( !Equals( x[i], y[i] ) )
				{
					return false;
				}
			}

			return true;
		}

		public int GetHashCode( MemberInfo obj )
		{
			throw new NotImplementedException();
		}
	}

	internal static class MonoCecilExtensions
	{
		public static IEnumerable<Mono.Cecil.MethodDefinition> GetConstructors( this Mono.Cecil.TypeDefinition typeDef )
		{
			return typeDef.Methods.Where( x => x.IsConstructor );
		}
	}

#elif NET_4_6

    internal static partial class ReflectionExtensions
    {
        public static Delegate CreateDelegate( this MethodInfo method, Type delegType )
        {
            return Delegate.CreateDelegate( delegType, method );
        }

        public static Delegate CreateDelegate( this MethodInfo method, Type delegType, object target )
        {
            return Delegate.CreateDelegate( delegType, target, method );
        }

        public static MethodInfo GetMethodInfo( this Delegate deleg )
        {
            return deleg.Method;
        }

        public static Type GetTypeInfo( this Type type )
        {
            return type;
        }

        public static TAttrib GetCustomAttribute<TAttrib>( this MemberInfo member, bool inherit = false )
            where TAttrib : Attribute
        {
            return (TAttrib) member.GetCustomAttributes( typeof (TAttrib), inherit ).FirstOrDefault();
        }
    }

#endif

#if !NET_CORE

    internal static partial class ReflectionExtensions
    {
        public static Type ResolveType( this Module module, TypeReference typeRef )
        {
            return module.ResolveType( typeRef.MetadataToken.ToInt32() );
        }

        public static FieldInfo ResolveField( this Module module, FieldReference fieldRef )
        {
            return module.ResolveField( fieldRef.MetadataToken.ToInt32() );
        }

        public static MethodBase ResolveMethod( this Module module, MethodReference methodRef )
        {
            return module.ResolveMethod( methodRef.MetadataToken.ToInt32() );
        }
    }

#endif

}
