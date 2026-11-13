using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Sandbox.Upgraders
{
	[UpgraderGroup( typeof( ReferenceTypeUpgraderGroup ) )]
	public partial class DelegateUpgrader : Hotload.InstanceUpgrader
	{
		public override bool ShouldProcessType( Type type )
		{
			return typeof( Delegate ).IsAssignableFrom( type );
		}

		private MethodInfo GetMatchingLambdaMethod( MethodInfo oldLambda, Type oldDeclaringType, LambdaMethodName oldName, Type newDeclaringType )
		{
			var oldScopeMethod = FindScopeMethod( oldDeclaringType, oldName.ScopeMethodName, oldName.ScopeMethodOrdinal );
			var newScopeMethod = GetNewInstance( oldScopeMethod );

			if ( newScopeMethod == null )
			{
				return null;
			}

			Type[] typeArgs = null;

			var captureMode = GetLambdaCaptureMode( oldLambda );

			if ( captureMode != LambdaCaptureMode.TargetInstance && oldLambda.DeclaringType!.IsConstructedGenericType )
			{
				typeArgs = oldLambda.DeclaringType.GetGenericArguments();

				for ( var i = 0; i < typeArgs.Length; ++i )
				{
					typeArgs[i] = GetNewType( typeArgs[i] );
				}
			}
			else
			{
				// Case 2

				foreach ( var method in newDeclaringType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
				{
					if ( IsMatchingLambdaMethod( newScopeMethod, oldName, method ) )
					{
						return method;
					}
				}
			}

			// Case 1 and 3

			foreach ( var nestedType in newDeclaringType.GetNestedTypes( BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
			{
				if ( !IsCompilerGenerated( nestedType ) ) continue;

				var displayClassType = nestedType;

				if ( typeArgs != null )
				{
					if ( !displayClassType.IsGenericTypeDefinition ) continue;
					if ( displayClassType.GetGenericArguments().Length != typeArgs.Length ) continue;

					try
					{
						displayClassType = displayClassType.MakeGenericType( typeArgs );
					}
					catch
					{
						continue;
					}
				}

				foreach ( var method in displayClassType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
				{
					if ( IsMatchingLambdaMethod( newScopeMethod, oldName, method ) )
					{
						return method;
					}
				}
			}

			return null;
		}

		public MethodInfo GetMatchingLambdaMethod( MethodInfo oldMethod )
		{
			if ( CachedUpgrader.TryGetCachedInstance( oldMethod, out var cached ) ) return (MethodInfo)cached;

			if ( !GetLambdaMethodInfo( oldMethod,
					out var oldDeclaringType, out _,
					out var oldName ) ) return null;

			var newDelaringType = GetNewType( oldDeclaringType );

			if ( newDelaringType == oldDeclaringType && !oldMethod.IsGenericMethod && !oldMethod.DeclaringType!.IsGenericType )
			{
				return oldMethod;
			}

			var newMethod = GetMatchingLambdaMethod( oldMethod, oldDeclaringType, oldName, newDelaringType );

			CachedUpgrader.AddCachedInstance( oldMethod, newMethod );
			return newMethod;
		}

		private bool TryUpgradeTarget( LambdaCaptureMode oldCaptureMode, LambdaCaptureMode newCaptureMode,
			MethodInfo oldMethod, MethodInfo newMethod,
			object oldTarget, out object newTarget )
		{
			if ( oldMethod == newMethod && (oldTarget == null || !IsSwappedType( oldTarget.GetType() )) )
			{
				newTarget = GetNewInstance( oldTarget );
				return true;
			}

			newTarget = null;

			switch (oldCaptureMode, newCaptureMode)
			{
				case (LambdaCaptureMode.Unknown, _ ):
				case (_, LambdaCaptureMode.Unknown ):
					return false;

				case (_, LambdaCaptureMode.None ):
					newTarget = GetCachedTarget( newMethod );
					return true;

				case (LambdaCaptureMode.None, LambdaCaptureMode.DisplayClass ):
				case (LambdaCaptureMode.None, LambdaCaptureMode.TargetInstance ):
					return false;

				case (LambdaCaptureMode.TargetInstance, LambdaCaptureMode.TargetInstance ):
				case (LambdaCaptureMode.DisplayClass, LambdaCaptureMode.DisplayClass ):
					newTarget = GetNewInstance( oldTarget );
					return true;

				case (LambdaCaptureMode.DisplayClass, LambdaCaptureMode.TargetInstance ):
					var oldThis = GetCapturedThis( oldTarget );
					if ( oldThis == null ) return false;

					newTarget = GetNewInstance( oldThis );
					return true;

				default:
					throw new NotImplementedException();
			}
		}

		public enum ErrorDelegateMessage
		{
			NoDeclaringType,
			NoMatchStatic,
			NoMatchLambda,
			NoRetroactiveCapture,
			LambdaSignatureChanged,
			TargetTypeRemoved
		}

		private static string GetErrorDelegateMessage( ErrorDelegateMessage message )
		{
			return message switch
			{
				ErrorDelegateMessage.NoDeclaringType => "Unable to upgrade delegate methods without declaring types.",
				ErrorDelegateMessage.NoMatchStatic => "Unable to find matching substitution for a static method.",
				ErrorDelegateMessage.NoMatchLambda => "Unable to find matching substitution for a lambda method.",
				ErrorDelegateMessage.NoRetroactiveCapture => "Unable to retrospectively capture values for a lambda method.",
				ErrorDelegateMessage.LambdaSignatureChanged => "Lambda signature has changed.",
				ErrorDelegateMessage.TargetTypeRemoved => "Delegate target instance type removed.",
				_ => throw new NotImplementedException()
			};
		}

		private object CreateErrorDelegate( Delegate oldDelegate, ErrorDelegateMessage message )
		{
			const string errorDelegateNamePrefix = "__error_delegate";

			var wasErrorDelegate = false;

			if ( oldDelegate.Method.DeclaringType == null && oldDelegate.Method.Name.StartsWith( errorDelegateNamePrefix ) )
			{
				var startIndex = oldDelegate.Method.Name.IndexOf( '<' );
				var endIndex = oldDelegate.Method.Name.IndexOf( '>' );
				var oldMessageName = oldDelegate.Method.Name.Substring( startIndex + 1, endIndex - startIndex - 1 );

				message = Enum.Parse<ErrorDelegateMessage>( oldMessageName );

				wasErrorDelegate = true;
			}

			var oldMethod = oldDelegate.GetMethodInfo();
			var messageText = GetErrorDelegateMessage( message );

			if ( !wasErrorDelegate )
			{
				Log( HotloadEntryType.Warning, $"{messageText}", oldMethod );
			}

			var newParamTypes = oldMethod.GetParameters()
				.Select( x => GetNewType( x.ParameterType ) )
				.ToArray();

			var newReturnType = GetNewType( oldMethod.ReturnType );
			var newDelegType = GetNewType( oldDelegate.GetType() );

			var errorMethod = new DynamicMethod( $"{errorDelegateNamePrefix}<{message}>", newReturnType, newParamTypes );
			var il = errorMethod.GetILGenerator();

			il.Emit( OpCodes.Ldstr, messageText );
			il.Emit( OpCodes.Newobj, typeof( NotImplementedException ).GetConstructor( new[] { typeof( string ) } ) );
			il.Emit( OpCodes.Throw );

			return errorMethod.CreateDelegate( newDelegType );
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			newInstance = null;

			if ( oldInstance is not Delegate oldDelegate )
				return false;

			var invocList = oldDelegate.GetInvocationList();

			if ( invocList.Length > 1 )
			{
				// If this is a multicast delegate, handle each inner delegate separately

				var newInvocList = invocList
					.Select( GetNewInstance )
					.ToArray();

				newInstance = Delegate.Combine( newInvocList );
				return true;
			}

			var oldMethod = oldDelegate.GetMethodInfo();

			if ( oldMethod.DeclaringType == null )
			{
				newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoDeclaringType );
				return true;
			}

			var oldTarget = oldDelegate.Target;

			MethodInfo newMethod;
			object newTarget;

			if ( !GetLambdaMethodInfo( oldMethod, out _, out _, out _ ) || !IsSwappedType( oldMethod.DeclaringType ) )
			{
				newMethod = GetNewInstance( oldMethod );
				newTarget = GetNewInstance( oldTarget );

				if ( newMethod == null )
				{
					newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoMatchStatic );
					return true;
				}
			}
			else
			{
				newMethod = GetMatchingLambdaMethod( oldMethod );

				if ( newMethod == null )
				{
					newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoMatchLambda );
					return true;
				}

				var oldCaptureMode = GetLambdaCaptureMode( oldMethod );
				var newCaptureMode = GetLambdaCaptureMode( newMethod );

				oldTarget = oldDelegate.Target;

				if ( !TryUpgradeTarget( oldCaptureMode, newCaptureMode,
						oldMethod, newMethod,
						oldTarget, out newTarget ) )
				{
					// TODO: maybe one day we can emit a new version of the old lambda

					newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoRetroactiveCapture );
					return true;
				}
			}

			if ( ReferenceEquals( newMethod, oldMethod ) && ReferenceEquals( newTarget, oldTarget ) )
			{
				newInstance = oldInstance;
				return true;
			}

			var oldDelegType = oldDelegate.GetType();
			var newDelegType = GetNewType( oldDelegType );

			if ( !newMethod.IsStatic )
			{
				// newTarget can be null despite newMethod's declaring type not being null.
				// This will happen if newTarget's type derived from the method's declaring type,
				// but that derived type got removed in this hotload.

				if ( newTarget is null )
				{
					newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.TargetTypeRemoved );
					return true;
				}

				var newTargetType = newTarget.GetType();

				if ( !newMethod.DeclaringType.IsAssignableFrom( newTargetType ) )
				{
					newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoMatchLambda );
					return true;
				}
			}
			else if ( newTarget != null )
			{
				newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoMatchStatic );
				return true;
			}

			try
			{
				newInstance = newTarget == null
					? newMethod.CreateDelegate( newDelegType )
					: newMethod.CreateDelegate( newDelegType, newTarget );
			}
			catch ( ArgumentException )
			{
				newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.LambdaSignatureChanged );
				return true;
			}

			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			if ( createdElsewhere )
			{
				// We can't do in-place delegate upgrades, we always want to make a new instance if
				// anything has changed

				return false;
			}

			AddCachedInstance( oldInstance, newInstance );

			return true;
		}
	}
}
