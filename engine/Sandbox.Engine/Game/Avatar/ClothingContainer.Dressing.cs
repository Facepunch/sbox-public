using System.Threading;

namespace Sandbox;

public partial class ClothingContainer
{
	/// <summary>
	/// Dresses a body with this outfit and appearance, downloading missing clothing.
	/// </summary>
	[Obsolete( "Use Dresser.UpdateAppearance and Dresser.ApplyAsync instead." )]
	public async Task ApplyAsync( SkinnedModelRenderer body, CancellationToken token )
	{
		token.ThrowIfCancellationRequested();
		if ( !body.IsValid() )
			return;

		var dresser = Dresser.GetOrCreate( body );
		Normalize();
		dresser.UpdateAppearance( this );
		await dresser.ApplyAsync( this, token );
	}

	/// <summary>
	/// Dresses a body with this outfit and appearance without downloading missing clothing.
	/// </summary>
	[Obsolete( "Use Dresser.UpdateAppearance and Dresser.Apply instead." )]
	public void Apply( SkinnedModelRenderer body )
	{
		if ( !body.IsValid() )
			return;

		var dresser = Dresser.GetOrCreate( body );
		Normalize();
		dresser.UpdateAppearance( this );
		dresser.Apply( this );
	}

	/// <summary>
	/// Removes clothing and resets the body's height, material group and bodygroups.
	/// </summary>
	[Obsolete( "Use Dresser.Clear to remove clothing while preserving appearance." )]
	public void Reset( SkinnedModelRenderer body )
	{
		if ( !body.IsValid() )
			return;

		Dresser.Reset( body );
	}
}
