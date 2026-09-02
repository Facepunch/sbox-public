using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System.Collections.Immutable;
using System.ComponentModel;

namespace Sandbox.UI;

/// <summary>
/// Source compatibility for package browsing controls that shipped in package.base before protocol 29.
/// New code should use Game.Overlay.ShowPackageSelector instead.
/// </summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[StyleSheet.Inline( "package-base-compatibility", Styles )]
public class PackageList : Panel
{
	const string Styles = """
		.package-list
		{
			flex-direction: column;
			width: 100%;
			height: 100%;
			gap: 12px;
		}

		.package-list > virtualgrid
		{
			flex-grow: 1;
		}
		""";

	readonly CardVideoRotation rotation = new();

	public PackageList() => AddClass( "package-list" );

	[Parameter] public string Query { get; set; }
	[Parameter] public int Take { get; set; } = 100;
	[Parameter] public bool ShowFilters { get; set; }
	[Parameter] public Vector2 ItemSize { get; set; } = new( 260, 206 );
	[Parameter] public Action<Package> OnMenu { get; set; }
	[Parameter] public Action<Package> OnSelected { get; set; }
	[Parameter] public Action<string> OnFilterChanged { get; set; }
	[Parameter] public Package[] Packages { get; set; }
	[Parameter] public RenderFragment<Package> Item { get; set; }
	[Parameter] public List<Package> FoundPackages { get; set; }

	public Package.FindResult Result;

	protected override async Task OnParametersSetAsync()
	{
		if ( Packages is not null )
		{
			FoundPackages = [.. Packages];
			StateHasChanged();
			return;
		}

		FoundPackages = null;
		await RunQuery();
	}

	public override void Tick()
	{
		base.Tick();
		rotation.Count = PackageCard.VideoThumbsPerGrid;
		rotation.Interval = PackageCard.VideoThumbsSeconds;
		rotation.Tick();
	}

	async Task RunQuery()
	{
		var query = Query;
		Result = await Package.FindAsync( string.IsNullOrWhiteSpace( query ) ? "type:game" : query, Take, 0 );

		if ( query != Query )
			return;

		FoundPackages = Result?.Packages?.ToList() ?? [];
		StateHasChanged();
	}

	async Task FetchMore()
	{
		if ( Result is null || FoundPackages is null || FoundPackages.Count >= Result.TotalCount )
			return;

		var query = Query;
		var result = await Package.FindAsync( string.IsNullOrWhiteSpace( query ) ? "type:game" : query, Take, FoundPackages.Count );

		if ( query != Query || result?.Packages is null )
			return;

		FoundPackages.AddRange( result.Packages );
		FoundPackages = [.. FoundPackages];
		StateHasChanged();
	}

	void QueryChanged( string query )
	{
		if ( Query == query )
			return;

		Query = query;
		_ = RunQuery();
		OnFilterChanged?.Invoke( query );
	}

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		if ( ShowFilters && Result is not null )
		{
			tree.OpenElement<PackageFilters>( 0 );
			tree.AddAttribute( 1, nameof( PackageFilters.Query ), Query );
			tree.AddAttribute( 2, nameof( PackageFilters.Result ), Result );
			tree.AddAttribute( 3, nameof( PackageFilters.OnChange ), (object)(Action<string>)QueryChanged );
			tree.CloseElement();
		}

		if ( FoundPackages is null )
		{
			tree.OpenElement<LoaderFullScreen>( 4 );
			tree.CloseElement();
		}
		else if ( FoundPackages.Count == 0 )
		{
			tree.OpenElement<Label>( 5 );
			tree.AddAttribute( 6, "class", "loading-status" );
			tree.AddAttribute( 7, nameof( Label.Text ), "Nothing Found" );
			tree.CloseElement();
		}
		else
		{
			tree.OpenElement<VirtualGrid>( 8 );
			tree.AddAttribute( 9, nameof( VirtualGrid.Items ), FoundPackages );
			tree.AddAttribute( 10, nameof( VirtualGrid.ItemSize ), ItemSize );
			tree.AddAttribute( 11, nameof( VirtualGrid.OnLastCell ), (Action)(() => _ = FetchMore()) );
			tree.AddAttribute( 12, nameof( VirtualGrid.Item ), (RenderFragment<object>)RenderItem );
			tree.CloseElement();
		}
	}

	RenderFragment RenderItem( object value ) => tree =>
	{
		if ( value is not Package package )
			return;

		if ( Item is not null )
		{
			Item( package )( tree );
			return;
		}

		tree.OpenElement<PackageCard>( 0, package.FullIdent );
		tree.AddAttribute( 1, nameof( PackageCard.Package ), package );
		tree.AddAttribute( 2, nameof( PackageCard.Rotation ), rotation );
		tree.AddAttribute( 3, nameof( PackageCard.Clicked ), (Action)(() => OnSelected?.Invoke( package )) );
		tree.AddAttribute( 4, nameof( PackageCard.RightClicked ), (Action)(() => OnMenu?.Invoke( package )) );
		tree.CloseElement();
	};

	protected override int BuildHash() => HashCode.Combine( Query, Result, FoundPackages, ShowFilters, Item, ItemSize );
	protected override string GetRenderTreeChecksum() => $"{BuildHash()}";
}

