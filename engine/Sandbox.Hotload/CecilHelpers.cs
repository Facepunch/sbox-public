using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Rocks;
#if NET_3_5 || NET_4_5
using Mono.Cecil.Rocks;
#endif

namespace Sandbox
{
	partial class Hotload
	{
		private readonly Dictionary<Assembly, AssemblyDefinition> LoadedDefinitions = new();

		private void CloseAssemblyImages()
		{
			foreach ( var definition in LoadedDefinitions.Values )
			{
				definition.Dispose();
			}

			LoadedDefinitions.Clear();
		}

		private AssemblyDefinition GetAssemblyDefinition( Assembly asm )
		{
			if ( AssemblyResolver == null )
			{
				Log( HotloadEntryType.Warning, $"No assembly resolver provided" );
				return null;
			}

			if ( LoadedDefinitions.TryGetValue( asm, out var loaded ) ) return loaded;

			if ( AssemblyResolver is IAssemblyImageResolver images && images.OpenAssemblyImage( asm ) is { } exact )
			{
				LoadedDefinitions.Add( asm, exact );
				return exact;
			}

			var name = asm.GetName();
			var asmDef = AssemblyResolver.Resolve( new AssemblyNameReference( name.Name, name.Version ) );

			if ( asmDef == null )
			{
				Log( HotloadEntryType.Error, $"Unable to resolve assembly definition ({asm.FullName})" );
			}
			else if ( asmDef.Name.Version.CompareTo( name.Version ) < 0 )
			{
				Log( HotloadEntryType.Error, $"Resolved assembly definition older than requested (expected {asm.FullName}, resolved {asmDef.FullName})" );
			}

			return asmDef;
		}

		private TypeDefinition GetTypeDefinition( Type type )
		{
			if ( type.IsConstructedGenericType )
			{
				type = type.GetGenericTypeDefinition();
			}

			TypeDefinition typeDef;

			if ( type.IsNested )
			{
				var declTypeDef = GetTypeDefinition( type.DeclaringType );

				if ( declTypeDef == null )
				{
					return null;
				}

				typeDef = declTypeDef.NestedTypes.Single( x => x.Name == type.Name );
			}
			else
			{
				var asmDef = GetAssemblyDefinition( type.Assembly );

				if ( asmDef == null )
				{
					return null;
				}

				typeDef = asmDef.MainModule.GetType( type.FullName );
			}

			if ( typeDef == null )
			{
				Log( HotloadEntryType.Error, $"Unable to resolve type definition", type );
			}

			return typeDef;
		}

		internal static bool IsMatchingType( TypeReference typeRef, Type type )
		{
			if ( typeRef == null && type == null )
			{
				return true;
			}

			if ( typeRef == null || type == null )
			{
				return false;
			}

			if ( typeRef is RequiredModifierType reqModType )
			{
				typeRef = reqModType.ElementType;
			}

			if ( type.IsArray )
			{
				if ( typeRef is not ArrayType arrayType )
				{
					return false;
				}

				if ( type.GetArrayRank() != arrayType.Rank )
				{
					return false;
				}

				return IsMatchingType( arrayType.ElementType, type.GetElementType() );
			}

			if ( typeRef.IsArray )
			{
				return false;
			}

			if ( type.IsByRef )
			{
				if ( typeRef is not ByReferenceType byRefType )
				{
					return false;
				}

				return IsMatchingType( byRefType.ElementType, type.GetElementType() );
			}

			if ( typeRef.IsByReference )
			{
				return false;
			}

			if ( type.IsGenericParameter )
			{
				if ( typeRef is not GenericParameter genericParam )
				{
					return false;
				}

				if ( genericParam.Position != type.GenericParameterPosition )
				{
					return false;
				}

				// TODO: check constraints?

				if ( type.DeclaringMethod != null )
				{
					if ( type.DeclaringMethod.Name != genericParam.DeclaringMethod?.Name )
					{
						return false;
					}

					if ( genericParam.DeclaringMethod.DeclaringType.IsGenericInstance &&
						 (type.DeclaringMethod.DeclaringType?.IsGenericTypeDefinition ?? false) )
					{
						return IsMatchingType( genericParam.DeclaringMethod.DeclaringType.GetElementType(), type.DeclaringMethod.DeclaringType );
					}

					return IsMatchingType( genericParam.DeclaringMethod.DeclaringType, type.DeclaringMethod.DeclaringType );
				}

				if ( type.DeclaringType != null )
				{
					return IsMatchingType( genericParam.DeclaringType, type.DeclaringType );
				}

				return false;
			}

			if ( type.Name != typeRef.Name )
			{
				return false;
			}

			if ( type.DeclaringType != null )
			{
				if ( !IsMatchingType( typeRef.DeclaringType, type.DeclaringType ) )
				{
					return false;
				}
			}
			else if ( typeRef.DeclaringType != null )
			{
				return false;
			}
			else if ( (type.Namespace ?? string.Empty) != (typeRef.Namespace ?? string.Empty) )
			{
				return false;
			}

			if ( type.IsConstructedGenericType )
			{
				if ( typeRef is not GenericInstanceType genericInstanceType )
				{
					return false;
				}

				if ( type.GenericTypeArguments.Length != genericInstanceType.GenericArguments.Count )
				{
					return false;
				}

				for ( var i = 0; i < type.GenericTypeArguments.Length; ++i )
				{
					if ( !IsMatchingType( genericInstanceType.GenericArguments[i], type.GenericTypeArguments[i] ) )
					{
						return false;
					}
				}
			}
			else if ( typeRef.IsGenericInstance )
			{
				return false;
			}

			if ( type.IsGenericTypeDefinition )
			{
				if ( !typeRef.HasGenericParameters )
				{
					return false;
				}

				var typeParams = type.GetGenericArguments();

				if ( typeParams.Length != typeRef.GenericParameters.Count )
				{
					return false;
				}

				// TODO: Check constraints?

				return true;
			}

			return true;
		}

