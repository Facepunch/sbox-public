namespace Sandbox;

internal partial class BytePack
{
	/// <summary>
	/// Plain old data types. It's faster to have these as identifier types than
	/// have them each being a TypeLibrary lookup dynamic type, so anything quite
	/// common should be included here.
	/// </summary>
	public class PodPacker<T> : Packer where T : unmanaged
	{
		private Identifier header;

		public override Type TargetType => typeof( T );
		internal override Identifier Header => header;

		internal PodPacker( Identifier header )
		{
			this.header = header;
		}

		public override void Write( ref ByteStream bs, object obj )
		{
			bs.Write<T>( (T)obj );
		}

		public override object Read( ref ByteStream data, int depth )
		{
			return data.Read<T>();
		}
	}


	void InstallPodCommon()
	{
		// native types
		Add( new PodPacker<bool>( Identifier.Bool ) );
		Add( new PodPacker<byte>( Identifier.Byte ) );
		Add( new PodPacker<char>( Identifier.Char ) );
		Add( new PodPacker<sbyte>( Identifier.SByte ) );
		Add( new PodPacker<short>( Identifier.Short ) );
		Add( new PodPacker<ushort>( Identifier.UShort ) );
		Add( new PodPacker<int>( Identifier.Int ) );
		Add( new PodPacker<uint>( Identifier.UInt ) );
		Add( new PodPacker<float>( Identifier.Float ) );
		Add( new PodPacker<double>( Identifier.Double ) );
		Add( new PodPacker<decimal>( Identifier.Decimal ) );
		Add( new PodPacker<long>( Identifier.Long ) );
		Add( new PodPacker<ulong>( Identifier.ULong ) );
		Add( new PodPacker<Guid>( Identifier.Guid ) );
		Add( new PodPacker<TimeSpan>( Identifier.TimeSpan ) );
		Add( new PodPacker<DateTime>( Identifier.DateTime ) );
		Add( new PodPacker<DateTimeOffset>( Identifier.DateTimeOffset ) );

		Add( new PodPacker<Vector2>( Identifier.Vector2 ) );
		Add( new PodPacker<Vector2Int>( Identifier.Vector2Int ) );
		Add( new PodPacker<Vector3>( Identifier.Vector3 ) );
		Add( new PodPacker<Vector3Int>( Identifier.Vector3Int ) );
		Add( new PodPacker<Vector4>( Identifier.Vector4 ) );
		Add( new PodPacker<Rotation>( Identifier.Rotation ) );
		Add( new PodPacker<Angles>( Identifier.Angles ) );
		Add( new PodPacker<Transform>( Identifier.Transform ) );
		Add( new PodPacker<Color>( Identifier.Color ) );
		Add( new PodPacker<Color32>( Identifier.Color32 ) );
		Add( new PodPacker<BBox>( Identifier.BBox ) );
		Add( new PodPacker<Sphere>( Identifier.Sphere ) );
		Add( new PodPacker<Plane>( Identifier.Plane ) );
		Add( new PodPacker<Rect>( Identifier.Rect ) );
		Add( new PodPacker<Ray>( Identifier.Ray ) );
		Add( new PodPacker<Line>( Identifier.Line ) );
		Add( new PodPacker<Matrix>( Identifier.Matrix ) );
		Add( new PodPacker<SteamId>( Identifier.SteamId ) );
	}
}