/// <summary>Compatibility package card formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[StyleSheet.Inline( "package-card-compatibility", Styles )]
public class PackageCard : Panel
{
	const string Styles = """
		.package-card
		{
			flex-direction: column;
			background-color: #181a1f;
			border-radius: 6px;
			overflow: hidden;
			cursor: pointer;
			pointer-events: all;
		}

		.package-card > .image
		{
			min-height: 120px;
			background-size: cover;
			background-position: center;
		}

		.package-card > .details
		{
			padding: 10px;
			flex-direction: column;
		}
		""";

	public enum SizeType { Default, Small, Wide, Tall, List }
	public enum VideoQuality { Auto, Thumb, Low, Medium, High }

	public static bool VideoThumbs { get; set; } = true;
	public static int VideoThumbsPerShelf { get; set; } = 2;
	public static int VideoThumbsPerGrid { get; set; } = 6;
	public static float VideoThumbsSeconds { get; set; } = 10f;

	[Parameter] public Package Package { get; set; }
	[Parameter] public SizeType Size { get; set; }
	[Parameter] public CardVideoRotation Rotation { get; set; }
	[Parameter] public VideoQuality Quality { get; set; } = VideoQuality.Auto;
	[Parameter] public bool ShowSummary { get; set; }
	[Parameter] public Action Clicked { get; set; }
	[Parameter] public Action RightClicked { get; set; }

	CardVideoRotation registered;

	public PackageCard() => AddClass( "package-card" );

	protected override void OnParametersSet()
	{
		base.OnParametersSet();

		foreach ( var size in Enum.GetValues<SizeType>() )
			SetClass( size.ToString().ToLowerInvariant(), size == Size );

		if ( registered == Rotation )
			return;

		registered?.Unregister( this );
		registered = Rotation;
		registered?.Register( this );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		registered?.Unregister( this );
		registered = null;
	}

	protected override void OnClick( MousePanelEvent e ) => Clicked?.Invoke();
	protected override void OnRightClick( MousePanelEvent e ) => RightClicked?.Invoke();

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		tree.OpenElement<Panel>( 0 );
		tree.AddAttribute( 1, "class", "image" );
		tree.AddAttribute( 2, "style", $"background-image: url({Image()})" );

		if ( ShowVideo() && !string.IsNullOrEmpty( Package?.VideoThumb ) )
		{
			tree.OpenElement<Panel>( 3 );
			tree.AddAttribute( 4, "class", "package-video" );
			tree.AddAttribute( 5, "style", $"background-image: url({VideoUrl()})" );
			tree.CloseElement();
		}

		if ( Package is not null && !Package.Flair.IsEmpty )
		{
			tree.OpenElement<PackageFlairBar>( 6 );
			tree.AddAttribute( 7, nameof( PackageFlairBar.Flair ), Package.Flair );
			tree.CloseElement();
		}

		if ( ShowSummary && !string.IsNullOrWhiteSpace( Package?.Summary ) )
		{
			tree.OpenElement<Panel>( 8 );
			tree.AddAttribute( 9, "class", "package-summary" );
			tree.OpenElement<Label>( 10 );
			tree.AddAttribute( 11, "class", "quote-mark" );
			tree.AddAttribute( 12, nameof( Label.Text ), "“" );
			tree.CloseElement();
			tree.OpenElement<Label>( 13 );
			tree.AddAttribute( 14, "class", "quote-text" );
			tree.AddAttribute( 15, nameof( Label.Text ), Package.Summary );
			tree.CloseElement();
			tree.CloseElement();
		}

