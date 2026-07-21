
namespace SeriousEngine3;

public class SoundLoader( string name ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		return SoundFile.FromWav( Path, Host.GetBytes( name ) );
	}
}
