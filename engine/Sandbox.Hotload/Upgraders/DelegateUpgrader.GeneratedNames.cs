
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Sandbox.Upgraders
{
	partial class DelegateUpgrader
	{
		internal enum LambdaCaptureMode
		{
			Unknown = 0,

			/// <summary>
			/// The lambda captures nothing local.
			/// Method is declared in a nested "&lt;&gt;c" class in the context's declaring type.
			/// Method is named like "&lt;{ContextName:ident}&gt;b__{ContextIndex:int}_{LambdaIndex:int}".
			/// </summary>
			None = 1,

			/// <summary>
			/// The lambda captures only an instance of the context's declaring type.
			/// Method is declared in the context's declaring type itself.
			/// Method is named like "&lt;{ContextName:ident}&gt;b__{ContextIndex:int}_{LambdaIndex:int}".
			/// </summary>
			TargetInstance = 2,

			/// <summary>
			/// The lambda captures other local values from the context.
			/// Method is declared in a nested "&lt;&gt;__DisplayClass{ContextIndex:int}_{DisplayClassIndex:int}" class.
			/// Method is named like "&lt;{ContextName:ident}&gt;b__{LambdaIndex:int}".
			/// </summary>
			DisplayClass = 3,

			/// <summary>
			/// A static local function emitted directly on the type containing the scope method.
			/// </summary>
			StaticLocalFunction
		}

		public static bool IsCompilerGenerated( string methodName )
		{
			return methodName.StartsWith( "<" );
		}

		public static bool IsCompilerGenerated( MethodReference methodRef )
		{
			return IsCompilerGenerated( methodRef.Name );
		}

		public static bool IsCompilerGenerated( MethodInfo method )
		{
			return IsCompilerGenerated( method.Name );
		}

		public static bool IsCompilerGenerated( Type type )
		{
			return type.GetTypeInfo().GetCustomAttribute<CompilerGeneratedAttribute>() != null;
		}

		private static Regex NonCapturingDisplayClassName { get; } =
			new( @"^<>c(?:__(?<smo>[0-9]+)(?:`(?<argCount>[0-9]+)|<(?<args>[^>]+)>))?$" );

		internal record struct LambdaMethodName( string ScopeMethodName,
			int ScopeMethodOrdinal,
			int LambdaMethodOrdinal,
			string LocalFunctionName );

		internal static bool GetLambdaMethodInfo( MethodInfo lambda, out Type declaringType,
			out LambdaCaptureMode captureMode, out LambdaMethodName nameInfo )
		{
			declaringType = null;
			captureMode = LambdaCaptureMode.Unknown;
			nameInfo = default;

			if ( lambda.DeclaringType is null || !IsCompilerGenerated( lambda ) )
			{
				return false;
			}

			if ( !GetLambdaMethodInfo( lambda.Name, lambda.DeclaringType.Name, lambda.IsStatic, out captureMode, out nameInfo ) )
			{
				return false;
			}

			switch ( captureMode )
			{
				case LambdaCaptureMode.DisplayClass:
				case LambdaCaptureMode.None:
					declaringType = lambda.DeclaringType.DeclaringType;

					if ( !lambda.DeclaringType.IsConstructedGenericType || !(declaringType?.IsGenericTypeDefinition ?? false) )
					{
						break;
					}

					// If the scope method is in a generic declaring type, the display class
					// for this lambda is generic with its own copy of the type parameters.

					var ownerArity = declaringType.GetGenericArguments().Length;
					declaringType = declaringType.MakeGenericType( lambda.DeclaringType.GenericTypeArguments[..ownerArity] );

					break;

				case LambdaCaptureMode.TargetInstance:
				case LambdaCaptureMode.StaticLocalFunction:
					declaringType = lambda.DeclaringType;
					break;

				default:
					throw new NotImplementedException();
			}

			return true;
		}

		internal static bool GetLambdaMethodInfo( MethodReference lambdaRef, out TypeReference declaringTypeRef,
			out LambdaCaptureMode captureMode, out LambdaMethodName nameInfo )
		{
			declaringTypeRef = null;
			captureMode = LambdaCaptureMode.Unknown;
			nameInfo = default;

			if ( !IsCompilerGenerated( lambdaRef ) )
			{
				return false;
			}

			if ( !GetLambdaMethodInfo( lambdaRef.Name, lambdaRef.DeclaringType.Name, !lambdaRef.HasThis, out captureMode, out nameInfo ) )
			{
				return false;
			}

			switch ( captureMode )
			{
				case LambdaCaptureMode.DisplayClass:
				case LambdaCaptureMode.None:
					declaringTypeRef = lambdaRef.DeclaringType.DeclaringType;
					break;

				case LambdaCaptureMode.TargetInstance:
				case LambdaCaptureMode.StaticLocalFunction:
					declaringTypeRef = lambdaRef.DeclaringType;
					break;

				default:
					throw new NotImplementedException();
			}

			return true;
		}

		internal static bool GetLambdaMethodInfo( string lambdaName, string declaringTypeName, bool isStatic,
			out LambdaCaptureMode captureMode, out LambdaMethodName nameInfo )
		{
			captureMode = LambdaCaptureMode.Unknown;
			nameInfo = default;

			if ( declaringTypeName == null )
			{
				return false;
			}

			if ( !IsCompilerGenerated( lambdaName ) )
			{
				return false;
			}

			GeneratedName methodName;

			var nonCapturingDisplayClass = NonCapturingDisplayClassName.IsMatch( declaringTypeName );

			// Case 3

			if ( !nonCapturingDisplayClass
				&& GeneratedName.TryParse( declaringTypeName, out var displayClassName )
				&& displayClassName.Suffix == "DisplayClass" )
			{
				Assert.AreEqual( GeneratedNameKind.LambdaDisplayClass, displayClassName.Kind );
				Assert.True( displayClassName.Ordinal1 >= 0 );
				Assert.True( displayClassName.Ordinal2 >= 0 );

				if ( !GeneratedName.TryParse( lambdaName, out methodName ) )
				{
					return false;
				}

				Assert.True( methodName.Kind == GeneratedNameKind.LambdaMethod || methodName.Kind == GeneratedNameKind.LocalFunction );
				Assert.True( methodName.Ordinal1 >= 0 );
				Assert.AreEqual( -1, methodName.Ordinal2 );

				captureMode = LambdaCaptureMode.DisplayClass;

				nameInfo = new LambdaMethodName( methodName.ScopeName,
					displayClassName.Ordinal1,
					methodName.Ordinal1,
					methodName.Suffix );

				return true;
			}

			// Case 1 or 2

			if ( !GeneratedName.TryParse( lambdaName, out methodName ) )
			{
				return false;
			}

			switch ( methodName.Kind )
			{
				case GeneratedNameKind.LambdaMethod:
					Assert.True( nonCapturingDisplayClass || !isStatic );
					break;

				case GeneratedNameKind.LocalFunction:
					break;

				default:
					return false;
			}

			captureMode = nonCapturingDisplayClass
				? LambdaCaptureMode.None
				: isStatic ? LambdaCaptureMode.StaticLocalFunction : LambdaCaptureMode.TargetInstance;

			Assert.True( methodName.Ordinal1 >= 0 );
			Assert.True( methodName.Ordinal2 >= 0 );

			nameInfo = new LambdaMethodName( methodName.ScopeName,
				methodName.Ordinal1,
				methodName.Ordinal2,
				methodName.Suffix );

			return true;
		}

		private static object GetCachedTarget( MethodInfo lambda )
		{
			Assert.True( NonCapturingDisplayClassName.IsMatch( lambda.DeclaringType!.Name ) );

			foreach ( var field in lambda.DeclaringType.GetFields( BindingFlags.Public | BindingFlags.Static ) )
			{
				if ( field.FieldType != lambda.DeclaringType ) continue;

				return field.GetValue( null );
			}

			return null;
		}

		private object GetCapturedThis( object displayClassInst )
		{
			Assert.NotNull( displayClassInst );

			var displayClassType = displayClassInst.GetType();

			Assert.True( displayClassType.Name.StartsWith( "<>c__DisplayClass" ) );

			var thisType = displayClassType.DeclaringType;

			foreach ( var field in displayClassType.GetFields( BindingFlags.Instance | BindingFlags.Public ) )
			{
				if ( field.FieldType != thisType ) continue;
				if ( field.Name.EndsWith( "__this" ) )
				{
					return field.GetValue( displayClassInst );
				}
			}

			return null;
		}

		private IEnumerable<string[]> GetUnavailableCaptures( object oldClosure, Type newClosure )
		{
			var fields = DefaultUpgrader.GetFieldsToProcess( oldClosure.GetType(), newClosure, canSkip: false );
			foreach ( var (source, destination) in fields )
			{
				if ( source is null )
				{
					yield return [destination.Name];
					continue;
				}

				// A base-typed capture can hold a removed instance. A null that was actually
				// captured is still available to the incoming callback.
				if ( source.FieldType.IsValueType || source.GetValue( oldClosure ) is not { } value ) continue;

				if ( GetNewType( value.GetType() ) is null )
				{
					yield return [destination.Name];
				}
				else if ( destination.FieldType.Name.StartsWith( "<>c__DisplayClass", StringComparison.Ordinal ) )
				{
					foreach ( var path in GetUnavailableCaptures( value, destination.FieldType ) )
					{
						yield return [destination.Name, .. path];
					}
				}
			}
		}

		/// <summary>
		/// A direct field load can select an available sibling. If the incomplete closure is
		/// used as a whole, the callback may still depend on any field below it.
		/// </summary>
		private static bool UsesUnavailableCapture( Instruction instruction, string[] path )
		{
			foreach ( var name in path )
			{
				var field = (FieldReference)instruction.Operand;
				if ( field.Name != name )
					return false;

				if ( instruction.OpCode.Code != Code.Ldfld
					|| instruction.Next?.OpCode.Code is not (Code.Ldfld or Code.Ldflda) )
					return true;

				instruction = instruction.Next;
			}

			return true;
		}

		private bool HasRequiredCaptures( object oldClosure, MethodInfo newMethod )
		{
			var missing = GetUnavailableCaptures( oldClosure, newMethod.DeclaringType ).ToArray();
			if ( missing.Length == 0 ) return true;

			// Several subscribers can share a display class. A new field only matters to a
			// callback that uses it or passes the incomplete closure to another method.
			var method = GetMethodDefinition( newMethod );
			if ( method?.HasBody != true ) return false;

			bool IsClosure( TypeReference type ) => type.GetElementType().FullName == method.DeclaringType.FullName;
			foreach ( var instruction in method.Body.Instructions )
			{
				if ( instruction.Operand is FieldReference field && IsClosure( field.DeclaringType )
					&& missing.Any( path => UsesUnavailableCapture( instruction, path ) ) )
					return false;

				if ( instruction.Operand is MethodReference call && (IsClosure( call.DeclaringType ) || call.Parameters.Any( p => IsClosure( p.ParameterType ) )) )
					return false;
			}

			return true;
		}

		private bool IsMatchingLambdaMethod( MethodBase newScopeMethod, LambdaMethodName oldName, MethodInfo newMethod )
		{
			if ( !GetLambdaMethodInfo( newMethod, out var newDeclaringType, out _, out var newName ) )
			{
				return false;
			}

			if ( newName.ScopeMethodName != oldName.ScopeMethodName ||
				newName.LocalFunctionName != oldName.LocalFunctionName ||
				newName.LambdaMethodOrdinal != oldName.LambdaMethodOrdinal )
			{
				return false;
			}

			Assert.AreEqual( newDeclaringType, newScopeMethod.DeclaringType );
			Assert.AreEqual( oldName.ScopeMethodName, newScopeMethod.Name );

			var newScopeOrdinal = GetScopeMethodOrdinal( newScopeMethod );

			return newName.ScopeMethodOrdinal == newScopeOrdinal;
		}
	}
}