		tree.CloseElement();
		tree.OpenElement<Panel>( 16 );
		tree.AddAttribute( 17, "class", "column" );
		tree.OpenElement<Label>( 18 );
		tree.AddAttribute( 19, "class", "medium package-title" );
		tree.AddAttribute( 20, nameof( Label.Text ), Package?.Title ?? "Invalid Package" );
		tree.CloseElement();

		if ( Package is null )
		{
			tree.OpenElement<Label>( 21 );
			tree.AddAttribute( 22, "class", "eyebrow" );
			tree.AddAttribute( 23, nameof( Label.Text ), "We couldn't find that package." );
			tree.CloseElement();
		}
		else
		{
			tree.OpenElement<Panel>( 24 );
			tree.AddAttribute( 25, "class", "package-meta" );
			AddStat( tree, 26, "thumb_up", VotePercentage(), null );
			if ( Package.Usage.UsersNow > 0 )
				AddStat( tree, 32, null, $"{Compact( Package.Usage.UsersNow )} playing", "live" );
			else
				AddStat( tree, 38, "group", Compact( Package.Usage.Total.Users ), null );
			tree.CloseElement();
		}

		tree.CloseElement();
	}

	bool ShowVideo() => HasHovered || (VideoThumbs && (Rotation is null || Rotation.IsPlaying( this )));

	string VideoUrl()
	{
		var url = Package?.VideoThumb;
		var quality = Quality == VideoQuality.Auto ? PickQuality() : Quality;
		if ( quality == VideoQuality.Thumb || string.IsNullOrEmpty( url ) ) return url;
		var variant = quality.ToString().ToLowerInvariant();
		return url.Replace( "thumb.webm", $"{variant}.webm" ).Replace( "thumb.mp4", $"{variant}.mp4" );
	}

	VideoQuality PickQuality()
	{
		var width = Box.Rect.Width;
		if ( width <= 0 ) return VideoQuality.Medium;
		if ( width < 160 ) return VideoQuality.Low;
		return width < 900 ? VideoQuality.Medium : VideoQuality.High;
	}

	string VotePercentage()
	{
		if ( Package is null || Package.VotesUp + Package.VotesDown == 0 ) return "Unrated";
		return $"{(int)(Package.VotesUp * 100f / (Package.VotesUp + Package.VotesDown))}%";
	}

	static string Compact( long value )
	{
		if ( value >= 1_000_000 ) return $"{value / 1_000_000f:0.#}M";
		if ( value >= 1_000 ) return $"{value / 1_000f:0.#}K";
		return value.ToString();
	}

	static void AddStat( RenderTreeBuilder tree, int sequence, string icon, string text, string extraClass )
	{
		tree.OpenElement<Panel>( sequence );
		tree.AddAttribute( sequence + 1, "class", $"package-stat {extraClass}" );
		if ( extraClass == "live" )
		{
			tree.OpenElement<Panel>( sequence + 2 );
			tree.AddAttribute( sequence + 3, "class", "live-dot" );
			tree.CloseElement();
		}
		else if ( icon is not null )
		{
			tree.OpenElement<IconPanel>( sequence + 2 );
			tree.AddAttribute( sequence + 3, nameof( IconPanel.Text ), icon );
			tree.CloseElement();
		}
		tree.OpenElement<Label>( sequence + 4 );
		tree.AddAttribute( sequence + 5, "class", "eyebrow" );
		tree.AddAttribute( sequence + 6, nameof( Label.Text ), text );
		tree.CloseElement();
		tree.CloseElement();
	}

	string Image() => Size switch
	{
		SizeType.Tall => Package?.ThumbTall,
		SizeType.Wide or SizeType.Default => Package?.ThumbWide ?? Package?.Thumb,
		_ => Package?.Thumb
	};

	protected override int BuildHash() => HashCode.Combine( Package?.FullIdent, Package?.Usage.UsersNow, Package?.Flair.Length, ShowVideo(), ShowSummary, VideoUrl() );
	protected override string GetRenderTreeChecksum() => $"{BuildHash()}";
}

