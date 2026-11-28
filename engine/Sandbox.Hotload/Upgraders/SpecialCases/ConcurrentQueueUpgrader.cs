using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Sandbox.Upgraders.SpecialCases
{
	/// <summary>
	/// Custom handling for <see cref="ConcurrentQueue{T}"/> to reduce hotload processing time. A queue whose type wasn't
	/// swapped stays the same queue, its items upgraded in place: other code can hold it besides the field hotload
	/// reaches it through (the runtime's socket engine keeps its event queue on its event loop thread, and enqueued into
	/// the old one for good once the field was given a copy, so no socket in the process heard anything again).
	/// </summary>
	[UpgraderGroup( typeof( CollectionsUpgraderGroup ) )]
	internal class ConcurrentQueueUpgrader : Hotload.InstanceUpgrader
	{
		public override bool ShouldProcessType( Type type )
		{
			return type.IsGenericType && type.GetGenericTypeDefinition() == typeof( ConcurrentQueue<> );
		}

		protected override bool OnTryCreateNewInstance( object oldInstance, out object newInstance )
		{
			var oldType = oldInstance.GetType();
			var newType = GetNewType( oldType );

			newInstance = oldType == newType ? oldInstance : Activator.CreateInstance( newType );

			return true;
		}

		protected override bool OnTryUpgradeInstance( object oldInstance, object newInstance, bool createdElsewhere )
		{
			AddCachedInstance( oldInstance, newInstance );
			SuppressFinalize( oldInstance, newInstance );
			ScheduleProcessInstance( oldInstance, newInstance );

			return true;
		}

		protected override int OnProcessInstance( object oldInstance, object newInstance )
		{
			var oldType = oldInstance.GetType();
			var newType = newInstance.GetType();

			var elementType = newType.GetGenericArguments()[0];
			var paramTypes = new[] { elementType };
			var enqueueMethod = newType.GetMethod( nameof( ConcurrentQueue<object>.Enqueue ), paramTypes );
			var toArrayMethod = oldType.GetMethod( nameof( ConcurrentQueue<object>.ToArray ), Array.Empty<Type>() );

			var oldItems = (Array)toArrayMethod.Invoke( oldInstance, Array.Empty<object>() );
			var newItems = new object[oldItems.Length];
			var changed = false;

			for ( var i = 0; i < oldItems.Length; i++ )
			{
				var oldItem = oldItems.GetValue( i );
				var newItem = GetNewInstance( oldItem );

				newItems[i] = newItem;
				changed |= oldItem is ValueType ? !Equals( oldItem, newItem ) : !ReferenceEquals( oldItem, newItem );
			}

			if ( ReferenceEquals( oldInstance, newInstance ) )
			{
				// The same queue (its type wasn't swapped): left alone unless an item was upgraded, then refilled in place

				if ( !changed ) return 1;

				var clearMethod = newType.GetMethod( nameof( ConcurrentQueue<object>.Clear ), Array.Empty<Type>() );

				clearMethod.Invoke( newInstance, Array.Empty<object>() );
			}

			var args = new object[1];
			foreach ( var newItem in newItems )
			{
				args[0] = newItem;
				enqueueMethod.Invoke( newInstance, args );
			}

			return 1;
		}
	}
}
