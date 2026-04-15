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
			public static Material Basic { get; internal set; } = FromShader( "shaders/ui_basic.shader" );

			/// <summary>
			/// CSS Box rendering
			/// </summary>
			public static Material Box { get; internal set; } = FromShader( "shaders/ui_cssbox.shader" );

			/// <summary>
			/// Batched CSS Box rendering — reads per-box data from a StructuredBuffer
			/// </summary>
			internal static Material BatchedBox { get; set; } = FromShader( "shaders/ui_cssbox_batched.shader" );

			/// <summary>
			/// CSS Box Shadow rendering
			/// </summary>
			internal static Material BoxShadow { get; set; } = FromShader( "shaders/ui_cssshadow.shader" );

			/// <summary>
			/// CSS Text Rendering
			/// </summary>
			internal static Material Text { get; set; } = FromShader( "shaders/ui_text.shader" );
			internal static Material BackdropFilter { get; set; } = FromShader( "shaders/ui_backdropfilter.shader" );
			internal static Material Filter { get; set; } = FromShader( "shaders/ui_filter.shader" );

			/// <summary>
			/// For filter: border-wrap( ... );
			/// </summary>
			internal static Material BorderWrap { get; set; } = FromShader( "shaders/ui_borderwrap.shader" );

			/// <summary>
			/// For filter: drop-shadow( ... );
			/// </summary>
			internal static Material DropShadow { get; set; } = FromShader( "shaders/ui_dropshadow.shader" );

			/// <summary>
			/// CSS Outline rendering
			/// </summary>
			internal static Material Outline { get; set; } = FromShader( "shaders/ui_cssoutline.shader" );
		}
	}
}