/// <summary>Compatibility package search filters formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[StyleSheet.Inline( "package-filter-compatibility", Styles )]
public class PackageFilters : Panel
{
	const string Styles = """
		.packagefilter
		{
			width: 100%;
			flex-shrink: 0;
			gap: 16px;
		}

		.packagefilter > .left, .packagefilter > .right
		{
			flex-grow: 1;
			gap: 16px;
		}

		.packagefilterfacet, .packagefilter textentry
		{
			background-color: #181a1fcc;
			color: white;
			border-radius: 4px;
			padding: 4px 12px;
			gap: 8px;
			pointer-events: all;
			cursor: pointer;
		}
		""";

	[Parameter] public string Query { get; set; }
	[Parameter] public string SearchString { get; set; }
	[Parameter] public Package.FindResult Result { get; set; }
	[Parameter] public Action<string> OnChange { get; set; }

	List<string> filterTypes = [];
	List<string> tags = [];
	Dictionary<string, string> filterFacets = [];
	string filterOrder;
	RealTimeUntil timeUntilSearch;
	string pendingSearch;

	protected override void OnParametersSet()
	{
		base.OnParametersSet();
		var parts = (Query ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		filterTypes = parts.Where( x => x.StartsWith( "type:" ) ).Select( x => x[5..] ).ToList();
		filterOrder = parts.Where( x => x.StartsWith( "sort:" ) ).Select( x => x[5..] ).FirstOrDefault();
		tags = parts.Where( x => x.StartsWith( '+' ) ).Select( x => x[1..] ).ToList();
		filterFacets = parts.Select( x => x.Split( ':', 2 ) )
			.Where( x => x.Length == 2 && x[0] is not ("type" or "sort") )
			.GroupBy( x => x[0] )
			.ToDictionary( x => x.Key, x => x.Last()[1] );
	}

	public override void Tick()
	{
		base.Tick();

		if ( pendingSearch is null || timeUntilSearch > 0 )
			return;

		SearchString = pendingSearch;
		pendingSearch = null;
		OnChange?.Invoke( BuildQuery() );
	}

	void SearchEdited( string value )
	{
		pendingSearch = value;
		timeUntilSearch = 0.5f;
	}

	void FacetChanged( Package.Facet facet, string value )
	{
		if ( string.IsNullOrEmpty( value ) ) filterFacets.Remove( facet.Name );
		else filterFacets[facet.Name] = value;
		OnChange?.Invoke( BuildQuery() );
	}

	string BuildQuery()
	{
		var parts = filterTypes.Select( x => $"type:{x}" )
			.Concat( filterFacets.Select( x => $"{x.Key}:{x.Value}" ) )
			.Concat( string.IsNullOrWhiteSpace( filterOrder ) ? [] : [$"sort:{filterOrder}"] )
			.Concat( tags.Select( x => $"+{x}" ) )
			.Concat( string.IsNullOrWhiteSpace( SearchString ) ? [] : [SearchString] );
		return string.Join( ' ', parts );
	}

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		if ( Result is null ) return;

		tree.OpenElement<Panel>( 0 );
		tree.AddAttribute( 1, "class", "packagefilter" );
		tree.OpenElement<Panel>( 2 );
		tree.AddAttribute( 3, "class", "left" );
		var sequence = 4;

		foreach ( var facet in Result.Facets ?? [] )
		{
			filterFacets.TryGetValue( facet.Name, out var value );
			tree.OpenElement<PackageFilterFacet>( sequence++ );
			tree.AddAttribute( sequence++, nameof( PackageFilterFacet.Facet ), facet );
			tree.AddAttribute( sequence++, nameof( PackageFilterFacet.Value ), value );
			tree.AddAttribute( sequence++, nameof( PackageFilterFacet.OnChange ), (object)(Action<Package.Facet, string>)FacetChanged );
			tree.CloseElement();
		}

		tree.CloseElement();
		tree.OpenElement<Panel>( sequence++ );
		tree.AddAttribute( sequence++, "class", "right" );
		tree.OpenElement<TextEntry>( sequence++ );
		tree.AddAttribute( sequence++, nameof( TextEntry.Icon ), "search" );
		tree.AddAttribute( sequence++, nameof( TextEntry.OnTextEdited ), (object)(Action<string>)SearchEdited );
		tree.CloseElement();

		tree.OpenElement<PackageFilterOrder>( sequence++ );
		tree.AddAttribute( sequence++, nameof( PackageFilterOrder.Orders ), Result.Orders ?? [] );
		tree.AddAttribute( sequence++, nameof( PackageFilterOrder.Value ), filterOrder );
		Action<string> orderChanged = x => { filterOrder = x; OnChange?.Invoke( BuildQuery() ); };
		tree.AddAttribute( sequence++, nameof( PackageFilterOrder.OnChange ), (object)orderChanged );
		tree.CloseElement();
		tree.CloseElement();
		tree.CloseElement();
	}

