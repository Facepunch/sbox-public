using Sandbox.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// Turns a sliced sheet into a character: a sprite asset per part, and a parented hierarchy of
/// GameObjects in the open scene.
/// <para>
/// It cannot assemble the character for you. A sheet has the limbs spread out with space between
/// them, so it records what the parts are but not how they fit together - the parts land laid out as
/// they are on the sheet and get dragged into place. What it does do is the work that is tedious to
/// do by hand and easy to get wrong: parenting, per-part size, sort order, and pivots that are
/// already on the joints.
/// </para>
/// </summary>
public class RigBuilder : Dialog
{
	readonly Window _editor;

	/// <summary>
	/// How many source pixels make one world unit. Only relative sizes matter for a rig, so this is
	/// really a "how big is the character" dial.
	/// </summary>
	int _pixelsPerUnit = 100;

	string _sortLayerName;
	bool _rebuildSprites = true;

	ScrollArea _list;
	Label _status;

	// Combo boxes assign CurrentIndex while being filled, and that reaches the item's action whether
	// or not anybody picked anything. Without this, building the list rewrites every parent.
	bool _building;

	public RigBuilder( Window editor ) : base( editor, false )
	{
		_editor = editor;

		Window.Title = "Build Rig";
		Window.WindowTitle = "Build Rig";
		Window.Size = new Vector2( 520, 620 );
		Window.SetModal( true );

		_sortLayerName = editor.Asset?.Name ?? "Sprites";
		_pixelsPerUnit = EditorCookie.Get( "SpriteSheetEditor.PixelsPerUnit", 100 );

		BuildLayout();
	}

	void BuildLayout()
	{
		Layout = Layout.Column();
		Layout.Margin = 12;
		Layout.Spacing = 8;

		Layout.Add( new Label(
			"Each part becomes a sprite and a GameObject. Set what hangs off what - a calf's parent is " +
			"a thigh - then build, and drag the parts together in the scene." )
		{
			WordWrap = true,
			Color = Theme.TextControl.WithAlpha( 0.7f )
		} );

		//
		// Parents
		//

		var header = Layout.AddRow();
		header.Spacing = 6;
		header.Add( new Label( "Hierarchy" ) );
		header.AddStretchCell();

		var guess = new Button( "Guess from names", "auto_fix_high" );
		guess.ToolTip = "Work the hierarchy out from part names like L_Thigh and R_Calf";
		guess.Clicked = GuessHierarchy;
		header.Add( guess );

		var clear = new Button( "Clear", "clear" );
		clear.Clicked = () =>
		{
			foreach ( var slice in _editor.Sheet.Slices ) slice.ParentId = Guid.Empty;
			_editor.SetModified();
			RebuildList();
		};
		header.Add( clear );

		_list = new ScrollArea( this );
		_list.Canvas = new Widget();
		_list.Canvas.Layout = Layout.Column();
		_list.Canvas.Layout.Spacing = 2;
		_list.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_list.Canvas.HorizontalSizeMode = SizeMode.Flexible;
		Layout.Add( _list, 1 );

		//
		// Options
		//

		var options = new ControlSheet();
		options.AddRow( this.GetSerialized().GetProperty( nameof( PixelsPerUnit ) ) );
		options.AddRow( this.GetSerialized().GetProperty( nameof( SortLayer ) ) );
		options.AddRow( this.GetSerialized().GetProperty( nameof( RebuildSprites ) ) );
		Layout.Add( options );

		_status = new Label( "" ) { WordWrap = true, Color = Theme.Yellow };
		Layout.Add( _status );

		var buttons = Layout.AddRow();
		buttons.Spacing = 6;
		buttons.AddStretchCell();

		var cancel = new Button( "Cancel" );
		cancel.Clicked = Close;
		buttons.Add( cancel );

		var build = new Button.Primary( "Build Rig", "accessibility_new" );
		build.Clicked = Build;
		buttons.Add( build );

		RebuildList();
	}

