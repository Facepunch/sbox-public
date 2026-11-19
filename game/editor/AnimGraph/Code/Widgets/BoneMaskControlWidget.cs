namespace Editor.AnimGraph;

/// <summary>
/// ComboBox control widget listing the skeleton's bone masks. Nothing else resolves to a mask at
/// runtime, so nothing else is offered. Used via [Editor( "animgraph.bonemask" )] on strings.
/// </summary>
[CustomEditor( typeof( string ), NamedEditor = "animgraph.bonemask" )]
public class BoneMaskControlWidget : ControlWidget, AssetSystem.IEventListener
{
	private string _skeletonPath;
	private Skeleton _skeleton;
	private Asset _skeletonAsset;
	private bool _skeletonOutOfDate;

	public BoneMaskControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();
		RefreshSkeleton();
		Rebuild();
	}

	protected override int ValueHash
	{
		get
		{
			if ( _skeletonPath != GetSkeletonPath() )
				RefreshSkeleton();

			return HashCode.Combine( base.ValueHash, _skeletonPath, _skeleton?.MaskNames, _skeletonAsset?.IsCompileFailed, _skeletonOutOfDate );
		}
	}

	protected override void OnValueChanged() => Rebuild();

	private void Rebuild()
	{
		Layout.Clear( true );

		var comboBox = new ComboBox( this );
		var current = SerializedProperty.GetValue<string>() ?? "";
		var found = string.IsNullOrWhiteSpace( current );
		var masks = _skeleton?.MaskNames ?? [];
		var compileFailed = _skeletonAsset?.IsCompileFailed == true;

		comboBox.AddItem( "None", "layers_clear", () => Pick( "" ), selected: found );

		foreach ( var mask in masks )
		{
			var selected = string.Equals( current, mask, StringComparison.OrdinalIgnoreCase );
			found |= selected;

			comboBox.AddItem( mask, "layers", () => Pick( mask ), selected: selected );
		}

		if ( !found )
			comboBox.AddItem( $"{current} (missing)", "error", () => Pick( current ), selected: true );

		Layout.Add( comboBox );

		string warning = null;
		if ( compileFailed )
			warning = $"Skeleton '{_skeletonPath}' failed to compile. Masks may be out of date. Fix the errors in the console and recompile the skeleton.";
		else if ( !string.IsNullOrWhiteSpace( _skeletonPath ) && _skeleton is null )
			warning = $"Skeleton '{_skeletonPath}' could not be loaded. Check the console and compile the skeleton.";
		else if ( _skeletonOutOfDate )
			warning = $"Skeleton '{_skeletonPath}' is not up to date. Masks may be out of date. Check the console and recompile the skeleton.";

		if ( warning is not null )
			Layout.Add( new WarningBox( warning, this ) { BackgroundColor = Theme.Red, Icon = "error_outline" } );
	}

	/// <summary>
	/// Nothing recompiles the graph until the edit is finished, and a combo box has no focus to
	/// lose the way the pickers do, so say so as soon as something is picked.
	/// </summary>
	private void Pick( string mask )
	{
		SerializedProperty.SetValue( mask );
		PropertyFinishEdit();
	}

	private string GetSkeletonPath()
	{
		var owners = SerializedProperty.Parent?.Targets;
		var node = owners?.OfType<Node>().FirstOrDefault()
			?? owners?.OfType<VariationDataNode.VariationData>().FirstOrDefault()?.Owner;

		return node?.Root?.TargetSkeleton;
	}

	private void RefreshSkeleton()
	{
		_skeletonPath = GetSkeletonPath();
		_skeletonAsset = AssetSystem.FindByPath( _skeletonPath );
		_skeleton = string.IsNullOrWhiteSpace( _skeletonPath ) ? null : Skeleton.Load( _skeletonPath );
		_skeletonOutOfDate = _skeletonAsset?.HasSourceFile == true && !_skeletonAsset.IsCompiledAndUpToDate;
	}

	void AssetSystem.IEventListener.OnAssetSystemChanges() => RefreshSkeleton();
}
