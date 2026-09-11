class RawFileLoader( string fullPath ) : ResourceLoader
{
	protected override object Load() => File.ReadAllBytes( fullPath );
}