	//
	// Bound options - properties so a ControlSheet can edit them
	//

	[Property, Title( "Pixels Per Unit" ), Range( 1, 512, true, false ), Step( 1 )]
	public int PixelsPerUnit
	{
		get => _pixelsPerUnit;
		set { _pixelsPerUnit = Math.Max( 1, value ); EditorCookie.Set( "SpriteSheetEditor.PixelsPerUnit", _pixelsPerUnit ); }
	}

	/// <summary>
	/// The sorting layer every part is put on. Created if it does not exist.
	/// </summary>
	[Property, Title( "Sort Layer" )]
	public string SortLayer
	{
		get => _sortLayerName;
		set => _sortLayerName = value;
	}

	/// <summary>
	/// Write the per-part sprite assets again. Turn off to keep sprites you have since edited by hand.
	/// </summary>
	[Property, Title( "Rewrite Sprites" )]
	public bool RebuildSprites
	{
		get => _rebuildSprites;
		set => _rebuildSprites = value;
	}

	//
	// The parent list
	//

	void RebuildList()
	{
		_building = true;

		try
		{
			var layout = _list.Canvas.Layout;
			layout.Clear( true );

			var sheet = _editor.Sheet;
			if ( sheet is null ) return;

			foreach ( var slice in sheet.Slices )
			{
				layout.Add( BuildRow( sheet, slice ) );
			}

			layout.AddStretchCell();
		}
		finally
		{
			_building = false;
		}
	}

	Widget BuildRow( SpriteSheet sheet, SpriteSheet.Slice slice )
	{
		var row = new Widget();
		row.Layout = Layout.Row();
		row.Layout.Spacing = 6;
		row.Layout.Margin = new Sandbox.UI.Margin( 4, 2, 4, 2 );
		row.FixedHeight = 28;

		var name = new Label( slice.Name );
		name.MinimumWidth = 150;
		row.Layout.Add( name );

		var combo = new ComboBox( row );

		combo.AddItem( "(root)",
			onSelected: () => SetParent( sheet, slice, Guid.Empty ),
			selected: slice.ParentId == Guid.Empty );

		foreach ( var candidate in sheet.Slices )
		{
			if ( candidate == slice ) continue;

			var id = candidate.Id;
			combo.AddItem( candidate.Name,
				onSelected: () => SetParent( sheet, slice, id ),
				selected: slice.ParentId == id );
		}

		row.Layout.Add( combo, 1 );

		return row;
	}

	void SetParent( SpriteSheet sheet, SpriteSheet.Slice slice, Guid parentId )
	{
		if ( _building ) return;

		if ( !sheet.TrySetParent( slice, parentId ) )
		{
			_status.Text = $"{slice.Name} cannot be a child of that - it would make a loop.";
			RebuildList();
			return;
		}

		_status.Text = "";
		_editor.SetModified();
	}

	//
	// Guessing
	//

	// Part name -> the part it hangs off. Ordered longest-first when matched, so "upperarm" is not
	// mistaken for "arm".
	static readonly (string Part, string Parent)[] CommonHierarchy =
	{
		("torso", "hips"), ("chest", "hips"), ("body", "hips"), ("spine", "hips"),
		("head", "torso"), ("neck", "torso"), ("hair", "head"),
		("upperarm", "torso"), ("shoulder", "torso"), ("arm", "torso"),
		("forearm", "arm"), ("lowerarm", "arm"), ("elbow", "arm"),
		("hand", "forearm"),
		("thigh", "hips"), ("upperleg", "hips"), ("leg", "hips"),
		("calf", "thigh"), ("shin", "thigh"), ("lowerleg", "thigh"), ("knee", "thigh"),
		("foot", "calf"), ("shoe", "calf"),
	};

