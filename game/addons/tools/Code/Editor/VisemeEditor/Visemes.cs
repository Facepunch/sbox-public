using System;
using Sandbox.UI;

namespace Editor.VisemeEditor;

public class Visemes : Widget
{
	private class VisemeItem
	{
		public string Name;
		public string Desc;
	}

	private static readonly VisemeItem[] VisemeList = new VisemeItem[]
	{
		new VisemeItem { Name = "sil", Desc = "Silence" },
		new VisemeItem { Name = "PP", Desc = "Put, Bat, Mat" },
		new VisemeItem { Name = "FF", Desc = "Fat, Vat" },
		new VisemeItem { Name = "TH", Desc = "Think, That" },
		new VisemeItem { Name = "DD", Desc = "Tip, Doll" },
		new VisemeItem { Name = "kk", Desc = "Call, Gas" },
		new VisemeItem { Name = "CH", Desc = "Chair, Join, She" },
		new VisemeItem { Name = "SS", Desc = "Sir, Zeal" },
		new VisemeItem { Name = "nn", Desc = "Lot, Not" },
		new VisemeItem { Name = "RR", Desc = "Red" },
		new VisemeItem { Name = "aa", Desc = "Car" },
		new VisemeItem { Name = "E", Desc = "Bed" },
		new VisemeItem { Name = "I", Desc = "Tip" },
		new VisemeItem { Name = "O", Desc = "Toe" },
		new VisemeItem { Name = "u", Desc = "Book" },
	};

	private readonly ListView ListView;
	private readonly Widget FilterClear;
	private readonly Dictionary<string, Pixmap> Pixmaps = new();

	private SceneWorld World;
	private SceneCamera Camera;
	private SceneModel SceneObject;

	public event Action<string> OnSelectionChanged;

	public string VisemeSelected
	{
		set
		{
			ListView.SelectItem( value == null ? VisemeList.FirstOrDefault() :
				VisemeList.FirstOrDefault( x => x.Name == value ), true, true );
		}
	}

	public Model Model { set => CreateSceneObject( value ); }

	private void CreateSceneObject( Model model )
	{
		if ( model == null || model.IsError )
			return;

		if ( model.MorphCount == 0 )
			return;

		if ( SceneObject.IsValid() )
		{
			SceneObject.Delete();
			SceneObject = null;
		}

		SceneObject = new SceneModel( World, model, Transform.Zero.WithPosition( Vector3.Backward * 250 ) );
		SceneObject.UseAnimGraph = false;

		var position = Vector3.Zero;
		var attachment = SceneObject.GetAttachment( "eyes" );
		if ( attachment.HasValue )
			position = attachment.Value.Position;

		Camera.Position = position + Vector3.Down * 1.0f + Camera.Rotation.Backward * 110;

		SceneObject.Update( 0.05f );

		Pixmaps.Clear();

		for ( int j = 0; j < VisemeList.Length; ++j )
		{
			var pixmap = new Pixmap( 128 );
			Camera.RenderToPixmap( pixmap );
			Pixmaps.Add( VisemeList[j].Name, pixmap );
		}
	}

