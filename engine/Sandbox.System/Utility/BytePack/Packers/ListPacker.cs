using System.Collections;

namespace Sandbox;

internal partial class BytePack
{
	class ListPacker : Packer
	{
		internal override Identifier Header => Identifier.List;

		public override void Write( ref ByteStream bs, object obj )
		{
			if ( obj is not IList list ) throw new NotSupportedException();

			var et = obj.GetType().GenericTypeArguments[0];

			bs.Write( list.Count );

			// List<object> etc - each type needs a header
			if ( et == typeof( object ) )
			{
				bs.Write<byte>( 1 );

				for ( int i = 0; i < list.Count; i++ )
				{
					Serialize( ref bs, list[i] );
				}

				return;
			}

			var w = GetHandlerFor( et );

			// List<int> etc - value type - easy mode
			if ( SandboxedUnsafe.IsAcceptablePod( et ) )
			{
				bs.Write<byte>( 0 );
				w.WriteTypeIdentifier( ref bs, et );

				var array = Array.CreateInstance( et, list.Count );
				list.CopyTo( array, 0 );
				bs.WriteValueArray( array );
				return;
			}

			bs.Write<byte>( 2 );
			w.WriteTypeIdentifier( ref bs, et );

			for ( int i = 0; i < list.Count; i++ )
			{
				w.Serialize( ref bs, list[i] );
			}
		}

		public override object Read( ref ByteStream bs, int depth )
		{
			var len = bs.Read<int>();
			var type = bs.Read<byte>();

			// List<int> etc - value type - easy mode
			if ( type == 0 )
			{
				var handler = GetHandlerFor( ref bs );
				var array = Array.CreateInstance( handler.TargetType, len );
				bs.ReadValueArray( array );

				return Activator.CreateInstance( typeof( List<> ).MakeGenericType( handler.TargetType ), new object[] { array } );
			}

			// List<object> etc - each type needs a header, slow mode
			if ( type == 1 )
			{
				var list = new List<object>();

				for ( int i = 0; i < len; i++ )
				{
					list.Add( Deserialize( ref bs, depth + 1 ) );
				}

				return list;
			}

			// some type
			if ( type == 2 )
			{
				var handler = GetHandlerFor( ref bs );

				var list = (IList)Activator.CreateInstance( typeof( List<> ).MakeGenericType( handler.TargetType ) );

				for ( int i = 0; i < len; i++ )
				{
					list.Add( handler.Deserialize( ref bs, depth + 1 ) );
				}

				return list;
			}

			throw new NotSupportedException();
		}
	}

}
