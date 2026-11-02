namespace Editor.AnimGraph;

/// <summary>
/// A named variation of a graph. Variations let one graph drive several models - each variation
/// picks its own target model and can override the data on any variation data node.
/// Ported from Esoterica's <c>EE::Animation::Variation</c>.
/// </summary>
public sealed class Variation
{
	/// <summary>
	/// The default name for a graph variation. The default variation always exists and is the root
	/// of the hierarchy.
	/// </summary>
	public const string DefaultVariationID = "Default";

	public static string GetResourcePath( string graphPath, string variationID )
		=> string.IsNullOrWhiteSpace( variationID ) || variationID == DefaultVariationID ? graphPath : $"{graphPath}+{variationID}.ag";

	/// <summary>
	/// Variation IDs end up as identifiers in the compiled resource, so keep them to name chars.
	/// Returns null when nothing usable is left.
	/// </summary>
	public static string SanitizeID( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		var cleaned = new string( name.Trim().Select( c => char.IsLetterOrDigit( c ) || c == '_' ? c : '_' ).ToArray() );
		return string.IsNullOrEmpty( cleaned ) ? null : cleaned;
	}

	private Model _targetModel;
	private string _targetModelPath;
	private string _targetSkeletonPath;
	private ulong _previewBodyGroups = ulong.MaxValue;

	public string ID { get; set; }

	/// <summary>
	/// The variation this one inherits from. Empty for the default variation.
	/// </summary>
	public string ParentID { get; set; }

	/// <summary>
	/// The model this variation's poses are shown on.
	/// </summary>
	public Model TargetModel
	{
		get
		{
			if ( _targetModel is null && !string.IsNullOrWhiteSpace( _targetModelPath ) )
				_targetModel = Model.Load( _targetModelPath );

			return _targetModel;
		}
		set
		{
			// Compare through the getter, the model may only be a path until it's first read
			if ( TargetModel == value )
				return;

			_targetModel = value;
			_targetModelPath = value?.Name;

			// Body groups are indices into the old model's parts, they mean nothing on a new one
			_previewBodyGroups = ulong.MaxValue;
		}
	}

	internal string TargetModelPath
	{
		get => _targetModel?.Name ?? _targetModelPath;
		set
		{
			_targetModel = null;
			_targetModelPath = value;
		}
	}

	/// <summary>
	/// The skeleton this variation animates. Every clip the graph plays is compiled against it,
	/// and it's the layout the graph's poses come out in. The model is only what they're shown on.
	/// A model names the skeleton its animation is authored against, so that's the default when
	/// it's loaded. <see cref="ResolveTargetSkeletonPath"/> loads it.
	/// </summary>
	public string TargetSkeletonPath
	{
		get => string.IsNullOrWhiteSpace( _targetSkeletonPath ) ? _targetModel?.SkeletonName : _targetSkeletonPath;
		set => _targetSkeletonPath = AssetSystem.FindByPath( value )?.Path ?? value;
	}

	/// <summary>
	/// <see cref="TargetSkeletonPath"/>, loading the model to read its default when none was set.
	/// Main thread only.
	/// </summary>
	internal string ResolveTargetSkeletonPath()
	{
		return string.IsNullOrWhiteSpace( _targetSkeletonPath ) ? TargetModel?.SkeletonName : _targetSkeletonPath;
	}

	/// <summary>
	/// The clips this variation can play - the ones compiled against <see cref="TargetSkeletonPath"/>.
	/// A clip records its skeleton as a resource reference, so the asset system already knows which
	/// clips belong to it. Nothing is compatible until a skeleton is set.
	/// </summary>
	public IEnumerable<string> CompatibleClips
	{
		get
		{
			var skeletonPath = ResolveTargetSkeletonPath();
			if ( string.IsNullOrWhiteSpace( skeletonPath ) )
				return [];

			var skeleton = AssetSystem.FindByPath( skeletonPath );
			if ( skeleton is null )
				return [];

			return skeleton.GetDependants( false )
				.Where( x => x?.AssetType?.FileExtension == "anim" )
				.Select( x => x.Path );
		}
	}

