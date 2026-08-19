/// <summary>
/// Returns a file from the mounted game as bytes. Only paths on GameMount's allowlist reach this.
/// </summary>
class RawFileLoader( string fullPath ) : ResourceLoader<GameMount>
{
	protected override object Load() => File.ReadAllBytes( fullPath );
}