	/// <summary>
	/// Fill the hierarchy in from the part names, matching left to left and right to right.
	/// </summary>
	void GuessHierarchy()
	{
		var sheet = _editor.Sheet;
		if ( sheet is null ) return;

		var matched = 0;

		foreach ( var slice in sheet.Slices )
		{
			var (side, bare) = SplitSide( slice.Name );

			// Longest match first, so "lowerarm" beats "arm".
			var rule = CommonHierarchy
				.Where( r => bare.Contains( r.Part, StringComparison.OrdinalIgnoreCase ) )
				.OrderByDescending( r => r.Part.Length )
				.FirstOrDefault();

			if ( rule.Parent is null ) continue;

			// Prefer a parent on the same side, then one with no side at all - a left calf belongs to
			// the left thigh, but both legs hang off the one set of hips.
			var parent = FindPart( sheet, rule.Parent, side ) ?? FindPart( sheet, rule.Parent, "" );
			if ( parent is null || parent == slice ) continue;

			if ( sheet.TrySetParent( slice, parent.Id ) ) matched++;
		}

		_editor.SetModified();
		RebuildList();

		_status.Text = matched > 0
			? $"Matched {matched} of {sheet.Slices.Count} parts. Check the rest by hand."
			: "Nothing matched. Names like Hips, Torso, L_Thigh, L_Calf are what this looks for.";
	}

	/// <summary>Split a leading or trailing side marker off a part name.</summary>
	static (string Side, string Bare) SplitSide( string name )
	{
		var lower = name.ToLowerInvariant();

		foreach ( var (marker, side) in new[]
		{
			("l_", "l"), ("r_", "r"), ("left_", "l"), ("right_", "r"),
			("left", "l"), ("right", "r")
		} )
		{
			if ( lower.StartsWith( marker, StringComparison.Ordinal ) )
				return (side, lower[marker.Length..]);

			if ( lower.EndsWith( "_" + marker.TrimEnd( '_' ), StringComparison.Ordinal ) )
				return (side, lower[..^(marker.TrimEnd( '_' ).Length + 1)]);
		}

		return ("", lower);
	}

	static SpriteSheet.Slice FindPart( SpriteSheet sheet, string part, string side )
	{
		foreach ( var slice in sheet.Slices )
		{
			var (sliceSide, bare) = SplitSide( slice.Name );
			if ( sliceSide != side ) continue;
			if ( bare.Contains( part, StringComparison.OrdinalIgnoreCase ) ) return slice;
		}

		return null;
	}

	//
	// Building
	//

	void Build()
	{
		var sheet = _editor.Sheet;

		if ( sheet is null || sheet.Slices.Count == 0 )
		{
			_status.Text = "Nothing to build - slice the sheet first.";
			return;
		}

		if ( _editor.Asset is null )
		{
			_status.Text = "Save the sheet before building, so the part sprites have somewhere to live.";
			return;
		}

		if ( SceneEditorSession.Active is null )
		{
			_status.Text = "No scene open to build into.";
			return;
		}

		var sprites = CreatePartSprites( sheet );
		if ( sprites is null ) return;

		BuildHierarchy( sheet, sprites );

		Close();
	}

