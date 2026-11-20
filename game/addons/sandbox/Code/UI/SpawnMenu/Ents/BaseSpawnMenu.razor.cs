using Sandbox.UI;
namespace Sandbox;

public partial class BaseSpawnMenu : Panel
{
	SpawnMenuOption activeOption;
	PanelSwitcher Switcher = default;
	protected Panel ActivePanel => activeOption?.Panel;

	protected Panel MenuHeader;
	protected Panel MenuFooter;
	bool _addedHeader;
	bool _addedFooter;

	Panel TreeContainer;
	readonly TreeView<SpawnMenuOption> Tree = new() { RowHeight = 30, IndentWidth = 12 };

	/// <summary>
	/// Top level options and headers.
	/// </summary>
	readonly List<SpawnMenuOption> roots = new();

	/// <summary>
	/// The header new options are added under, or null for the root.
	/// </summary>
	SpawnMenuOption currentHeader;

	/// <summary>
	/// Headers we've opened once already. New headers start open, after that the tree remembers.
	/// </summary>
	readonly HashSet<string> knownHeaders = new();

	public BaseSpawnMenu()
	{
		Tree.Roots = roots;
		Tree.Comparer = new OptionComparer();
		Tree.GetChildren = o => o.Children;
		Tree.GetParent = o => o.Parent;
		Tree.CanExpand = o => o.Enabled && o.HasChildren;
		Tree.GetHeight = o => o.IsHeader ? 34f + HeadingGap( o ) : o.Parent is null ? Tree.RowHeight : 26f;
		Tree.OnRow = BindRow;
		Tree.OnSelect = OnTreeSelect;
		Tree.OnContextMenu = o => o.OnRightClick?.Invoke();
	}

	protected override void OnParametersSet()
	{
		base.OnParametersSet();

		roots.Clear();
		currentHeader = null;
		Rebuild();
		Tree.Refresh();
		StateHasChanged();
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );

		if ( !_addedHeader && MenuHeader.IsValid() )
		{
			_addedHeader = true;
			OnMenuHeader( MenuHeader );
		}

		if ( !_addedFooter && MenuFooter.IsValid() )
		{
			_addedFooter = true;
			OnMenuFooter( MenuFooter );
		}

		if ( TreeContainer.IsValid() && Tree.Parent != TreeContainer )
		{
			TreeContainer.AddChild( Tree );
		}

