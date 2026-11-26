using Sandbox.Resources;

namespace Editor;

/// <summary>
/// Pops up when editing a TextureGenerator's properties
/// </summary>
class TextureGeneratorPopup : PopupEditor, IPopupEditor<TextureGenerator>
{
	TextureWidget texturePreview;

	public TextureGeneratorPopup( SerializedObject so, Widget parent ) : base( so, parent )
	{
	}

	public override void Initialize()
	{
		Layout = Layout.Row();

		var left = Layout.AddColumn();
		texturePreview = left.Add( new TextureWidget() );
		texturePreview.FixedSize = 186f;
		left.AddStretchCell();
		left.Margin = 16;

		var right = Layout.AddColumn( 1 );

		var scrollArea = new ScrollArea( this );
		scrollArea.Canvas = new Widget( this );
		scrollArea.NoSystemBackground = true;
		scrollArea.TranslucentBackground = true;
		scrollArea.SetStyles( "QScrollArea { border: 0px solid red; }" );

		scrollArea.Canvas.Layout = Layout.Column();
		scrollArea.Canvas.Layout.Margin = 8;
		scrollArea.Canvas.Layout.Add( ControlSheet.Create( SerializedObject ) );
		scrollArea.Canvas.Layout.AddStretchCell();

		MinimumWidth = 768;

		scrollArea.Canvas.UpdateGeometry();
		scrollArea.Canvas.AdjustSize();

		right.Add( scrollArea, 1 );

		var height = scrollArea.Canvas.Height + 8;
		if ( height > 600 ) height = 600;

		QueueTextureGeneration();
	}

	public override void OnPropertyChanged( SerializedProperty property )
	{
		QueueTextureGeneration();
	}

	bool _generatingTexture = false;
	bool _isDirty = false;

	public void QueueTextureGeneration()
	{
		_isDirty = true;
	}

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		if ( _generatingTexture ) return;
		if ( !_isDirty ) return;

		_isDirty = false;
		_ = UpdateTexture();
	}

	async Task UpdateTexture()
	{
		_generatingTexture = true;

		try
		{
			var gen = SerializedObject.Targets.OfType<TextureGenerator>().FirstOrDefault();
			if ( gen is null ) return;

			var preview = texturePreview;
			Texture texture;

			try
			{
				texture = await gen.CreateAsync( TextureGenerator.Options.Default, default );
			}
			catch ( Exception ex ) when ( ex is not OperationCanceledException )
			{
				Log.Warning( ex, $"Couldn't generate texture preview with {gen.GetType().FullName}" );
				return;
			}

			preview.Texture = texture;
		}
		finally
		{
			_generatingTexture = false;
		}
	}
}
