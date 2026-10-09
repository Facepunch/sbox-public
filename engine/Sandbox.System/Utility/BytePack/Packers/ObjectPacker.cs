namespace Sandbox;

internal partial class BytePack
{
	/// <summary>
	/// Writes a type header and then the value
	/// </summary>
	public class ObjectPacker : Packer
	{
		public override Type TargetType => typeof( object );
		internal override Identifier Header => Identifier.Object;

		internal ObjectPacker()
		{

		}

		public override void Write( ref ByteStream bs, object obj )
		{
			Serialize( ref bs, obj );
		}

		public override object Read( ref ByteStream data, int depth )
		{
			return Deserialize( ref data, depth + 1 );
		}
	}
}