	/// <summary>
	/// One sprite asset per part, in a folder beside the sheet.
	/// </summary>
	/// <remarks>
	/// A part gets a whole sprite rather than being one animation inside a shared one, so that its
	/// Animations list stays free for actual animation - a head that blinks, a torch that flickers -
	/// on top of whatever the rig does by rotating it.
	/// </remarks>
	Dictionary<Guid, Sprite> CreatePartSprites( SpriteSheet sheet )
	{
		var sheetFile = _editor.Asset.GetSourceFile( true );
		var directory = Path.GetDirectoryName( sheetFile );

		if ( string.IsNullOrEmpty( directory ) )
		{
			_status.Text = "Could not work out where to put the part sprites.";
			return null;
		}

		var folder = Path.Combine( directory, $"{Path.GetFileNameWithoutExtension( sheetFile )}_parts" );
		Directory.CreateDirectory( folder );

		var result = new Dictionary<Guid, Sprite>();

		foreach ( var slice in sheet.Slices )
		{
			var path = Path.Combine( folder, $"{SanitiseFileName( slice.Name )}.sprite" );
			var existing = AssetSystem.FindByPath( path );

			if ( existing is not null && !_rebuildSprites )
			{
				var loaded = existing.LoadResource<Sprite>();
				if ( loaded is not null ) { result[slice.Id] = loaded; continue; }
			}

			// The texture is a live reference to the slice, not a baked copy - re-slicing or nudging
			// a rect reaches this sprite without anything being rebuilt.
			var generator = new SpriteSheetSliceGenerator
			{
				Sheet = sheet,
				SliceId = slice.Id,
				SliceName = slice.Name
			};

			var texture = generator.Create( ResourceGenerator.Options.Default );
			if ( texture is null )
			{
				_status.Text = $"Could not build a texture for {slice.Name}.";
				return null;
			}

			var sprite = new Sprite
			{
				Animations = new List<Sprite.Animation>
				{
					new()
					{
						Name = "Default",
						// The joint. This is what makes the part turn about the right point once the
						// hierarchy is assembled.
						Origin = slice.Pivot,
						Frames = new List<Sprite.Frame> { new() { Texture = texture } }
					}
				}
			};

			var asset = existing ?? AssetSystem.CreateResource( "sprite", path );
			asset.SaveToDisk( sprite );

			result[slice.Id] = asset.LoadResource<Sprite>() ?? sprite;
		}

		return result;
	}

	static string SanitiseFileName( string name )
	{
		foreach ( var c in Path.GetInvalidFileNameChars() ) name = name.Replace( c, '_' );
		return name;
	}

	void BuildHierarchy( SpriteSheet sheet, Dictionary<Guid, Sprite> sprites )
	{
		var sorting = ProjectSettings.Sorting;
		var layer = sorting.GetLayer( _sortLayerName ) ?? sorting.AddLayer( _sortLayerName );

		var centre = new Vector2( _editor.SourceWidth, _editor.SourceHeight ) * 0.5f;

		using var scope = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Build Sprite Rig" ).WithGameObjectCreations().Push() )
		{
			var root = new GameObject( true, _editor.Asset?.Name ?? "Rig" );
			var objects = new Dictionary<Guid, GameObject>();

			var order = 0;

			foreach ( var slice in sheet.InHierarchyOrder() )
			{
				var go = new GameObject( true, slice.Name );

				go.Parent = slice.ParentId != Guid.Empty && objects.TryGetValue( slice.ParentId, out var parent )
					? parent
					: root;

				var renderer = go.Components.GetOrCreate<SpriteRenderer>();

				if ( sprites.TryGetValue( slice.Id, out var sprite ) )
					renderer.Sprite = sprite;

				// Size sets the longer side; the shorter one follows the texture's aspect. So feeding
				// it the larger pixel dimension is what keeps a hand a hand and a torso a torso.
				var longest = MathF.Max( slice.Rect.Width, slice.Rect.Height );
				renderer.Size = new Vector2( longest, longest ) / _pixelsPerUnit;

				// Sorting is ignored entirely while this is off, so a rig built without it would come
				// out in an arbitrary order and look broken for no visible reason.
				renderer.IsSorted = true;
				renderer.SortLayer = new SortLayerHandle( layer.Id );
				renderer.SortOrder = order++;

				// Laid out as they sit on the sheet. Parented first, so this resolves to the right
				// local offset. Positioning by the pivot rather than the rect corner reproduces the
				// sheet exactly, because the pivot is where the object's origin actually is.
				var pivot = slice.PivotToImagePixels;
				go.WorldPosition = new Vector3(
					0,
					(pivot.x - centre.x) / _pixelsPerUnit,
					-(pivot.y - centre.y) / _pixelsPerUnit );

				objects[slice.Id] = go;
			}

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( root );
		}
	}
}