	protected override string GetRenderTreeChecksum() => $"{Result?.GetHashCode()}:{Query}:{SearchString}";
}

/// <summary>Compatibility package facet selector formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
public class PackageFilterFacet : Panel
{
	[Parameter] public Package.Facet Facet { get; set; }
	[Parameter] public string Value { get; set; }
	[Parameter] public Action<Package.Facet, string> OnChange { get; set; }
	Popup menu;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( Facet?.Entries is null ) return;
		if ( menu.IsValid() ) { menu.Delete(); return; }

		menu = new Popup( this, Popup.PositionMode.BelowLeft, 0f );
		menu.Style.MinWidth = Box.Rect.Width;
		foreach ( var entry in Facet.Entries )
		{
			var option = menu.AddOption( entry.Title, entry.Icon, () => SwitchTo( entry ) );
			option.AddChild( new Label( $"{entry.Count:n0}", "count" ) );
			if ( Value == entry.Name ) option.AddClass( "active" );
		}
	}

	void SwitchTo( Package.Facet.Entry entry )
	{
		Value = entry is null || Value == entry.Name ? null : entry.Name;
		OnChange?.Invoke( Facet, Value );
		StateHasChanged();
	}

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		var current = Facet?.Entries?.FirstOrDefault( x => x.Name == Value );
		tree.OpenElement<Label>( 0 );
		tree.AddAttribute( 1, "class", $"packagefilterfacet{(current is null ? "" : " is-active")}" );
		tree.AddAttribute( 2, nameof( Label.Text ), current?.Title ?? Facet?.Title ?? "Filter" );
		tree.CloseElement();
	}

	protected override string GetRenderTreeChecksum() => $"{Facet?.Name}:{Value}";
}

/// <summary>Compatibility package sort selector formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
public class PackageFilterOrder : Panel
{
	[Parameter] public Package.SortOrder[] Orders { get; set; }
	[Parameter] public string Value { get; set; }
	[Parameter] public Action<string> OnChange { get; set; }
	Popup menu;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( menu.IsValid() ) { menu.Delete(); return; }
		menu = new Popup( this, Popup.PositionMode.BelowLeft, 0f );
		foreach ( var entry in Orders ?? [] )
		{
			var option = menu.AddOption( entry.Title, entry.Icon, () => { Value = entry.Name; OnChange?.Invoke( entry.Name ); StateHasChanged(); } );
			if ( Value == entry.Name ) option.AddClass( "active" );
		}
	}

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		var current = (Orders ?? []).FirstOrDefault( x => x.Name == Value );
		tree.OpenElement<Label>( 0 );
		tree.AddAttribute( 1, "class", "packagefilterfacet" );
		tree.AddAttribute( 2, nameof( Label.Text ), current.Title ?? "Sort" );
		tree.CloseElement();
	}

	protected override string GetRenderTreeChecksum() => $"{Orders?.Length}:{Value}";
}

/// <summary>Compatibility package flair display formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[StyleSheet.Inline( "package-flair-compatibility", Styles )]
public class PackageFlairBar : Panel
{
	const string Styles = """
		packageflairbar
		{
			position: absolute;
			top: 6px;
			left: 6px;
			z-index: 2;
			gap: 5px;
		}

		packageflairbar .flair
		{
			width: 22px;
			height: 22px;
			align-items: center;
			justify-content: center;
			border-radius: 4px;
			color: white;
		}
		""";

	[Parameter] public ImmutableArray<Package.PackageFlair> Flair { get; set; } = [];

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		var sequence = 0;
		foreach ( var flair in Flair )
		{
			tree.OpenElement<IconPanel>( sequence++ );
			tree.AddAttribute( sequence++, "class", $"flair flair-{flair.Kind}" );
			tree.AddAttribute( sequence++, "style", flair.Style );
			tree.AddAttribute( sequence++, nameof( IconPanel.Text ), flair.Icon );
			tree.CloseElement();
		}
	}

	protected override string GetRenderTreeChecksum() => $"{Flair.GetHashCode()}";
}