	/// <summary>
	/// Body groups shown on this variation's preview model, defaulting to the model's own.
	/// </summary>
	public ulong PreviewBodyGroups
	{
		get => _previewBodyGroups == ulong.MaxValue ? TargetModel?.Parts.DefaultMask ?? ulong.MaxValue : _previewBodyGroups;
		set => _previewBodyGroups = value;
	}

	/// <summary>
	/// Raw mask - <see cref="ulong.MaxValue"/> means "whatever the model defaults to", so it isn't saved.
	/// </summary>
	internal ulong RawPreviewBodyGroups => _previewBodyGroups;

	/// <summary>
	/// Material group (skin) shown on this variation's preview model.
	/// </summary>
	public string PreviewMaterialGroup { get; set; }

	/// <summary>
	/// Models bone merged onto this variation's preview model - weapons, clothing etc.
	/// </summary>
	public List<PreviewAttachment> PreviewAttachments { get; set; } = new();

	/// <summary>
	/// Bone the preview camera focuses on for this variation.
	/// </summary>
	public string PreviewFocusBone { get; set; }
}

/// <summary>
/// The tree of variations owned by a graph. There is always a default variation at the root, and
/// every other variation names a parent it inherits from.
/// Ported from Esoterica's <c>EE::Animation::VariationHierarchy</c>.
/// </summary>
public sealed class VariationHierarchy
{
	private readonly List<Variation> _variations = new();

	public VariationHierarchy()
	{
		Reset();
	}

	/// <summary>
	/// A flat list of all variations, the default one first.
	/// </summary>
	public IReadOnlyList<Variation> All => _variations;

	public void Reset()
	{
		_variations.Clear();
		_variations.Add( new Variation { ID = Variation.DefaultVariationID } );
	}

	/// <summary>
	/// Does the specified variation exist?
	/// </summary>
	public bool IsValidVariation( string variationID )
	{
		if ( variationID == Variation.DefaultVariationID )
			return true;

		return _variations.Any( variation => variation.ID == variationID );
	}

	/// <summary>
	/// Get the actual case correct variation ID - this is useful to give the users some degree of flexibility.
	/// </summary>
	public string TryGetCaseCorrectVariationID( string variationName )
	{
		return _variations.FirstOrDefault( variation => string.Equals( variation.ID, variationName, StringComparison.OrdinalIgnoreCase ) )?.ID;
	}

	/// <summary>
	/// Is this name already taken? Variations resolve case insensitively when the graph plays, so
	/// two that only differ in case would be the same variation at runtime.
	/// </summary>
	public bool IsVariationNameTaken( string variationID )
	{
		return TryGetCaseCorrectVariationID( variationID ) is not null;
	}

	/// <summary>
	/// Get a specified variation.
	/// </summary>
	public Variation GetVariation( string variationID )
	{
		return _variations.FirstOrDefault( variation => variation.ID == variationID );
	}

	/// <summary>
	/// Returns the ID of the parent variation, empty when called for the default variation.
	/// </summary>
	public string GetParentVariationID( string variationID )
	{
		return GetVariation( variationID )?.ParentID;
	}

	/// <summary>
	/// The closest variation at or above this one that owns its data, falling back to the default
	/// variation. Node data resolves up the tree, so this is where a variation's data comes from.
	/// </summary>
	public string ResolveDataSource( string variationID, Func<string, bool> ownsData )
	{
		if ( string.IsNullOrEmpty( variationID ) || variationID == Variation.DefaultVariationID )
			return Variation.DefaultVariationID;

		if ( ownsData( variationID ) )
			return variationID;

		var parentID = GetParentVariationID( variationID );

		// Bounded by the variation count so a malformed hierarchy can't spin
		for ( var i = _variations.Count; i > 0 && !string.IsNullOrEmpty( parentID ); i-- )
		{
			if ( parentID == Variation.DefaultVariationID || ownsData( parentID ) )
				return parentID;

			parentID = GetParentVariationID( parentID );
		}

		return Variation.DefaultVariationID;
	}

