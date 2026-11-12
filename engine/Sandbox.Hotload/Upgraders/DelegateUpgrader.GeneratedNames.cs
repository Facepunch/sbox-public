
using Mono.Cecil;
using System;
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
			DisplayClass = 3
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

		private static LambdaCaptureMode GetLambdaCaptureMode( MethodInfo lambda )
		{
			if ( lambda.DeclaringType == null || !IsCompilerGenerated( lambda ) )
			{
				return LambdaCaptureMode.Unknown;
			}

			if ( !IsCompilerGenerated( lambda.DeclaringType ) )
			{
				return LambdaCaptureMode.TargetInstance;
			}

			if ( NonCapturingDisplayClassName.IsMatch( lambda.DeclaringType.Name ) )
			{
				return LambdaCaptureMode.None;
			}

			return LambdaCaptureMode.DisplayClass;
		}

		internal record struct LambdaMethodName( string ScopeMethodName,
			int ScopeMethodOrdinal,
			int LambdaMethodOrdinal,
			int DisplayClassOrdinal );

		internal static bool GetLambdaMethodInfo( MethodInfo lambda, out Type declaringType,
			out LambdaCaptureMode captureMode, out LambdaMethodName nameInfo )
		{
			declaringType = null;
			captureMode = LambdaCaptureMode.Unknown;
			nameInfo = default;

			if ( !IsCompilerGenerated( lambda ) )
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

					if ( lambda.DeclaringType.GenericTypeArguments.Length != declaringType.GetGenericArguments().Length )
					{
						break;
					}

					declaringType = declaringType.MakeGenericType( lambda.DeclaringType.GenericTypeArguments );

					break;

				case LambdaCaptureMode.TargetInstance:
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
					displayClassName.Ordinal2 );

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

			captureMode = nonCapturingDisplayClass || isStatic
				? LambdaCaptureMode.None
				: LambdaCaptureMode.TargetInstance;

			Assert.True( methodName.Ordinal1 >= 0 );
			Assert.True( methodName.Ordinal2 >= 0 );

			nameInfo = new LambdaMethodName( methodName.ScopeName,
				methodName.Ordinal1,
				methodName.Ordinal2,
				-1 );

			return true;
		}

		private object GetCachedTarget( MethodInfo lambda )
		{
			Assert.True( NonCapturingDisplayClassName.IsMatch( lambda.DeclaringType!.Name ) );

			foreach ( var field in lambda.DeclaringType.GetFields( BindingFlags.Public | BindingFlags.Static ) )
			{
				if ( field.FieldType != lambda.DeclaringType ) continue;

				var target = field.GetValue( null );

				Assert.NotNull( target );

				return target;
			}

			Log( HotloadEntryType.Error, $"Unable to get cached instance of lambda target.", lambda.DeclaringType );
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

		private bool IsMatchingLambdaMethod( MethodBase newScopeMethod, LambdaMethodName oldName, MethodInfo newMethod )
		{
			if ( !GetLambdaMethodInfo( newMethod, out var newDeclaringType, out _, out var newName ) )
			{
				return false;
			}

			if ( newName.ScopeMethodName != oldName.ScopeMethodName ||
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
