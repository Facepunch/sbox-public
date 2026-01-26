namespace Sandbox
{
	public partial class Material
	{

		/// <summary>
		/// Static materials for UI rendering purposes.
		/// </summary>
		public static class UI
		{
			/// <summary>
			/// As basic 2D drawing material. Supports Texture and vertex color.
			/// </summary>
			public static Material Basic { get; internal set; }

			/// <summary>
			/// CSS Box rendering
			/// </summary>
			public static Material Box { get; internal set; }

			/// <summary>
			/// CSS Box Shadow rendering
			/// </summary>
			internal static Material BoxShadow { get; set; }

			/// <summary>
			/// CSS Text Rendering
			/// </summary>
			internal static Material Text { get; set; }
			internal static Material BackdropFilter { get; set; }
			internal static Material Filter { get; set; }

			/// <summary>
			/// For filter: border-wrap( ... );
			/// </summary>
			internal static Material BorderWrap { get; set; }

			/// <summary>
			/// For filter: drop-shadow( ... );
			/// </summary>
			internal static Material DropShadow { get; set; }

			internal static void InitStatic()
			{
				Basic = FromShader( "shaders/ui_basic.shader" );
				Box = FromShader( "shaders/ui_cssbox.shader" );
				BoxShadow = FromShader( "shaders/ui_cssshadow.shader" );
				Text = FromShader( "shaders/ui_text.shader" );
				BackdropFilter = FromShader( "shaders/ui_backdropfilter.shader" );
				Filter = FromShader( "shaders/ui_filter.shader" );
				DropShadow = FromShader( "shaders/ui_dropshadow.shader" );
				BorderWrap = FromShader( "shaders/ui_borderwrap.shader" );
			}

			internal static void DisposeStatic()
			{
				Basic?.Dispose();
				Basic = null;
				Box?.Dispose();
				Box = null;
				BoxShadow?.Dispose();
				BoxShadow = null;
				Text?.Dispose();
				Text = null;
				BackdropFilter?.Dispose();
				BackdropFilter = null;
				Filter?.Dispose();
				Filter = null;
				DropShadow?.Dispose();
				DropShadow = null;
				BorderWrap?.Dispose();
				BorderWrap = null;
			}
		}
	}
}