	/// <summary>
	/// Serialized data can name a parent that doesn't exist, or a chain that loops back on itself -
	/// a hand edit or a bad merge. Either way the variation would vanish from the tree, so anything
	/// that can't reach the default variation is reparented onto it.
	/// </summary>
	public void Repair()
	{
		foreach ( var variation in _variations )
		{
			if ( variation.ID == Variation.DefaultVariationID || ReachesDefault( variation ) )
				continue;

			variation.ParentID = Variation.DefaultVariationID;
		}
	}

	private bool ReachesDefault( Variation variation )
	{
		var parent = GetVariation( variation.ParentID );

		// Bounded by the variation count so a cycle can't spin
		for ( var i = _variations.Count; i > 0 && parent is not null; i-- )
		{
			if ( parent.ID == Variation.DefaultVariationID )
				return true;

			parent = GetVariation( parent.ParentID );
		}

		return false;
	}

	/// <summary>
	/// Get all children for a specified variation.
	/// </summary>
	public List<string> GetChildVariations( string variationID )
	{
		return _variations
			.Where( variation => variation.ParentID == variationID )
			.Select( variation => variation.ID )
			.ToList();
	}

	/// <summary>
	/// Every variation below this one, nearest first.
	/// </summary>
	public IEnumerable<string> GetDescendantVariations( string variationID )
	{
		var pending = new Queue<string>();
		var seen = new HashSet<string>();
		pending.Enqueue( variationID );

		while ( pending.Count > 0 )
		{
			foreach ( var child in GetChildVariations( pending.Dequeue() ) )
			{
				// A repaired hierarchy has no cycles, but never trust one enough to spin on it
				if ( !seen.Add( child ) )
					continue;

				pending.Enqueue( child );
				yield return child;
			}
		}
	}

	/// <summary>
	/// This will create a new variation under the specified parent variation.
	/// </summary>
	public void CreateVariation( string variationID, string parentVariationID )
	{
		if ( string.IsNullOrWhiteSpace( variationID ) || IsVariationNameTaken( variationID ) )
			return;

		if ( GetVariation( parentVariationID ) is not { } parent )
			return;

		var variation = new Variation
		{
			ID = variationID,
			ParentID = parentVariationID,
			TargetModel = parent.TargetModel,
			TargetSkeletonPath = parent.TargetSkeletonPath,
		};

		// Same model as the parent, so start from how the parent previews it
		variation.PreviewBodyGroups = parent.RawPreviewBodyGroups;
		variation.PreviewMaterialGroup = parent.PreviewMaterialGroup;
		variation.PreviewFocusBone = parent.PreviewFocusBone;
		variation.PreviewAttachments = parent.PreviewAttachments.Select( x => x.Clone() ).ToList();

		_variations.Add( variation );
	}

	/// <summary>
	/// This will rename the specified variation.
	/// </summary>
	public void RenameVariation( string oldVariationID, string newVariationID )
	{
		if ( oldVariationID == Variation.DefaultVariationID || newVariationID == Variation.DefaultVariationID )
			return;

		if ( !IsValidVariation( oldVariationID ) )
			return;

		// Changing the case of its own name is fine, taking another variation's isn't
		if ( TryGetCaseCorrectVariationID( newVariationID ) is { } taken && taken != oldVariationID )
			return;

		foreach ( var variation in _variations )
		{
			if ( variation.ID == oldVariationID )
				variation.ID = newVariationID;

			if ( variation.ParentID == oldVariationID )
				variation.ParentID = newVariationID;
		}
	}

	/// <summary>
	/// This will destroy the specified variation and all children.
	/// </summary>
	public void DestroyVariation( string variationID )
	{
		if ( variationID == Variation.DefaultVariationID )
			return;

		if ( GetVariation( variationID ) is not { } variation )
			return;

		// Materialised before anything is removed, the walk reads the list it's about to change
		var children = GetDescendantVariations( variationID ).ToHashSet();

		_variations.Remove( variation );
		_variations.RemoveAll( x => children.Contains( x.ID ) );
	}

	internal void Add( Variation variation )
	{
		// Serialized data could name a variation twice, and the second one could never play
		if ( string.IsNullOrWhiteSpace( variation?.ID ) || IsVariationNameTaken( variation.ID ) )
			return;

		_variations.Add( variation );
	}
}