/// <summary>Compatibility loading indicator formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[StyleSheet.Inline( "loader-full-screen-compatibility", Styles )]
public class LoaderFullScreen : Panel
{
	const string Styles = """
		loaderfullscreen
		{
			flex-grow: 1;
			align-items: center;
			justify-content: center;
		}

		loaderfullscreen > .indicator
		{
			width: 240px;
			height: 4px;
			background-color: #ffffff22;
		}

		loaderfullscreen .progress
		{
			height: 100%;
			background-color: white;
		}
		""";

	public RealTimeSince timeSinceShown;
	readonly Panel progress;

	public LoaderFullScreen()
	{
		timeSinceShown = 0;
		AddClass( "loader-full-screen" );
		var indicator = Add.Panel( "indicator" );
		progress = indicator.Add.Panel( "progress" );
	}

	public override void Tick()
	{
		base.Tick();
		var elapsed = timeSinceShown.Relative - 1f;
		if ( elapsed < 0f ) return;
		progress.Style.Width = Length.Fraction( MathF.Pow( elapsed / 30f, 0.33f ).Clamp( 0f, 1f ) );
	}
}

/// <summary>Compatibility video scheduler formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
public sealed class CardVideoRotation
{
	public int Count { get; set; } = 2;
	public float Interval { get; set; } = 10f;

	const float Jitter = 0.15f;

	class Turn
	{
		public Panel Card;
		public float NextSwitch;
	}

	readonly List<Panel> cards = [];
	readonly List<Turn> turns = [];
	readonly Dictionary<Panel, float> lastPlayed = [];
	readonly float phase = Game.Random.Float( 0f, 1f );

	public void Register( Panel card )
	{
		if ( card is not null && !cards.Contains( card ) ) cards.Add( card );
	}

	public void Unregister( Panel card )
	{
		cards.Remove( card );
		lastPlayed.Remove( card );
		foreach ( var turn in turns ) if ( turn.Card == card ) turn.Card = null;
	}

	public bool IsPlaying( Panel card ) => turns.Any( x => x.Card == card );

	public void Tick()
	{
		if ( cards.RemoveAll( x => !x.IsValid() ) > 0 )
			foreach ( var card in lastPlayed.Keys.Where( x => !x.IsValid() ).ToArray() ) lastPlayed.Remove( card );

		var want = Math.Max( 0, Count );
		while ( turns.Count > want ) turns.RemoveAt( turns.Count - 1 );
		while ( turns.Count < want ) turns.Add( new Turn() );

		for ( var i = 0; i < turns.Count; i++ )
		{
			var turn = turns[i];
			if ( Eligible( turn.Card ) && RealTime.Now < turn.NextSwitch ) continue;
			var next = PickNext( turn );
			if ( next is null ) continue;
			var first = !turn.Card.IsValid();
			turn.Card = next;
			turn.NextSwitch = RealTime.Now + Delay( i, first );
		}
	}

	float Delay( int index, bool first )
	{
		var jitter = 1f + Game.Random.Float( -Jitter, Jitter );
		return first ? Interval * (index + 1f + phase) / turns.Count * jitter : Interval * jitter;
	}

	Panel PickNext( Turn turn )
	{
		for ( var pass = 0; pass < 3; pass++ )
		{
			Panel best = null;
			var longestWait = float.MaxValue;
			foreach ( var card in cards )
			{
				if ( !Eligible( card ) || turns.Any( x => x != turn && x.Card == card ) ) continue;
				if ( pass < 2 && card == turn.Card ) continue;
				if ( pass < 2 && Crowded( card, turn, pass == 0 ) ) continue;
				var played = lastPlayed.GetValueOrDefault( card );
				if ( played >= longestWait ) continue;
				best = card;
				longestWait = played;
			}
			if ( best is null ) continue;
			lastPlayed[best] = RealTime.Now;
			return best;
		}
		return turn.Card;
	}

	bool Crowded( Panel card, Turn turn, bool vertical )
	{
		foreach ( var other in turns )
		{
			if ( other == turn || !other.Card.IsValid() ) continue;
			var a = card.Box.Rect;
			var b = other.Card.Box.Rect;
			if ( MathF.Abs( a.Center.x - b.Center.x ) < MathF.Max( a.Width, b.Width ) * 1.5f
				&& (!vertical || MathF.Abs( a.Center.y - b.Center.y ) < MathF.Max( a.Height, b.Height ) * 1.5f) ) return true;
		}
		return false;
	}

