using Sandbox.Diagnostics;

namespace Sandbox;

internal partial class BytePack
{
	/// <summary>
	/// An object[] array. More expensive than a Value array because each type has to write its type
	/// </summary>
	public class ObjectArrayPacker : Packer
	{
		internal override Identifier Header => Identifier.Array;

		public override void Write( ref ByteStream bs, object obj )
		{
			if ( obj is not Array array ) throw new NotSupportedException();
			var et = obj.GetType().GetElementType();

			bs.Write( array.Length );

			if ( et == typeof( object ) )
			{
				bs.Write<byte>( 1 );

				for ( int i = 0; i < array.Length; i++ )
				{
					Serialize( ref bs, array.GetValue( i ) );
				}

				return;
			}

			bs.Write<byte>( 2 );
			var h = GetHandlerFor( et );
			Assert.NotNull( h, $"Couldn't find or create handler for '{et}'" );

			h.WriteTypeIdentifier( ref bs, et );

			for ( int i = 0; i < array.Length; i++ )
			{
				h.Serialize( ref bs, array.GetValue( i ) );
			}
		}

		public override object Read( ref ByteStream bs, int depth )
		{
			var len = bs.Read<int>();
			var type = bs.Read<byte>();

			if ( type == 1 )
			{
				var array = new object[len];

				for ( int i = 0; i < len; i++ )
				{
					array.SetValue( Deserialize( ref bs, depth + 1 ), i );
				}

				return array;
			}

			if ( type == 2 )
			{
				var handler = GetHandlerFor( ref bs );

				var array = Array.CreateInstance( handler.TargetType, len );

				for ( int i = 0; i < len; i++ )
				{
					array.SetValue( handler.Deserialize( ref bs, depth + 1 ), i );
				}

				return array;
			}

			throw new NotImplementedException();
		}
	}
}
