public sealed partial class PostProcessManager
{
	// Rebuild the stock definitions from source when the game assembly changes.
	[SkipHotload]
	static readonly Dictionary<Type, string> BuiltInEffects = new()
	{
		[typeof( Bloom )] = "#effects.group.light_color",
		[typeof( ColorAdjustments )] = "#effects.group.light_color",

		[typeof( ChromaticAberration )] = "#effects.group.lens_motion",
		[typeof( DepthOfField )] = "#effects.group.lens_motion",
		[typeof( MotionBlur )] = "#effects.group.lens_motion",
		[typeof( Vignette )] = "#effects.group.lens_motion",

		[typeof( FilmGrain )] = "#effects.group.detail_texture",
		[typeof( Sharpen )] = "#effects.group.detail_texture",

		[typeof( Pixelate )] = "#effects.group.art_styles"
	};

	readonly Dictionary<string, TypeDescription> _types = new( StringComparer.OrdinalIgnoreCase );
	IReadOnlyList<(TypeDescription Type, PostProcessMenuAttribute Attribute)> _marked;
	int _revision;

	/// <summary>
	/// Changes when the installed type library is refreshed, including script mounts and hotloads.
	/// </summary>
	public int CatalogRevision
	{
		get
		{
			RefreshCatalog();
			return _revision;
		}
	}

	/// <summary>
	/// Returns the menu entries grouped and sorted for the current language.
	/// </summary>
	internal IGrouping<string, TypeDescription>[] MenuGroups()
	{
		RefreshCatalog();
		return _types.Values
			.OrderBy( x => LocalizedText.Resolve( MenuGroup( x ) ), StringComparer.CurrentCultureIgnoreCase )
			.ThenBy( x => LocalizedText.Resolve( MenuTitle( x ) ), StringComparer.CurrentCultureIgnoreCase )
			.GroupBy( MenuGroup )
			.ToArray();
	}

	static string MenuGroup( TypeDescription type ) =>
		BuiltInEffects.GetValueOrDefault( type.TargetType ) ?? type.Group ?? "#effects.group.effects";

	internal static string MenuTitle( TypeDescription type )
	{
		if ( !BuiltInEffects.ContainsKey( type.TargetType ) ) return type.Title;

		return $"#asset.{type.Name.ToLowerInvariant()}.title";
	}

	void RefreshCatalog()
	{
		// TypeLibrary returns the same immutable query result until types change.
		var marked = Game.TypeLibrary.GetTypesWithAttribute<PostProcessMenuAttribute>( inherited: false );
		if ( ReferenceEquals( marked, _marked ) ) return;

		_marked = marked;
		_types.Clear();

		foreach ( var type in BuiltInEffects.Keys.Select( t => Game.TypeLibrary.GetType( t ) )
			.Concat( marked.Select( x => x.Type ) ) )
		{
			if ( type is null || type.IsAbstract || !type.TargetType.IsAssignableTo( typeof( BasePostProcess ) ) )
				continue;

			if ( !_types.TryAdd( type.ClassName, type ) && _types[type.ClassName] != type )
				Log.Warning( $"Post-process effects share the class name '{type.ClassName}'. Give each effect a unique [ClassName]." );
		}

		foreach ( var (id, go) in _active.ToArray() )
		{
			if ( !_types.TryGetValue( id, out var type ) || !go.IsValid() || go.Components.Get( type.TargetType, FindMode.EverythingInSelf ) is null )
				Remove( id );
		}

		_revision++;
	}

	internal TypeDescription FindEffect( string id )
	{
		if ( string.IsNullOrEmpty( id ) ) return null;

		RefreshCatalog();

		// Cameras saved before the type catalog store the stock asset path.
		const string prefix = "entities/pp/";
		if ( id.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) && id.EndsWith( ".spp", StringComparison.OrdinalIgnoreCase ) )
			id = id[prefix.Length..^4];

		return _types.GetValueOrDefault( id );
	}

	internal GameObject CreateEffect( GameObject parent, string id, bool enabled )
	{
		var type = FindEffect( id );
		if ( type is null ) return null;

		var go = new GameObject( parent, false, type.Title );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.Components.Create( type );
		go.Enabled = enabled;
		return go;
	}
}
