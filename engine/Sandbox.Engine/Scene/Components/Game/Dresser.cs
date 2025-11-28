using System.Threading;

namespace Sandbox;

/// <summary>
/// Allows easily dressing a citizen or human in clothing
/// </summary>
[Alias( "Sandbox.ApplyLocalClothing" )]
[Expose]
[Title( "Dresser" )]
[Category( "Game" )]
[Icon( "checkroom" )]
public sealed class Dresser : Component, Component.ExecuteInEditor
{
	public enum ClothingSource
	{
		/// <summary>
		/// Manually select the clothing to wear
		/// </summary>
		Manual,

		/// <summary>
		/// Dress according to the local user's avatar
		/// </summary>
		LocalUser,

		/// <summary>
		/// Dress according to the avatar of the network owner of this GameObject
		/// </summary>
		OwnerConnection
	}

	/// <summary>
	/// Where to get the clothing from
	/// </summary>
	[Property]
	public ClothingSource Source { get; set; }

	/// <summary>
	/// Who are we dressing? This should be the renderer of the body of a Citizen or Human
	/// </summary>
	[Property]
	public SkinnedModelRenderer BodyTarget { get; set; }

	/// <summary>
	/// Should we change the height too?
	/// </summary>
	[Property]
	public bool ApplyHeightScale { get; set; } = true;

	[Header( "Manual Attributes" )]
	[ShowIf( "Source", ClothingSource.Manual )]
	[Property, Range( 0, 1 )]
	[Change( nameof( OnManualChange ) )]
	[Sync]
	public float ManualHeight { get; set; } = 0.5f;

	[ShowIf( "Source", ClothingSource.Manual )]
	[Property, Range( 0, 1 )]
	[Change( nameof( OnManualChange ) )]
	[Sync]
	public float ManualTint { get; set; } = 0.5f;

	[ShowIf( "Source", ClothingSource.Manual )]
	[Property, Range( 0, 1 )]
	[Change( nameof( OnManualChange ) )]
	[Sync]
	public float ManualAge { get; set; } = 0.5f;

	[Header( "Manual Items" )]
	[ShowIf( "Source", ClothingSource.Manual )]
	[Property]
	public List<ClothingContainer.ClothingEntry> Clothing { get; set; }

	[ShowIf( "Source", ClothingSource.Manual )]
	[Property]
	public List<string> WorkshopItems { get; set; }

	protected override void OnStart()
	{
		if ( IsProxy )
			return;

		_ = Apply();
	}

	protected override void OnEnabled()
	{
		// if we're a proxy then height, age and tint are sent via
		// parameters on this component, so we need to apply them
		if ( IsProxy )
		{
			ApplyAttributes();
		}
	}

	async Task<Clothing> InstallWorkshopClothing( string ident, CancellationToken ct )
	{
		if ( string.IsNullOrEmpty( ident ) ) return default;

		var package = await Package.FetchAsync( ident, false );
		if ( package is null ) return default;
		if ( package.TypeName != "clothing" ) return default;
		if ( ct.IsCancellationRequested ) return default;

		var primaryAsset = package.PrimaryAsset;
		if ( string.IsNullOrWhiteSpace( primaryAsset ) ) return default;

		var fs = await package.MountAsync();
		if ( fs is null ) return default;
		if ( ct.IsCancellationRequested ) return default;

		// try to load it
		return ResourceLibrary.Get<Clothing>( primaryAsset );
	}

	CancellationTokenSource _cts;

	/// <summary>
	/// If we're dressing in an async way - stop it.
	/// </summary>
	public void CancelDressing()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = default;
	}

	async ValueTask<ClothingContainer> GetClothing( CancellationToken token )
	{
		if ( Source == ClothingSource.OwnerConnection )
		{
			var clothing = new ClothingContainer();

			if ( Network.Owner != null )
			{
				clothing.Deserialize( Network.Owner.GetUserData( "avatar" ) );
			}

			return clothing;
		}

		if ( Source == ClothingSource.LocalUser )
		{
			return ClothingContainer.CreateFromLocalUser();
		}

		if ( Source == ClothingSource.Manual )
		{
			var clothing = new ClothingContainer();
			clothing.AddRange( Clothing );
			clothing.Height = ManualHeight;
			clothing.Age = ManualAge;
			clothing.Tint = ManualTint;

			if ( WorkshopItems != null && WorkshopItems.Count > 0 )
			{
				var tasks = WorkshopItems.Select( x => InstallWorkshopClothing( x, token ) );

				foreach ( var task in tasks )
				{
					var c = await task;

					if ( c is null )
						continue;

					clothing.Add( c );
				}
			}

			clothing.Normalize();
			return clothing;
		}

		return null;
	}

	/// <summary>
	/// True if we're dressing, in an async way
	/// </summary>
	public bool IsDressing { get; private set; }

	[Button( "Clear Clothing" )]
	public void Clear()
	{
		CancelDressing();

		if ( !BodyTarget.IsValid() )
			return;

		var clothing = new ClothingContainer();
		clothing.Apply( BodyTarget );

		BodyTarget.PostAnimationUpdate();
	}

	[Button( "Apply Clothing" )]
	public async ValueTask Apply()
	{
		CancelDressing();

		if ( !BodyTarget.IsValid() )
			return;

		_cts = new CancellationTokenSource();
		var token = _cts.Token;

		IsDressing = true;

		try
		{
			var clothing = await GetClothing( token );
			if ( clothing is null )
				return;

			if ( !ApplyHeightScale )
			{
				clothing.Height = 1;
			}

			clothing.AddRange( Clothing );
			clothing.Normalize();

			await clothing.ApplyAsync( BodyTarget, token );

			ManualHeight = clothing.Height;
			ManualTint = clothing.Tint;
			ManualAge = clothing.Age;

			BodyTarget.PostAnimationUpdate();
		}
		finally
		{
			IsDressing = false;
		}
	}

	protected override void OnValidate()
	{
		if ( IsProxy )
			return;

		base.OnValidate();

		using var p = Scene.Push();

		if ( !BodyTarget.IsValid() )
		{
			BodyTarget = GetComponentInChildren<SkinnedModelRenderer>();
		}

		if ( Scene.IsEditor )
		{
			_ = Apply();
		}
	}

	/// <summary>
	/// Called when Height, Age or Tint is changed
	/// </summary>
	public void OnManualChange( float a, float b )
	{
		ApplyAttributes();
	}

	/// <summary>
	/// Applies Height, Age and Tint.
	/// </summary>
	void ApplyAttributes()
	{
		if ( BodyTarget is null )
			return;

		if ( ApplyHeightScale )
			BodyTarget.Set( "scale_height", ManualHeight.Remap( 0, 1, 0.8f, 1.2f, true ) );
		else
			BodyTarget.Set( "scale_height", 1 );

		foreach ( var c in BodyTarget.GetComponentsInChildren<SkinnedModelRenderer>() )
		{
			c.Attributes.Set( "skin_age", ManualAge );
			c.Attributes.Set( "skin_tint", ManualTint );
		}
	}
}