		Tree.RebindRows();
	}

	protected bool _firstViewed;
	readonly RealTimeSince _created = 0;
	internal Tab MenuTab { get; set; }

	public override void Tick()
	{
		base.Tick();

		if ( _firstViewed ) return;

		var prewarm = MenuTab?.Selected == true && _created > 2f;
		if ( !IsVisible && !prewarm ) return;
		if ( roots.Count == 0 ) return;
		if ( !Switcher.IsValid() ) return;

		_firstViewed = true;
		SwitchOption( AllOptions().FirstOrDefault( x => x.PanelCreator != null || x.Panel != null ) );
	}


	protected virtual void Rebuild()
	{

	}

	protected virtual void OnMenuHeader( Panel header )
	{

	}

	protected virtual void OnMenuFooter( Panel footer )
	{

	}

	/// <summary>
	/// A section with an option for each mounted game that has prefabs tagged <paramref name="tag"/> (see <see cref="MountedPrefabs"/>),
	/// or scripted entities made from its art that <paramref name="where"/> accepts, filed under the game by the second level
	/// of their category - "Half-Life 2" for "Npc/Half-Life 2". An installed game that isn't mounted gets an option
	/// that mounts it for the session, unless it's been seen mounted with nothing for this tab.
	/// </summary>
	protected void AddMountedSections( string tag, Func<ScriptedEntity, bool> where )
	{
		MountedPrefabs.Learn();

		var sections = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var prefab in MountedPrefabs.WithTag( tag ) )
			if ( MountedPrefabs.GameTitle( prefab.Path ) is { Length: > 0 } title && Sandbox.Mounting.MountUtility.TryParse( prefab.Path, out string ident ) )
				sections.TryAdd( title, ident );

		foreach ( var entity in ResourceLibrary.GetAll<ScriptedEntity>().Where( e => !string.IsNullOrEmpty( e.RequiredMount ) && !e.IsRemote && e.IsMountAvailable && where( e ) ) )
			if ( SpawnMenuCategory.Section( entity.Category ) is { Length: > 0 } title )
				sections.TryAdd( title, entity.RequiredMount );

		foreach ( var mount in MountedPrefabs.Unmounted( tag ) )
			sections.TryAdd( mount.Title, mount.Ident );

		if ( sections.Count == 0 ) return;

		AddHeader( "#spawnmenu.tab.games" );

		foreach ( var (section, ident) in sections.OrderBy( x => x.Key, StringComparer.CurrentCultureIgnoreCase ) )
		{
			var mounted = Sandbox.Mounting.Directory.Get( ident )?.IsMounted == true;
			var option = mounted
				? AddOption( "🧩", section, () => new MountedEntityList { Game = section, Tag = tag, Where = where } )
				: AddOption( "🧩", section, () => new MountGamePanel { Ident = ident, Mounted = RebuildOptions } );

			option.Key = $"mounted:{section}";
			option.Tokenize = false;
		}
	}

	/// <summary>
	/// Make the options again, after what they list changed, such as a game being mounted. The open one stays open.
	/// </summary>
	protected void RebuildOptions()
	{
		var key = activeOption?.Key;
		var previous = activeOption?.Panel;
		activeOption = null;
		previous?.Delete();

		roots.Clear();
		currentHeader = null;
		Rebuild();
		Tree.Refresh();

		if ( AllOptions().FirstOrDefault( x => x.Key == key && (x.PanelCreator != null || x.Panel != null) ) is { } again )
			SwitchOption( again );

		StateHasChanged();
	}

	/// <summary>
	/// Start a collapsible section. Options added after this go inside it.
	/// </summary>
	public SpawnMenuOption AddHeader( string name, string icon = null )
	{
		currentHeader = new SpawnMenuOption { Name = name, Icon = icon, IsHeader = true };
		roots.Add( currentHeader );

		if ( knownHeaders.Add( name ) ) Tree.Open( currentHeader );
		return currentHeader;
	}

	SpawnMenuOption AddOption( SpawnMenuOption o )
	{
		o.Parent = currentHeader;
		(currentHeader?.Children ?? roots).Add( o );
		return o;
	}

	public SpawnMenuOption AddOption( SpawnMenuOption parent, SpawnMenuOption o )
	{
		if ( parent is null ) return AddOption( o );

		o.Parent = parent;
		parent.Children.Add( o );
		return o;
	}

	public SpawnMenuOption AddOption( string name, Func<Panel> createPanelFunction )
	{
		return AddOption( new SpawnMenuOption
		{
			Name = name,
			PanelCreator = createPanelFunction
		} );
	}

	public SpawnMenuOption AddOption( string icon, string name, Func<Panel> createPanelFunction )
	{
		return AddOption( new SpawnMenuOption
		{
			Icon = icon,
			Name = name,
			PanelCreator = createPanelFunction
		} );
	}

	public SpawnMenuOption AddOption( string icon, string name, Func<Panel> createPanelFunction, Action onRightClick )
	{
		return AddOption( new SpawnMenuOption
		{
			Icon = icon,
			Name = name,
			PanelCreator = createPanelFunction,
			OnRightClick = onRightClick
		} );
	}

	public void AddAction( string icon, string name, Action action )
	{
		AddOption( new SpawnMenuOption
		{
			Icon = icon,
			Name = name,
			OnClick = action
		} );
	}

	IEnumerable<SpawnMenuOption> AllOptions()
	{
		foreach ( var o in roots )
		{
			yield return o;
			foreach ( var child in o.Children ) yield return child;
		}
	}

	public void RemoveOption( SpawnMenuOption o )
	{
		(o.Parent?.Children ?? roots).Remove( o );
		StateHasChanged();
	}

	void OnTreeSelect( SpawnMenuOption o )
	{
		// Clicking empty space drops the selection but still reports the cursor row
		if ( !Tree.IsSelected( o ) ) return;

		if ( !o.Enabled ) return;

		// Headers only group things, clicking one opens or closes it
		if ( o.PanelCreator is null && o.Panel is null && o.OnClick is null )
		{
			Tree.Toggle( o );
			return;
		}

		if ( o.HasChildren ) Tree.Open( o );

		if ( o.OnClick != null )
		{
			o.OnClick.Invoke();
			return;
		}

		SwitchOption( o );
	}

	float HeadingGap( SpawnMenuOption o ) => o.IsHeader && o != roots.FirstOrDefault() ? 10f : 0f;

	void BindRow( TreeRow row, SpawnMenuOption o )
	{
		row.SetClass( "active", activeOption is not null && Tree.Comparer.Equals( o, activeOption ) );
		row.SetClass( "disabled", !o.Enabled );
		row.SetClass( "section-heading", o.IsHeader );
		row.Style.PaddingTop = HeadingGap( o );

		row.IconName = o.Icon;
		row.Label.Tokenize = o.Tokenize;
		row.Text = o.Name;
	}

	protected void SwitchOption( SpawnMenuOption o )
	{
		if ( o == activeOption ) return;

		activeOption?.Panel?.SetClass( "hidden", true );

		activeOption = o;

		if ( activeOption.Panel == null && activeOption.PanelCreator != null )
		{
			activeOption.Panel = activeOption.PanelCreator.Invoke();
			Switcher.AddChild( activeOption.Panel );
		}

		activeOption?.Panel?.SetClass( "hidden", false );
		Tree.RebindRows();
		StateHasChanged();
	}

	public void SelectOption( string name )
	{
		var option = AllOptions().FirstOrDefault( o => o.Name == name && (o.PanelCreator != null || o.Panel != null) );
		if ( option != null ) SwitchOption( option );
	}

	public void DeselectOption()
	{
		activeOption?.Panel?.SetClass( "hidden", true );
		activeOption = null;
		Tree.RebindRows();
		StateHasChanged();
	}

	public class SpawnMenuOption
	{
		internal bool IsHeader { get; init; }

		public string Name { get; set; }

		/// <summary>
		/// Whether the name is allowed to reference a localization phrase.
		/// Disable this for user-authored entries.
		/// </summary>
		public bool Tokenize { get; set; } = true;

		public string Icon { get; set; }
		public bool Enabled { get; set; } = true;

		public Func<Panel> PanelCreator { get; set; }
		public Panel Panel { get; set; }
		public Action OnClick { get; set; }
		public Action OnRightClick { get; set; }

		/// <summary>
		/// Identifies this option across rebuilds, so the tree keeps it open and selected.
		/// Defaults to the name - set it when names can repeat, like folders.
		/// </summary>
		public string Key
		{
			get => field ?? Name;
			set => field = value;
		}

		public SpawnMenuOption Parent { get; set; }

		/// <summary>
		/// Makes the children the first time the option is opened, for trees too big to build up front.
		/// </summary>
		public Func<IEnumerable<SpawnMenuOption>> LoadChildren { get; set; }

		List<SpawnMenuOption> _children;

		public List<SpawnMenuOption> Children
		{
			get
			{
				if ( _children is not null ) return _children;

				_children = new();
				if ( LoadChildren is null ) return _children;

				foreach ( var child in LoadChildren() )
				{
					child.Parent = this;
					_children.Add( child );
				}

				return _children;
			}
		}

		/// <summary>
		/// Whether this can be opened, without loading the children to find out.
		/// </summary>
		public bool HasChildren => _children is null ? LoadChildren is not null : _children.Count > 0;
	}

	/// <summary>
	/// Options are recreated on every rebuild. Match them by key so open ones stay open.
	/// </summary>
	class OptionComparer : IEqualityComparer<SpawnMenuOption>
	{
		public bool Equals( SpawnMenuOption a, SpawnMenuOption b ) => a?.Key == b?.Key;
		public int GetHashCode( SpawnMenuOption o ) => o.Key?.GetHashCode() ?? 0;
	}
}

