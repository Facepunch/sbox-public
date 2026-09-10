namespace Sandbox.Network;

[Expose]
struct TargetedMessage
{
	public Guid SenderId { get; set; }
	public Guid TargetId { get; set; }
	public object Message { get; set; }
	public byte Flags { get; set; }
}

[Expose]
struct TargetedInternalMessage : BytePack.ISerializer
{
	public Guid SenderId { get; set; }
	public Guid TargetId { get; set; }
	public byte[] Data { get; set; }
	public byte Flags { get; set; }

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		return new TargetedInternalMessage
		{
			SenderId = bs.Read<Guid>(),
			TargetId = bs.Read<Guid>(),
			Data = bs.ReadArray<byte>(),
			Flags = bs.Read<byte>(),
		};
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		var msg = (TargetedInternalMessage)value;
		bs.Write( msg.SenderId );
		bs.Write( msg.TargetId );
		bs.WriteArray<byte>( msg.Data ?? [] );
		bs.Write( msg.Flags );
	}
}
