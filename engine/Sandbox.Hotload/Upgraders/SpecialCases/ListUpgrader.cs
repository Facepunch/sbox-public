using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Sandbox.Upgraders.SpecialCases
{
	/// <summary>
	/// Upgrader for <see cref="List{T}"/> so that we only process its live elements.
	/// Without this, hotload would process the whole inner array, even if the list was cleared.
	/// </summary>
	[UpgraderGroup( typeof( CollectionsUpgraderGroup ) )]
	internal class ListUpgrader : Hotload.InstanceUpgrader
	{
		private static readonly Type Type = typeof( List<> );

		private Dictionary<Type, FieldInfo> ItemsFields { get; } = new Dictionary<Type, FieldInfo>();
		private Dictionary<Type, FieldInfo> SizeFields { get; } = new Dictionary<Type, FieldInfo>();

		protected ArrayUpgrader ArrayUpgrader { get; private set; }

		protected override void OnInitialize()
		{
			base.OnInitialize();

			ArrayUpgrader = GetUpgrader<ArrayUpgrader>();
		}

		protected override void OnClearCache()
		{
			ItemsFields.Clear();
			SizeFields.Clear();
		}

		public override bool ShouldProcessType( Type type )
		{
			return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == Type;
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			var oldType = oldInstance.GetType();
			var newType = GetNewType( oldType );

			var oldList = (IList)oldInstance;

			newInstance = oldType == newType
				? oldInstance
				: Activator.CreateInstance( newType, oldList.Count );

			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			AddCachedInstance( oldInstance, newInstance );
			SuppressFinalize( oldInstance, newInstance );

			ScheduleProcessInstance( oldInstance, newInstance );

			return true;
		}

		private Array GetArray( IList list )
		{
			var type = list.GetType();

			if ( !ItemsFields.TryGetValue( type, out var fieldInfo ) )
			{
				ItemsFields[type] = fieldInfo = type.GetField( "_items", BindingFlags.Instance | BindingFlags.NonPublic );
			}

			return (Array)fieldInfo!.GetValue( list );
		}

		private void SetCount( IList list, int value )
		{
			var type = list.GetType();

			if ( !SizeFields.TryGetValue( type, out var fieldInfo ) )
			{
				SizeFields[type] = fieldInfo = type.GetField( "_size", BindingFlags.Instance | BindingFlags.NonPublic );
			}

			fieldInfo!.SetValue( list, value );
		}

		protected override int OnProcessInstance( object oldInstance, object newInstance )
		{
			var oldList = (IList)oldInstance;
			var newList = (IList)newInstance;

			var oldPath = CurrentPath;

			if ( TracePaths )
			{
				CurrentPath = oldPath[typeof( int )];
			}

			try
			{
				var oldElemType = oldList.GetType().GetGenericArguments()[0];

				if ( ReferenceEquals( oldList, newList ) && ArrayUpgrader.CanSkipType( oldElemType ) )
				{
					return 1;
				}

				var newElemType = newList.GetType().GetGenericArguments()[0];

				if ( ArrayUpgrader.CanBlockCopy( oldElemType, newElemType ) )
				{
					var oldArray = GetArray( oldList );
					var newArray = GetArray( newList );

					SetCount( newList, oldList.Count );

					ArrayUpgrader.BlockCopyStructArray( oldArray, newArray, oldElemType, newElemType, oldList.Count );
					return 1;
				}

				if ( ReferenceEquals( oldList, newList ) )
				{
					for ( var i = 0; i < oldList.Count; i++ )
					{
						var item = oldList[i];
						var upgraded = GetNewInstance( item );

						// Writing the same reference back invalidates active list enumerators.
						if ( oldElemType.IsValueType || !ReferenceEquals( item, upgraded ) )
						{
							newList[i] = upgraded;
						}
					}
				}
				else
				{
					newList.Clear();

					foreach ( var item in oldList )
					{
						newList.Add( GetNewInstance( item ) );
					}
				}

				return 1;
			}
			finally
			{
				CurrentPath = oldPath;
			}
		}
	}
}