	static bool Eligible( Panel card )
	{
		if ( !card.IsValid() ) return false;
		var rect = card.Box.Rect;
		return rect.Width > 0 && rect.Height > 0 && rect.Right > 0 && rect.Bottom > 0 && rect.Left < Screen.Width && rect.Top < Screen.Height;
	}
}

/// <summary>Compatibility context menu formerly supplied by package.base.</summary>
[Hide, EditorBrowsable( EditorBrowsableState.Never )]
[StyleSheet.Inline( "menu-panel-compatibility", Styles )]
public class MenuPanel : Panel
{
	const string Styles = """
		menupanel
		{
			position: absolute;
			z-index: 1000;
			pointer-events: all;
		}

		menupanel > .background
		{
			position: absolute;
			left: -5000px;
			right: -5000px;
			top: -5000px;
			bottom: -5000px;
		}

		menupanel > .inner
		{
			flex-direction: column;
			background-color: #202226;
			padding: 6px;
			border-radius: 4px;
		}

		menupanel .option
		{
			padding: 8px 12px;
			gap: 8px;
			cursor: pointer;
		}
		""";

	record struct MenuOption( string Icon, string Text, Action Action );
	readonly List<MenuOption> options = [];

	public static MenuPanel Open( Panel source )
	{
		var root = source.FindRootPanel();
		var menu = root.AddChild<MenuPanel>();
		menu.Style.Left = root.MousePosition.x * root.ScaleFromScreen;
		menu.Style.Top = root.MousePosition.y * root.ScaleFromScreen;
		menu.PlaySound( "sounds/kenney/ui/ui.navigate.forward.sound" );
		return menu;
	}

	public void AddOption( string icon, string text, Action action )
	{
		options.Add( new( icon, text, action ) );
		StateHasChanged();
	}

	public void AddSpacer()
	{
		options.Add( new( "__spacer__", "", null ) );
		StateHasChanged();
	}

	public void Close()
	{
		foreach ( var menu in FindRootPanel().Children.OfType<MenuPanel>().ToArray() )
			menu.Delete( false );
	}

	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		tree.OpenElement<Panel>( 0 );
		tree.AddAttribute( 1, "class", "background" );
		tree.AddAttribute( 2, "onmousedown", (Action)Close );
		tree.AddAttribute( 3, "onclick", (Action)Close );
		tree.CloseElement();
		tree.OpenElement<Panel>( 4 );
		tree.AddAttribute( 5, "class", "inner" );
		var sequence = 6;

		foreach ( var option in options )
		{
			if ( option.Icon == "__spacer__" )
			{
				tree.OpenElement<Panel>( sequence++ );
				tree.AddAttribute( sequence++, "class", "spacer" );
				tree.CloseElement();
				continue;
			}

			tree.OpenElement<Panel>( sequence++, option.Text );
			tree.AddAttribute( sequence++, "class", "option" );
			tree.AddAttribute( sequence++, "onclick", (Action)(() => { Close(); option.Action?.Invoke(); }) );
			tree.OpenElement<IconPanel>( sequence++ );
			tree.AddAttribute( sequence++, "class", "icon" );
			tree.AddAttribute( sequence++, nameof( IconPanel.Text ), option.Icon );
			tree.CloseElement();
			tree.OpenElement<Label>( sequence++ );
			tree.AddAttribute( sequence++, "class", "text" );
			tree.AddAttribute( sequence++, nameof( Label.Text ), option.Text );
			tree.CloseElement();
			tree.CloseElement();
		}

		tree.CloseElement();
	}

	public override void OnLayout( ref Rect layoutRect )
	{
		const int padding = 10;
		var height = Screen.Height - padding;
		var width = Screen.Width - padding;

		if ( layoutRect.Bottom > height )
		{
			layoutRect.Top -= layoutRect.Bottom - height;
			layoutRect.Bottom = height;
		}

		if ( layoutRect.Right > width )
		{
			layoutRect.Left -= layoutRect.Right - width;
			layoutRect.Right = width;
		}
	}

	protected override string GetRenderTreeChecksum() => $"{options.Count}";
}
