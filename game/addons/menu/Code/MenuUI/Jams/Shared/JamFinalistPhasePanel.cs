using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Services;
using MenuProject.MenuUI.ContentBlock;

namespace MenuProject.MenuUI.Jams;

/// <summary>
/// Shared finalist selection and artwork loading for the knockout, final and results views.
/// Voting, round snapshots and cooldowns remain owned by GameJamSystem.
/// </summary>
public abstract class JamFinalistPhasePanel : JamPhasePanel
{
	protected GameJamSystem Voting => GameJamSystem.For( this );
	protected JamFinalistCategory[] Categories => Voting?.Finalists;
	protected JamFinalistCategory Category => Categories?.FirstOrDefault( x => x.Id == CategoryId ) ?? Categories?.FirstOrDefault();
	protected int CategoryId;
	protected bool Loading => Voting?.IsLoadingFinalists == true;
	protected bool Submitting => Voting?.IsSubmittingFinalistVote == true;
	protected string Error => Voting?.FinalistVoteError ?? Voting?.FinalistsError;
	protected bool VotingOpen => Category?.IsVotingAt( Jam.Now ) == true;
	protected int VoteCooldownSeconds => Category is null ? 0 : Voting.GetFinalistVoteCooldown( Category.Id );
	protected readonly Dictionary<string, Package> Packages = new( StringComparer.OrdinalIgnoreCase );
	Jam loadedJam;
	JamFinalistCategory[] loadedCategories;

	protected IReadOnlyList<KeyValuePair<string, int?>> StandingsEntries => Category?.Nominees
		.Select( x => new KeyValuePair<string, int?>( x.PackageIdent,
			x.PackageIdent is not null && Category.NominationCounts.TryGetValue( x.PackageIdent, out var count ) ? count : null ) ).ToArray() ?? [];
	protected override void OnParametersSet()
	{
		if ( loadedJam == Jam ) return;

		loadedJam = Jam;
		CategoryId = 0;
		Packages.Clear();
		loadedCategories = null;
	}

	/// <summary>
	/// Resolves presentation metadata when the shared slate changes.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		if ( !IsVisible || Jam is null ) return;

		if ( Categories is null || loadedCategories == Categories ) return;

		loadedCategories = Categories;
		var idents = Categories.SelectMany( x => x.Nominees ).Select( x => x.PackageIdent )
			.Where( x => !string.IsNullOrEmpty( x ) && !Packages.ContainsKey( x ) ).Distinct( StringComparer.OrdinalIgnoreCase );
		foreach ( var ident in idents ) _ = LoadPackage( Jam, ident );
	}

	protected void RequestRefresh() => Voting?.RefreshFinalists();

	protected void SubmitVote( string ident )
	{
		if ( Voting is not null ) _ = Voting.SubmitFinalistVoteAsync( Category, ident );
	}

	async Task LoadPackage( Jam jam, string ident )
	{
		try
		{
			var package = await Package.FetchAsync( ident, false );
			if ( this.IsValid() && Jam == jam && package is not null )
			{
				Packages[ident] = package;
				StateHasChanged();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load finalist {ident} ({e.Message})" );
		}
	}

	protected Package GetPackage( string ident ) => ident is null ? null : Packages.GetValueOrDefault( ident );
	protected void Play( Package package )
	{
		if ( package is not null ) ContentBlocks.OnLaunch( package );
	}
	protected void OpenMenu( Package package ) => ContentBlocks.OnMenu( this, package );

	protected override int BuildHash() => HashCode.Combine( base.BuildHash(), Categories, Category?.Id, Voting?.Version,
		Loading, Submitting, VoteCooldownSeconds, Packages.Count );
}
