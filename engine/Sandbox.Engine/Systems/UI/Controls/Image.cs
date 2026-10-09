namespace Sandbox.UI
{
	/// <summary>
	/// A generic box that displays a given texture within itself.
	/// </summary>
	[Library( "image" ), Alias( "img" ), Expose]
	public partial class Image : Panel
	{
		Vector2 _textureSize;
		int _textureVersion;

		/// <summary>
		/// The texture being displayed by this panel.
		/// </summary>
		public Texture Texture
		{
			get;
			set
			{
				if ( field == value ) return;
				field = value;
				_textureSize = value.IsValid() ? value.Size : default;
				_textureVersion = value?.DirtyVersion ?? 0;
				LayoutTree.MarkDirty();
				SetNeedsPreLayout();
			}
		}

		public Image()
		{
			LayoutTree.SetMeasureFunction( MeasureTexture );
		}

		/// <summary>
		/// Set <see cref="Texture"/> from a file path. URLs supported.
		/// </summary>
		public async void SetTexture( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) return;
			if ( !IsValid ) return;

			var texture = await Texture.LoadAsync( name );

			if ( !IsValid ) return;
			Texture = texture;
		}

		public override void Tick()
		{
			base.Tick();

			// Loading can replace the contents of the same cached Texture wrapper.
			if ( Texture is null || Texture.DirtyVersion == _textureVersion ) return;
			_textureVersion = Texture.DirtyVersion;

			var size = Texture.IsValid() ? Texture.Size : default;
			if ( size == _textureSize ) return;
			_textureSize = size;

			LayoutTree.MarkDirty();
			SetNeedsPreLayout();
		}

		float oldScaleToScreen = 1.0f;
		internal override void PreLayout( LayoutCascade cascade )
		{
			base.PreLayout( cascade );

			if ( ScaleToScreen != oldScaleToScreen )
			{
				LayoutTree.MarkDirty();
			}
		}

		public override void OnDraw( Painter painter )
		{
			var size = ComputedStyle?.ObjectFit switch
			{
				ObjectFit.Contain => Length.Contain,
				ObjectFit.Cover => Length.Cover,
				ObjectFit.Fill => Length.Percent( 100 ).Value,
				_ => Length.Auto,
			};
			DrawTexture( painter, Texture, size );
		}

		public override void SetProperty( string name, string value )
		{
			base.SetProperty( name, value );

			if ( name == "src" ) SetTexture( value );
		}

		internal Vector2 MeasureTexture( float width, Sandbox.Layout.MeasureMode widthMode, float height, Sandbox.Layout.MeasureMode heightMode )
		{
			if ( !Texture.IsValid() ) return default;

			try
			{
				var (w, h) = (Texture.Width, Texture.Height);
				var exact = Sandbox.Layout.MeasureMode.Exactly;
				var atMost = Sandbox.Layout.MeasureMode.AtMost;

				oldScaleToScreen = ScaleToScreen;
				var ideal = new Vector2( w * ScaleToScreen, h * ScaleToScreen );

				if ( widthMode == exact ) return new Vector2( width, h * width / w );
				if ( heightMode == exact ) return new Vector2( w * height / h, height );

				if ( widthMode == atMost && heightMode == atMost && (width < ideal.x || height < ideal.y) )
				{
					float scale = Math.Min( width / w, height / h );
					return new Vector2( w * scale, h * scale );
				}

				if ( widthMode == atMost && width < ideal.x ) return new Vector2( width, h * width / w );
				if ( heightMode == atMost && height < ideal.y ) return new Vector2( w * height / h, height );

				return ideal;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
				return default;
			}
		}

	}

	namespace Construct
	{
		public static class ImageConstructor
		{
			/// <summary>
			/// Create an image with given texture and CSS classname.
			/// </summary>
			public static Image Image( this PanelCreator self, string image = null, string classname = null )
			{
				var control = self.panel.AddChild<Image>();

				if ( image != null )
					control.SetTexture( image );

				if ( classname != null )
					control.AddClass( classname );

				return control;
			}
		}
	}
}
