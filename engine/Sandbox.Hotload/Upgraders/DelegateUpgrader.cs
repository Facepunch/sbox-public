using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Sandbox.Upgraders
{
	/// <summary>
	/// Rebinds live callbacks to the incoming methods and receivers during hotload.
	/// </summary>
	[UpgraderGroup( typeof( ReferenceTypeUpgraderGroup ) )]
	public partial class DelegateUpgrader : Hotload.InstanceUpgrader
	{
		/// <summary>
		/// Selects delegate types for callback migration.
		/// </summary>
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

			const BindingFlags methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			foreach ( var method in newDeclaringType.GetMethods( methods | BindingFlags.Static ) )
			{
				if ( IsMatchingLambdaMethod( newScopeMethod, oldName, method ) )
					return method;
			}

			Type[] typeArgs = null;
			if ( oldLambda.DeclaringType != oldDeclaringType && oldLambda.DeclaringType!.IsConstructedGenericType )
			{
				typeArgs = oldLambda.DeclaringType.GetGenericArguments().Select( GetNewType ).ToArray();
				if ( typeArgs.Any( x => x is null ) ) return null;
			}

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
					catch ( ArgumentException )
					{
						continue;
					}
				}

				foreach ( var method in displayClassType.GetMethods( methods ) )
				{
					if ( IsMatchingLambdaMethod( newScopeMethod, oldName, method ) )
					{
						return method;
					}
				}
			}

			return null;
		}

		/// <summary>
		/// Finds the incoming compiler-generated method for a lambda or local function.
		/// </summary>
		public MethodInfo GetMatchingLambdaMethod( MethodInfo oldMethod )
		{
			if ( CachedUpgrader.TryGetCachedInstance( oldMethod, out var cached ) ) return (MethodInfo)cached;

			if ( !GetLambdaMethodInfo( oldMethod,
					out var oldDeclaringType, out _,
					out var oldName ) ) return null;

			var newDeclaringType = GetNewType( oldDeclaringType );
			if ( newDeclaringType is null ) return null;

			if ( newDeclaringType == oldDeclaringType && !oldMethod.IsGenericMethod && !oldMethod.DeclaringType!.IsGenericType )
			{
				return oldMethod;
			}

			var newMethod = GetMatchingLambdaMethod( oldMethod, oldDeclaringType, oldName, newDeclaringType );
			if ( newMethod is { IsGenericMethodDefinition: true } && !oldMethod.ContainsGenericParameters )
			{
				// When a local stops capturing, Roslyn moves the enclosing method's type
				// arguments from its display class onto the static local function.
				var arguments = oldMethod.DeclaringType.GetGenericArguments().Concat( oldMethod.GetGenericArguments() )
					.Skip( newMethod.DeclaringType.GetGenericArguments().Length );
				newMethod = GetUpgrader<MemberInfoUpgrader>().BindGenericArguments( newMethod, arguments );
			}

			CachedUpgrader.AddCachedInstance( oldMethod, newMethod );
			return newMethod;
		}

		private bool TryUpgradeTarget( LambdaCaptureMode oldCaptureMode, LambdaCaptureMode newCaptureMode,
			Delegate oldDelegate, MethodInfo newMethod, out object newTarget )
		{
			var oldTarget = oldDelegate.Target;
			newTarget = null;
			if ( oldTarget is null )
			{
				// A delegate closed over null cannot provide newly captured state.
				return oldCaptureMode == newCaptureMode || newCaptureMode == LambdaCaptureMode.None;
			}

			switch (oldCaptureMode, newCaptureMode)
			{
				case (_, LambdaCaptureMode.None ):
					newTarget = GetCachedTarget( newMethod );
					return true;

				case (LambdaCaptureMode.TargetInstance, LambdaCaptureMode.TargetInstance ):
				case (LambdaCaptureMode.StaticLocalFunction, LambdaCaptureMode.StaticLocalFunction ):
					newTarget = GetNewInstance( oldTarget );
					return true;

				case (LambdaCaptureMode.DisplayClass, LambdaCaptureMode.DisplayClass ):
					if ( !HasRequiredCaptures( oldTarget, newMethod ) ) return false;

					newTarget = GetNewInstance( oldTarget );
					return true;

				case (LambdaCaptureMode.DisplayClass, LambdaCaptureMode.TargetInstance ):
					var oldThis = GetCapturedThis( oldTarget );
					if ( oldThis == null ) return false;

					newTarget = GetNewInstance( oldThis );
					return true;

				default:
					// A new capture has no value in the old closure.
					return false;
			}
		}

		/// <summary>
		/// Identifies why a delegate could not be retained across a hotload.
		/// </summary>
		public enum ErrorDelegateMessage
		{
			NoDeclaringType,
			NoMatchStatic,
			NoMatchLambda,
			NoRetroactiveCapture,
			LambdaSignatureChanged,
			TargetTypeRemoved,

			/// <summary>
			/// An upgrader failed while resolving the callback or migrating its target.
			/// </summary>
			UpgradeFailed
		}

		private static string GetErrorDelegateMessage( ErrorDelegateMessage message )
		{
			return message switch
			{
				ErrorDelegateMessage.NoDeclaringType => "Cannot retain the generated callback across this hotload.",
				ErrorDelegateMessage.NoMatchStatic => "Unable to find a replacement for the callback method.",
				ErrorDelegateMessage.NoMatchLambda => "Unable to find matching substitution for a lambda method.",
				ErrorDelegateMessage.NoRetroactiveCapture => "Unable to retrospectively capture values for a lambda method.",
				ErrorDelegateMessage.LambdaSignatureChanged => "Lambda signature has changed.",
				ErrorDelegateMessage.TargetTypeRemoved => "The delegate target is unavailable after hotload.",
				ErrorDelegateMessage.UpgradeFailed => "The callback could not be migrated. See the hotload log for the cause.",
				_ => throw new NotImplementedException()
			};
		}

		private const string DroppedCallbackName = "__dropped_runtime_callback";

		private Delegate CreateErrorDelegate( Delegate oldDelegate, ErrorDelegateMessage message )
		{
			const string errorDelegateNamePrefix = "__error_delegate<";

			var wasErrorDelegate = false;

			var oldMethod = oldDelegate.GetMethodInfo();
			var oldName = oldMethod.Name;
			if ( oldMethod is DynamicMethod && oldName.StartsWith( errorDelegateNamePrefix, StringComparison.Ordinal )
				&& oldName.EndsWith( '>' )
				&& Enum.TryParse<ErrorDelegateMessage>( oldName.AsSpan( errorDelegateNamePrefix.Length..^1 ), out var previous )
				&& Enum.IsDefined( previous ) )
			{
				message = previous;
				wasErrorDelegate = true;
			}

			var messageText = GetErrorDelegateMessage( message );

			var newDelegType = GetNewType( oldDelegate.GetType() );
			if ( newDelegType is null ) return null;

			// Open delegates take the receiver as an argument. Use the signature callers actually invoke.
			var invoke = newDelegType.GetMethod( "Invoke" );
			var newParamTypes = invoke.GetParameters().Select( parameter => parameter.ParameterType ).ToArray();
			var newReturnType = invoke.ReturnType;

			// Skip an unavailable action so the other handlers still run. A caller that needs a
			// result or a by-reference argument needs to see the binding failure.
			if ( newReturnType == typeof( void ) && newParamTypes.All( type => !type.IsByRef ) )
			{
				if ( oldMethod.Name != DroppedCallbackName )
				{
					Log( HotloadEntryType.Warning, $"Dropping a callback that can't call the new code: {messageText}", oldMethod );
				}

				var dropped = new DynamicMethod( DroppedCallbackName, newReturnType, newParamTypes );
				dropped.GetILGenerator().Emit( OpCodes.Ret );
				return dropped.CreateDelegate( newDelegType );
			}

			if ( !wasErrorDelegate )
			{
				Log( HotloadEntryType.Warning, $"{messageText}", oldMethod );
			}

			var errorMethod = new DynamicMethod( $"{errorDelegateNamePrefix}{message}>", newReturnType, newParamTypes );
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

#if HOTLOAD_NOCATCH
			newInstance = UpgradeCallback( oldDelegate );
#else
			try
			{
				newInstance = UpgradeCallback( oldDelegate );
			}
			catch ( Exception ex ) when ( ex is not OutOfMemoryException )
			{
				// Contain migration failures at the individual subscriber, before a field
				// upgrader would have to discard the entire multicast delegate.
				Log( ex, member: oldDelegate.Method );
				newInstance = CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.UpgradeFailed );
			}
