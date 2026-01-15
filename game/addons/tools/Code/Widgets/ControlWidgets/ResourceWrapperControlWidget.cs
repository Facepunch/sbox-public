using Sandbox.Resources;
using System.Reflection;

namespace Editor;

/// <summary>
/// Allows resources to choose between different types of sources, such as embedded, disk, or a generator.
/// At its core this is just a button to switch types and a body cell holding the editor for whatever type is selected.
/// </summary>
public sealed class ResourceWrapperControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;
	public Resource Resource => SerializedProperty.GetValue<Resource>( null );

	IconButton Button;
	Layout Body;
	string CurrentOption = null;

	public ResourceWrapperControlWidget( SerializedProperty property ) : base( property )
	{
		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		Layout = Layout.Row();
		Layout.Spacing = 2;

		Body = Layout.AddRow();

		Layout.AddStretchCell();

		if ( HasAvailableOptions() )
		{
			var buttonCell = Layout.AddColumn();
			buttonCell.Alignment = TextFlag.RightTop;
			Button = buttonCell.Add( new IconButton.WithCornerIcon( "category" )
			{
				OnClick = ShowMenu,
				Background = Color.Transparent,
				Foreground = Theme.Yellow,
				IconSize = 16,
				CornerIconSize = 16,
				CornerIconOffset = 2
			} );
			Button.Enabled = SerializedProperty.IsEditable;
			Button.FixedSize = Theme.RowHeight;
			Button.ToolTip = "Source Type";
		}

		TryGetOption();
		RebuildControl();
	}

	bool IsActive( string option ) => CurrentOption == option;

	Option AddOption( Menu menu, string name, string icon = null, string option = null, Action action = null )
	{
		var o = menu.AddOption( name, icon, () =>
		{
			if ( action is null )
			{
				SwitchTo( option );
			}
			else
			{
				action();
			}
		} );
		o.Checkable = true;
		o.Checked = IsActive( option );

		return o;
	}

	/// <summary>
	/// Special case, when we switch to embedded, we can take the current GameResource properties and apply them to the inline resource
	/// </summary>
	void SwitchToEmbedded()
	{
		//
		// I think this is better than having a specific option to convert a file to embedded
		//
		var oldResource = SerializedProperty.GetValue<GameResource>();

		CurrentOption = "embed";

		if ( oldResource is null )
		{
			RebuildControl();
			return;
		}

		//
		// Create a new embedded resource from the old one, this will copy all properties over
		//
		EmbeddedResourceControlWidget.CreateEmbeddedFromFile( SerializedProperty, oldResource );

		RebuildControl();
	}

	/// <summary>
	/// Do we have any options available for this resource?
	/// </summary>
	/// <returns></returns>
	private bool HasAvailableOptions()
	{
		//
		// GameResources - CanEmbed?
		//
		var attribute = SerializedProperty.PropertyType.GetCustomAttribute<AssetTypeAttribute>();
		if ( attribute is not null && !attribute.Flags.Contains( AssetTypeFlags.NoEmbedding ) )
		{
			return true;
		}

		//
		// Find the gernerators
		//
		var generators = EditorTypeLibrary.GetGenericTypes( typeof( ResourceGenerator<> ), [SerializedProperty.PropertyType] );
		if ( generators.Any( x => !x.TargetType.IsAbstract ) )
		{
			return true;
		}

		//
		// Anything else
		//
		return false;
	}

	/// <summary>
	/// Show the list of options this resource can come from
	/// </summary>
	void ShowMenu()
	{
		var menu = new ContextMenu();

		AddOption( menu, "From Disk", "folder" );

		// Only allow embedded if the type is a subclass of GameResource - it doesn't make sense to embed native types
		if ( SerializedProperty.PropertyType.IsSubclassOf( typeof( GameResource ) ) )
		{
			var attribute = SerializedProperty.PropertyType.GetCustomAttribute<AssetTypeAttribute>();

			if ( attribute is not null && !attribute.Flags.Contains( AssetTypeFlags.NoEmbedding ) )
			{
				AddOption( menu, "Embedded", "document_scanner", "embed", SwitchToEmbedded );
			}
		}

		//
		// Add The Generators
		//
		var generators = EditorTypeLibrary.GetGenericTypes( typeof( ResourceGenerator<> ), [SerializedProperty.PropertyType] );

		foreach ( var generator in generators.OrderBy( x => x.Order ).ThenBy( x => x.Name ) )
		{
			if ( generator.TargetType.IsAbstract ) continue;

			var option = AddOption( menu, generator.Title, generator.Icon, generator.ClassName );
			var description = generator.Description;

			if ( !string.IsNullOrEmpty( description ) )
			{
				menu.ToolTipsVisible = true;
				option.ToolTip = description;
			}
		}

		menu.OpenNextTo( Button, WidgetAnchor.BottomEnd with { AdjustSize = true, ConstrainToScreen = true } );
	}

	void TryGetOption()
	{
		// No need to check if not embedded
		if ( Resource is null || !Resource.EmbeddedResource.HasValue )
			return;

		//
		// If we're using the embed path, we don't want to use the generator at all
		//
		if ( Resource.EmbeddedResource.Value.ResourceCompiler == "embed" )
		{
			CurrentOption = "embed";
			return;
		}

		CurrentOption = Resource.EmbeddedResource.Value.ResourceGenerator;
	}

	void SwitchTo( string name = null )
	{
		CurrentOption = name;
		RebuildControl();
	}

	/// <summary>
	/// Rebuild the resource editing control
	/// </summary>
	public void RebuildControl()
	{
		Body.Clear( true );

		var types = EditorTypeLibrary.GetGenericTypes( typeof( ResourceGenerator<> ), [SerializedProperty.PropertyType] );
		var type = types.FirstOrDefault( x => x.ClassName == CurrentOption );

		Widget editor = null;

		// Which editor should we use?
		{
			if ( type is not null && type.Create<ResourceGenerator>() is var generator )
			{
				editor = ResourceGeneratorControlWidget.Create( generator, SerializedProperty );

				if ( Button.IsValid() )
					Button.Icon = DisplayInfo.For( generator ).Icon;
			}
			else if ( CurrentOption == "embed" )
			{
				editor = EmbeddedResourceControlWidget.CreateWidget( SerializedProperty );

				if ( Button.IsValid() )
					Button.Icon = "document_scanner";
			}
			else
			{
				editor = ControlWidget.Create( SerializedProperty );

				if ( Button.IsValid() )
					Button.Icon = "folder";
			}
		}

		if ( editor is not null )
		{
			Body.Add( editor );
		}
	}

	protected override void PaintUnder()
	{
		// nothing
	}
}