		internal static TypeReference ResolveGenericArguments( MethodReference methodRef, TypeReference typeRef )
		{
			if ( !typeRef.ContainsGenericParameter )
			{
				return typeRef;
			}

			if ( typeRef is GenericParameter genericParam )
			{
				if ( genericParam.DeclaringMethod != null && methodRef is GenericInstanceMethod genericMethodRef )
				{
					if ( genericMethodRef.GetElementMethod().FullName == genericParam.DeclaringMethod.FullName )
					{
						return genericMethodRef.GenericArguments[genericParam.Position];
					}
				}
				else if ( genericParam.DeclaringType != null )
				{
					var declTypeRef = methodRef.DeclaringType;

					while ( declTypeRef != null )
					{
						if ( declTypeRef is GenericInstanceType genericTypeRef )
						{
							if ( genericTypeRef.GetElementType().FullName == genericParam.DeclaringType.FullName )
							{
								return genericTypeRef.GenericArguments[genericParam.Position];
							}
						}

						declTypeRef = declTypeRef.DeclaringType;
					}
				}

				return typeRef;
			}

			if ( typeRef is ArrayType arrayType )
			{
				var elemType = ResolveGenericArguments( methodRef, arrayType.ElementType );
				return arrayType.Rank == 1
					? elemType.MakeArrayType()
					: elemType.MakeArrayType( arrayType.Rank );
			}

			if ( typeRef is ByReferenceType byRefType )
			{
				var elemType = ResolveGenericArguments( methodRef, byRefType.ElementType );
				return elemType.MakeByReferenceType();
			}

			if ( typeRef is GenericInstanceType genericInstType )
			{
				var elemType = ResolveGenericArguments( methodRef, genericInstType.ElementType );
				var typeArgs = genericInstType.GenericArguments
					.Select( x => ResolveGenericArguments( methodRef, x ) )
					.ToArray();

				return elemType.MakeGenericInstanceType( typeArgs );
			}

			return typeRef;
		}

		private MethodDefinition GetMethodDefinition( MethodBase method )
		{
			if ( method is MethodInfo { IsConstructedGenericMethod: true } methodInfo )
			{
				method = methodInfo.GetGenericMethodDefinition();
			}

			var typeDef = GetTypeDefinition( method.DeclaringType );

			if ( typeDef == null )
			{
				return null;
			}

			// A method on a closed generic type has substituted parameter types. Its
			// metadata token still identifies the definition in the exact loaded image.
			if ( method.DeclaringType.IsConstructedGenericType && method.Module.ModuleVersionId == typeDef.Module.Mvid )
			{
				return typeDef.Methods.FirstOrDefault( definition => definition.MetadataToken.ToInt32() == method.MetadataToken );
			}

			var parameters = method.GetParameters();

			foreach ( var methodDef in typeDef.Methods )
			{
				if ( methodDef.Name != method.Name ) continue;
				if ( methodDef.Parameters.Count != parameters.Length ) continue;

				var isMatch = true;

				for ( var i = 0; i < parameters.Length; ++i )
				{
					if ( !IsMatchingType( ResolveGenericArguments( methodDef, methodDef.Parameters[i].ParameterType ), parameters[i].ParameterType ) )
					{
						isMatch = false;
						break;
					}
				}

				if ( isMatch )
				{
					return methodDef;
				}
			}

			Log( HotloadEntryType.Warning, $"Unable to resolve method definition", method );
			return null;
		}
	}
}