#endif
			return true;
		}

		private Delegate UpgradeCallback( Delegate oldDelegate )
		{
			var oldMethod = oldDelegate.Method;
			var oldTarget = oldDelegate.Target;

			if ( oldMethod.DeclaringType is null && (oldMethod is not DynamicMethod generated || !CanKeepGeneratedMethod( generated )) )
			{
				return CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoDeclaringType );
			}

			var matchLambda = GetLambdaMethodInfo( oldMethod, out _, out var oldCaptureMode, out _ ) && IsSwappedType( oldMethod.DeclaringType );
			var newMethod = matchLambda ? GetMatchingLambdaMethod( oldMethod ) : GetNewInstance( oldMethod );
			var newCaptureMode = LambdaCaptureMode.Unknown;
			if ( newMethod is null || (matchLambda && !GetLambdaMethodInfo( newMethod, out _, out newCaptureMode, out _ )) )
			{
				return CreateErrorDelegate( oldDelegate, matchLambda ? ErrorDelegateMessage.NoMatchLambda : ErrorDelegateMessage.NoMatchStatic );
			}

			var oldDelegType = oldDelegate.GetType();
			var newDelegType = GetNewType( oldDelegType );
			if ( newDelegType is null ) return null;

			// A method that became static no longer needs the old receiver. Bind it using
			// only the caller's arguments, just as we do for a delegate that was already open.
			var bindOpen = (!oldMethod.IsStatic && newMethod.IsStatic) || (oldTarget is null && IsOpenBinding( oldDelegType, oldMethod ));
			object newTarget = null;
			if ( !bindOpen )
			{
				if ( !matchLambda )
				{
					newTarget = GetNewInstance( oldTarget );
				}
				else if ( !TryUpgradeTarget( oldCaptureMode, newCaptureMode, oldDelegate, newMethod, out newTarget ) )
				{
					return CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.NoRetroactiveCapture );
				}

				if ( oldTarget != null && newTarget == null )
				{
					return CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.TargetTypeRemoved );
				}
			}

			if ( ReferenceEquals( newMethod, oldMethod ) && ReferenceEquals( newTarget, oldTarget ) && newDelegType == oldDelegType )
			{
				return oldDelegate;
			}

			// A null target can mean an open delegate or one closed over null. The CLR
			// accepts both, so check that the caller still supplies the same arguments.
			if ( !bindOpen && newTarget is null && IsOpenBinding( newDelegType, newMethod ) )
			{
				return CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.LambdaSignatureChanged );
			}

			try
			{
				// The CLR checks the receiver and signature for both open and closed delegates.
				return bindOpen ? newMethod.CreateDelegate( newDelegType ) : newMethod.CreateDelegate( newDelegType, newTarget );
			}
			catch ( ArgumentException )
			{
				return CreateErrorDelegate( oldDelegate, ErrorDelegateMessage.LambdaSignatureChanged );
			}
		}

		private static bool IsOpenBinding( Type delegateType, MethodInfo method )
		{
			var argumentCount = method.GetParameters().Length + (method.IsStatic ? 0 : 1);
			return delegateType.GetMethod( "Invoke" ).GetParameters().Length == argumentCount;
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
