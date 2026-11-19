using System;
using System.Linq;
using System.Reflection;

namespace Sandbox.Upgraders
{
	[UpgraderGroup( typeof( ReferenceTypeUpgraderGroup ) )]
	public class ReflectionUpgraderGroup : UpgraderGroup { }

	[UpgraderGroup( typeof( ReflectionUpgraderGroup ) )]
	internal class AssemblyUpgrader : Hotload.InstanceUpgrader
	{
		public override bool ShouldProcessType( Type type )
		{
			return typeof( Assembly ).IsAssignableFrom( type );
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			if ( oldInstance is Assembly oldAsm )
			{
				newInstance = Swaps.TryGetValue( oldAsm, out var swapAsm ) ? swapAsm : oldAsm;
				return true;
			}

			newInstance = oldInstance;
			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			AddCachedInstance( oldInstance, newInstance );

			return true;
		}
	}

	[UpgraderGroup( typeof( ReflectionUpgraderGroup ) )]
	internal class MemberInfoUpgrader : Hotload.InstanceUpgrader
	{
		public override bool ShouldProcessType( Type type )
		{
			return typeof( MemberInfo ).IsAssignableFrom( type );
		}

		private bool IsMatchingMember( MemberInfo oldMember, MemberInfo newMember )
		{
			if ( oldMember.Name != newMember.Name ) return false;
			if ( oldMember.GetType() != newMember.GetType() ) return false;
			if ( !(oldMember is MethodBase) ) return true;

			var oldMethod = (MethodBase)oldMember;
			var newMethod = (MethodBase)newMember;

			if ( oldMethod is MethodInfo { IsConstructedGenericMethod: true } genericInstMethod )
			{
				oldMethod = genericInstMethod.GetGenericMethodDefinition();
			}

			var oldParams = oldMethod.GetParameters();
			var newParams = newMethod.GetParameters();

			if ( oldParams.Length != newParams.Length ) return false;

			for ( var i = 0; i < oldParams.Length; ++i )
			{
				var oldParam = oldParams[i];
				var newParam = newParams[i];

				if ( AreEquivalentTypes( oldParam.ParameterType, newParam.ParameterType ) ) continue;

				return false;
			}

			if ( oldMethod.IsGenericMethodDefinition || oldMethod.IsConstructedGenericMethod )
			{
				return newMethod.IsGenericMethodDefinition;
			}

			return !newMethod.IsGenericMethodDefinition;
		}

		private MemberInfo GetMatchingMember( MemberInfo orig, Type newType )
		{
			if ( newType == null )
			{
				return null;
			}

			const BindingFlags bFlags = BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

			var match = newType.GetMembers( bFlags )
				.FirstOrDefault( x => IsMatchingMember( orig, x ) );

			if ( orig is MethodInfo { IsConstructedGenericMethod: true } oldMethod
				&& match is MethodInfo { IsGenericMethodDefinition: true } newMethod )
			{
				var newTypeArgs = oldMethod
					.GetGenericArguments()
					.Select( GetNewType )
					.ToArray();

				if ( newTypeArgs.Any( x => x == null ) )
				{
					return null;
				}

				match = newMethod.MakeGenericMethod( newTypeArgs );
			}

			return match;
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			if ( oldInstance is Type oldType )
			{
				newInstance = GetNewType( oldType );
				return true;
			}

			newInstance = oldInstance;

			var oldMember = (MemberInfo)oldInstance;

			if ( oldMember.DeclaringType == null )
				return true;

			var declaringType = GetNewType( oldMember.DeclaringType );
			if ( declaringType == oldMember.DeclaringType && oldMember is not MethodBase { IsConstructedGenericMethod: true } )
				return true;

			// Other generators also use angle brackets for helpers. Only C# lambda names need lambda matching.
			if ( oldMember is MethodInfo oldMethod && DelegateUpgrader.GetLambdaMethodInfo( oldMethod, out _, out _, out _ ) )
			{
				newInstance = GetUpgrader<DelegateUpgrader>().GetMatchingLambdaMethod( oldMethod );
				return true;
			}

			newInstance = GetMatchingMember( oldMember, declaringType );
			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			AddCachedInstance( oldInstance, newInstance );

			return true;
		}
	}

	[UpgraderGroup( typeof( ReflectionUpgraderGroup ) )]
	internal class ParameterInfoUpgrader : Hotload.InstanceUpgrader
	{
		public override bool ShouldProcessType( Type type )
		{
			return type.IsAssignableTo( typeof( ParameterInfo ) );
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			var oldParam = (ParameterInfo)oldInstance;
			var oldMember = oldParam.Member;

			var newMember = GetNewInstance( oldMember );

			if ( ReferenceEquals( oldMember, newMember ) )
			{
				newInstance = oldInstance;
				return true;
			}

			if ( newMember == null )
			{
				newInstance = null;
				return true;
			}

			if ( newMember is MethodBase method )
			{
				newInstance = method.GetParameters().FirstOrDefault( x => x.Name == oldParam.Name );
				return newInstance != null;
			}

			throw new NotImplementedException();
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			AddCachedInstance( oldInstance, newInstance );

			return true;
		}
	}
}
