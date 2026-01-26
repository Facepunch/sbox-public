using System;
using Sandbox.UI;

namespace Editor.VisemeEditor;

public class MorphSlider : Widget
{
	private readonly Pixmap Pixmap;
	private readonly FloatSlider FloatSlider;
	private readonly LineEdit LineEdit;
	private static string Format => "0.00";

	public event Action OnValueEdited;

	public float Value
	{
		get => FloatSlider.Value;

		set
		{
			FloatSlider.Value = value;
			LineEdit.Text = FloatSlider.Value.ToString( Format );
		}
	}

	public MorphSlider( Widget parent, string name, Pixmap pixmap ) : base( parent )
	{
		Pixmap = pixmap;

		Layout = Layout.Column();
		FixedHeight = 64;
		Layout.Margin = new Margin( 64 + 8, 8, 8, 8 );
		SetSizeMode( SizeMode.CanShrink, SizeMode.Default );

		var w = new Widget( this );
		w.Layout = Layout.Row();
		w.Layout.Add( new Label( name, w ) );
		w.Layout.AddStretchCell( 1 );
		LineEdit = new LineEdit( "0", w );
		LineEdit.FixedHeight = 22;
		LineEdit.FixedWidth = 35;
		LineEdit.TextEdited += LineEdit_TextEdited;
		LineEdit.EditingFinished += LineEdit_EditingFinished;
		LineEdit.NoSystemBackground = true;
		LineEdit.TranslucentBackground = true;
		LineEdit.Alignment = TextFlag.RightCenter;
		w.Layout.Add( LineEdit );

		Layout.Add( w );
		Layout.AddStretchCell( 1 );

		FloatSlider = new FloatSlider( this );
		FloatSlider.Minimum = 0;
		FloatSlider.Maximum = 1;
		FloatSlider.OnValueEdited = () =>
		{
			Value = FloatSlider.Value;
			SignalValuesChanged();
			OnValueEdited?.Invoke();
		};
		Layout.Add( FloatSlider );

		Value = 0;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.7f ) );
		Paint.DrawRect( LocalRect, 4 );
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.7f ) );
		Paint.DrawRect( new Rect( 0, 0, 64, 64 ), 4 );
		Paint.Draw( new Rect( 0, 0, 64, 64 ).Shrink( 3 ), Pixmap );

		base.OnPaint();
	}

	private void LineEdit_EditingFinished()
	{
		FloatSlider.Value = LineEdit.Text.ToFloat();
		LineEdit.Text = FloatSlider.Value.ToString( Format );
		SignalValuesChanged();
		OnValueEdited?.Invoke();
	}

	private void LineEdit_TextEdited( string obj )
	{
		FloatSlider.Value = LineEdit.Text.ToFloat();
		SignalValuesChanged();
		OnValueEdited?.Invoke();
	}
}

public class Morphs : Widget
{
	public event Action<string, float> OnValueEdited;
	public event Action OnReset;

	private readonly Dictionary<string, MorphSlider> Sliders = new();

	private readonly Widget Canvas;
	private readonly Widget FilterClear;

	public Model Model { set => CreateSliders( value ); }

	public Morphs( Widget parent ) : base( parent )
	{
		Name = "Morphs";
		WindowTitle = "Morphs";
		SetWindowIcon( "tune" );

		MinimumWidth = 270;

		Layout = Layout.Column();

		var toolbar = new ToolBar( this );
		toolbar.SetIconSize( 18 );
		Layout.Add( toolbar );

		var filter = new LineEdit( this );
		filter.PlaceholderText = "Filter Morphs..";
		filter.TextEdited += UpdateList;
		toolbar.AddWidget( filter );
		toolbar.AddSeparator();
		toolbar.AddOption( "Reset Morphs", "replay", () => OnReset?.Invoke() );

		FilterClear = new Widget( filter );
		FilterClear.Visible = false;
		FilterClear.FixedHeight = 22;
		FilterClear.FixedWidth = 22;
		FilterClear.MouseClick = () => { filter.Text = ""; UpdateList( null ); };
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

		var scroll = new ScrollArea( this );
		scroll.VerticalScrollbarMode = ScrollbarMode.Auto;
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		Canvas = new Widget( this );
		Canvas.Layout = Layout.Column();
		Canvas.Layout.Spacing = 4;
		Canvas.Layout.Margin = new Margin( 3, 0, 14, 4 );

		scroll.Canvas = Canvas;
		Layout.Add( scroll );
	}

	private void CreateSliders( Model model )
	{
		if ( model == null || model.IsError )
			return;

		if ( model.MorphCount == 0 )
			return;

		Canvas.DestroyChildren();
		Sliders.Clear();

		var world = new SceneWorld();
		using var camera = new SceneCamera
		{
			World = world,
			AmbientLightColor = Color.White * 0.0f,
			ZNear = 0.1f,
			ZFar = 4000,
			EnablePostProcessing = true,
			Angles = new Angles( 0, 180, 0 ),
			FieldOfView = 10,
			AntiAliasing = true,
			BackgroundColor = Color.Transparent
		};

		new SceneLight( world, new Vector3( 100, 100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
		new SceneLight( world, new Vector3( -100, -100, 100 ), 500, Color.White * 4 ).ShadowsEnabled = false;
		new SceneCubemap( world, Texture.Load( "textures/cubemaps/default.vtex" ), BBox.FromPositionAndSize( Vector3.Zero, 5000 ) );
		var sceneObject = new SceneModel( world, model, Transform.Zero.WithPosition( Vector3.Backward * 250 ) );
		sceneObject.UseAnimGraph = false;

		var position = Vector3.Zero;
		var attachment = sceneObject.GetAttachment( "eyes" );
		if ( attachment.HasValue )
			position = attachment.Value.Position;

		camera.Position = position + Vector3.Down * 1.0f + camera.Rotation.Backward * 60;

		var morphs = sceneObject.Morphs;
		for ( int i = 0; i < morphs.Count; ++i )
		{
			morphs.ResetAll();
			morphs.Set( i, 1.0f );
			sceneObject.Update( 0.05f );
			var pixmap = new Pixmap( 58 );
			sceneObject.Update( 0.05f );
			camera.RenderToPixmap( pixmap );
			var name = morphs.GetName( i );
			var slider = new MorphSlider( this, name, pixmap );
			slider.OnValueEdited += () => OnValueEdited?.Invoke( name, slider.Value );
			Canvas.Layout.Add( slider, 1 );
			Sliders.Add( name, slider );
		}

		Canvas.Layout.AddStretchCell( 1 );

		world.Delete();
	}

	public void UpdateList( string text )
	{
		FilterClear.Visible = !string.IsNullOrEmpty( text );

		if ( string.IsNullOrWhiteSpace( text ) )
		{
			foreach ( var slider in Sliders )
			{
				slider.Value.Visible = true;
			}

			return;
		}

		foreach ( var slider in Sliders )
		{
			slider.Value.Visible = slider.Key.Contains( text, StringComparison.OrdinalIgnoreCase );
		}

		Update();
	}

	public void SetMorphs( Dictionary<string, float> morphs )
	{
		foreach ( var slider in Sliders )
		{
			slider.Value.Value = 0.0f;
		}

		if ( morphs != null )
		{
			foreach ( var morph in morphs )
			{
				if ( !Sliders.TryGetValue( morph.Key, out var slider ) )
					continue;

				slider.Value = morph.Value;
			}
		}
	}
}
