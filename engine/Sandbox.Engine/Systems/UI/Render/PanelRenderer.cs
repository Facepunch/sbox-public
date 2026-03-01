using Sandbox.Rendering;

namespace Sandbox.UI;

internal unsafe sealed partial class PanelRenderer
{
	[ConVar( ConVarFlags.Protected, Help = "Enable drawing text" )]
	public static bool ui_drawtext { get; set; } = true;

	public Rect Screen { get; internal set; }

	public void Render( RootPanel panel, float opacity = 1.0f )
	{
		ThreadSafe.AssertIsMainThread();

		Screen = panel.PanelBounds;

		MatrixStack.Clear();
		MatrixStack.Push( Matrix.Identity );
		Matrix = Matrix.Identity;

		RenderModeStack.Clear();
		RenderModeStack.Push( "normal" );
		RenderMode = null;
		SetRenderMode( "normal" );

		LayerStack?.Clear();

		DefaultRenderTarget = Graphics.RenderTarget;

		InitScissor( Screen, panel.CommandList );

		Render( panel, new RenderState { X = Screen.Left, Y = Screen.Top, Width = Screen.Width, Height = Screen.Height, RenderOpacity = opacity } );
	}

	/// <summary>
	/// Build command lists for a root panel and all its children.
	/// Called during the tick phase, before rendering.
	/// </summary>
	public void BuildCommandLists( RootPanel panel, float opacity = 1.0f )
	{
		ThreadSafe.AssertIsMainThread();

		Screen = panel.PanelBounds;

		// Initialize matrix state for build phase
		MatrixStack.Clear();
		MatrixStack.Push( Matrix.Identity );
		Matrix = Matrix.Identity;

		// Save off the default render target for layer restoration during build
		DefaultRenderTarget = Graphics.RenderTarget;

		LayerStack?.Clear();

		InitScissor( Screen, panel.CommandList );

		BuildCommandLists( (Panel)panel, new RenderState { X = Screen.Left, Y = Screen.Top, Width = Screen.Width, Height = Screen.Height, RenderOpacity = opacity } );
	}

	/// <summary>
	/// Build command lists for a panel and its children.
	/// </summary>
	public void BuildCommandLists( Panel panel, RenderState state )
	{
		if ( panel?.ComputedStyle == null )
			return;

		if ( !panel.IsVisible )
			return;

		// Build transform command list (sets GlobalMatrix and TransformMat attribute)
		BuildTransformCommandList( panel );

		// Update layer (creates render target if needed for filters/masks)
		panel.UpdateLayer( panel.ComputedStyle );

		//
		// Rebuild the command list if dirty
		//
		if ( panel.IsRenderDirty )
		{
			BuildCommandList( panel, ref state );

			//
			// Add Content = Text, Image (not children)
			//
			if ( panel.HasContent )
			{
				try
				{
					panel.DrawContent( panel.CommandList, this, ref state );
				}
				catch ( Exception e )
				{
					Log.Error( e );
				}
			}

			// Build post-children layer commands (for filters/masks)
			panel.BuildLayerPopCommands( this, DefaultRenderTarget );
		}

		// Build command lists for children
		if ( panel.HasChildren )
		{
			panel.BuildCommandListsForChildren( this, ref state );
		}
	}

	/// <summary>
	/// Render a panel - executes pre-built command lists.
	/// Command lists should be built during tick phase via BuildCommandLists.
	/// </summary>
	public void Render( Panel panel, RenderState state )
	{
		if ( panel?.ComputedStyle == null )
			return;

		if ( !panel.IsVisible )
			return;

		//
		// Push matrix before culling so Panel.GlobalMatrix is set
		//
		var pushed = PushMatrix( panel );

		//
		// Quickly clip anything before sending to renderer, this doesn't need to be perfect
		//
		if ( ShouldEarlyCull( panel ) )
		{
			if ( pushed ) PopMatrix();
			return;
		}

		var renderMode = PushRenderMode( panel );

		//
		// Execute the pre-built command list
		//
		panel.CommandList.ExecuteOnRenderThread();

		// Draw children
		if ( panel.HasChildren )
		{
			panel.RenderChildren( this, ref state );
		}

		// Execute post-children layer commands (draws filtered result)
		if ( panel.HasPanelLayer )
		{
			// Restore the default render target before executing layer commands.
			Graphics.RenderTarget = DefaultRenderTarget;
			panel.LayerCommandList.ExecuteOnRenderThread();
		}

		if ( pushed ) PopMatrix();
		if ( renderMode ) PopRenderMode();
	}

	internal struct LayerEntry
	{
		public Texture Texture;
		public Matrix Matrix;
	}

	internal Stack<LayerEntry> LayerStack;

	internal bool IsWorldPanel( Panel panel )
	{
		if ( panel is RootPanel { IsWorldPanel: true } )
			return true;

		if ( panel.FindRootPanel()?.IsWorldPanel ?? false )
			return true;

		return false;
	}

	internal void PushLayer( Panel panel, Texture texture, Matrix mat )
	{
		LayerStack ??= new Stack<LayerEntry>();

		panel.CommandList.SetRenderTarget( RenderTarget.From( texture ) );
		panel.CommandList.Attributes.Set( "LayerMat", mat );
		panel.CommandList.Attributes.SetCombo( "D_WORLDPANEL", 0 );
		panel.CommandList.Clear( Color.Transparent );

		LayerStack.Push( new LayerEntry { Texture = texture, Matrix = mat } );
	}

	/// <summary>
	/// Pop a layer and restore the previous render target.
	/// Commands are written to the specified command list.
	/// </summary>
	internal void PopLayer( Panel panel, CommandList commandList, RenderTarget defaultRenderTarget )
	{
		LayerStack.Pop();

		if ( LayerStack.TryPeek( out var top ) )
		{
			commandList.SetRenderTarget( RenderTarget.From( top.Texture ) );
			commandList.Attributes.Set( "LayerMat", top.Matrix );
			commandList.Attributes.SetCombo( "D_WORLDPANEL", 0 );
		}
		else
		{
			commandList.Attributes.Set( "LayerMat", Matrix.Identity );
			commandList.Attributes.SetCombo( "D_WORLDPANEL", IsWorldPanel( panel ) );
		}
	}

	/// <summary>
	/// The default render target for the current root panel render.
	/// Set during Render() and used by layers to restore after popping.
	/// </summary>
	internal RenderTarget DefaultRenderTarget;
}