	public Visemes( Widget parent ) : base( parent )
	{
		Name = "Visemes";
		WindowTitle = "Visemes";
		SetWindowIcon( "abc" );

		MinimumWidth = 141;

		Layout = Layout.Column();

		var toolbar = new ToolBar( this );
		Layout.Add( toolbar );

		var filter = new LineEdit( this );
		filter.PlaceholderText = "Filter Visemes..";
		filter.TextEdited += UpdateList;
		toolbar.AddWidget( filter );

		FilterClear = new Widget( filter );
		FilterClear.Visible = false;
		FilterClear.FixedHeight = 22;
		FilterClear.FixedWidth = 22;
		FilterClear.MouseClick = () => { filter.Text = ""; UpdateList(); };
		FilterClear.Cursor = CursorShape.Finger;
		FilterClear.OnPaintOverride = () =>
		{
			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( FilterClear.LocalRect );
			Paint.SetPen( Theme.Text.WithAlpha( Paint.HasMouseOver ? 1.0f : 0.5f ) );
			Paint.DrawIcon( FilterClear.LocalRect, "close", 14 );
			return true;
		};

		filter.Layout = Layout.Row();
		filter.Layout.Add( FilterClear );
		filter.Layout.AddStretchCell();

		ListView = new ListView( this );
		ListView.Margin = new Margin( 3, 0, 14, 4 );
		ListView.ItemSize = new Vector2( 128, 128 );
		ListView.ItemAlign = Align.Center;
		ListView.ItemSpacing = 4;
		ListView.ItemPaint = PaintItem;
		ListView.ItemSelected += ( e ) =>
		{
			if ( e is VisemeItem viseme )
				OnSelectionChanged?.Invoke( viseme.Name );
		};

		Layout.Add( ListView, 1 );

		UpdateList();

		World = new SceneWorld();
		Camera = new SceneCamera
		{
			World = World,
			AmbientLightColor = Color.White * 0.0f,
			ZNear = 0.1f,
			ZFar = 4000,
			EnablePostProcessing = true,
			Angles = new Angles( 0, 180, 0 ),
			FieldOfView = 10,
			AntiAliasing = true,
			BackgroundColor = Color.Transparent
		};

		new ScenePointLight( World, new Vector3( 100, 100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
		new ScenePointLight( World, new Vector3( -100, -100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
		new SceneCubemap( World, Texture.Load( "textures/cubemaps/default.vtex" ), BBox.FromPositionAndSize( Vector3.Zero, 5000 ) );
	}

	public void SetMorph( string viseme, string name, float value )
	{
		if ( !Pixmaps.TryGetValue( viseme, out var pixmap ) )
			return;

		var morphs = SceneObject.Morphs;
		morphs.Set( name, value );
		SceneObject.Update( 0.05f );
		SceneObject.Update( 0.05f );
		Camera.RenderToPixmap( pixmap );

		Update();
	}

	public void SetMorphs( string viseme, Dictionary<string, float> morphs )
	{
		if ( !Pixmaps.TryGetValue( viseme, out var pixmap ) )
			return;

		SceneObject.Morphs.ResetAll();

		if ( morphs != null )
		{
			foreach ( var morph in morphs )
			{
				SceneObject.Morphs.Set( morph.Key, morph.Value );
			}
		}

		SceneObject.Update( 0.05f );
		SceneObject.Update( 0.05f );
		Camera.RenderToPixmap( pixmap );

		Update();
	}

	private void PaintItem( VirtualWidget v )
	{
		if ( v.Object is not VisemeItem viseme )
			return;

		if ( !Pixmaps.TryGetValue( viseme.Name, out var pixmap ) )
			return;

		var textRext = v.Rect.Shrink( 4 );

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.SetDefaultFont( 8, 1000 );

		var bg = v.Selected ? Theme.Blue.Darken( 0.5f ) : Theme.ControlBackground.WithAlpha( 0.7f ).Lighten( v.Hovered ? 0.5f : 0.0f );

		Paint.ClearPen();
		Paint.SetBrush( bg );
		Paint.DrawRect( v.Rect, 4 );

		Paint.Draw( v.Rect, pixmap );

		var r = v.Rect.Shrink( 0, v.Rect.Height - 22, 0, 0 );
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.8f ) );
		Paint.DrawRect( r, 2 );

		Paint.SetPen( Theme.Text.WithAlpha( 0.8f ) );
		Paint.DrawText( textRext, viseme.Name, TextFlag.Top );

		Paint.SetPen( Theme.Text.WithAlpha( v.Hovered || v.Selected ? 0.9f : 0.6f ) );
		Paint.DrawText( r, viseme.Desc, TextFlag.Center );
	}

	private IEnumerable<VisemeItem> GetItems( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return VisemeList;

		return VisemeList.Where( e => e.Name.Contains( text, StringComparison.OrdinalIgnoreCase ) ||
			e.Desc.Contains( text, StringComparison.OrdinalIgnoreCase ) );
	}

	public void UpdateList( string text = null )
	{
		FilterClear.Visible = !string.IsNullOrEmpty( text );
		ListView.SetItems( GetItems( text ) );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		World?.Delete();
		World = null;
		Camera = null;
		SceneObject = null;
	}
}
