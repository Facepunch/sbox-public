class MaxPayne2Texture( string fileName ) : ResourceLoader<MaxPayne2Mount>
{
	public string FileName { get; set; } = fileName;

	protected override object Load()
	{
		var data = Host.GetFileBytes( FileName );
		if ( data is null )
			return null;

		return MaxPayneImage.Load( data );
	}
}
