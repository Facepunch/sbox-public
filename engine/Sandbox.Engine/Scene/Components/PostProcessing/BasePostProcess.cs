using Sandbox.MovieMaker;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// The base class for all post process effects.
/// </summary>
public abstract class BasePostProcess : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer
{
	PostProcessContext? _currentContext;

	internal PostProcessContext context
	{
		get
		{
			if ( !_currentContext.HasValue ) throw new System.Exception( "Should only be called during build" );
			return _currentContext.Value;
		}
	}

	/// <summary>
	/// The camera we're being applied to. This is only valid during the Render call.
	/// </summary>
	protected CameraComponent Camera => context.Camera;

	/// <summary>
	/// The default attributes for this post process. This will be used by helper functions like Blit.
	/// </summary>
	protected readonly RenderAttributes Attributes = new();

	internal void Build( PostProcessContext ctx )
	{
		_currentContext = ctx;

		try
		{
			// always cleared before build
			Attributes.Clear();
			Render();
		}
		finally
		{
			_currentContext = default;
		}
	}

	/// <summary>
	/// Override in your implementation to do your rendering
	/// </summary>
	public abstract void Render();


	public ref struct BlitMode
	{
		/// <summary>
		/// The material to use for the blit.
		/// </summary>
		public Material Material;

		/// <summary>
		/// We'll use this instead of BasePostProcess.Attributes if set.
		/// </summary>
		public RenderAttributes Attributes;

		/// <summary>
		/// Where to place this in the render pipeline
		/// </summary>
		public Stage RenderStage;

		/// <summary>
		/// The order within the stage. Lower numbers get rendered first.
		/// </summary>
		public int Order;

		/// <summary>
		/// If true, the backbuffer will be copied to a texture called "ColorBuffer" before the blit.
		/// </summary>
		public bool WantsBackbuffer;

		/// <summary>
		/// If both WantsBackbuffer and this is true the backbuffer will be mipped after being copied.
		/// </summary>
		public bool WantsBackbufferMips;

		/// <summary>
		/// Shortcut to build a simple blit mode
		/// </summary>
		public static BlitMode Simple( Material m, Stage stage, int order = 0 ) => new BlitMode { Material = m, RenderStage = stage, Order = order };

		/// <summary>
		/// Shortcut to build a blit mode that copies the backbuffer first
		/// </summary>
		public static BlitMode WithBackbuffer( Material m, Stage stage, int order = 0, bool mip = false ) => new BlitMode { Material = m, RenderStage = stage, Order = order, WantsBackbuffer = true, WantsBackbufferMips = true };
	}

	/// <summary>
	/// Helper to do a blit with the current camera's post process
	/// </summary>
	protected void Blit( BlitMode blit, string debugName )
	{
		if ( !blit.Material.IsValid() ) return;

		CommandList cl = new CommandList( blit.Material.Name );

		if ( blit.WantsBackbuffer )
		{
			cl.Attributes.GrabFrameTexture( "ColorBuffer", blit.WantsBackbufferMips );
		}

		cl.Blit( blit.Material, blit.Attributes ?? Attributes );

		InsertCommandList( cl, blit.RenderStage, blit.Order, debugName );
	}


	/// <summary>
	/// Helper to do a blit with the current camera's post process
	/// </summary>
	protected void BlitSimple( Material shader, Stage stage, int order, string debugName )
	{
		Blit( BlitMode.Simple( shader, stage, order ), debugName );
	}

	/// <summary>
	/// Helper to add a command list to the current camera's post process
	/// </summary>
	protected void InsertCommandList( CommandList cl, Sandbox.Rendering.Stage stage, int order, string debugName )
	{
		var layer = context.Camera.PostProcess.CreateLayer( stage );
		layer.Order = order;
		layer.CommandList = cl;
		layer.Name = debugName;
	}
}

/// <summary>
/// Like BasePostProcess but enables access to helper methods for accessing from multiple instances using GetWeighted.
/// </summary>
public abstract class BasePostProcess<T> : BasePostProcess where T : BasePostProcess
{
	/// <summary>
	/// Helper to get a weighted value from all active post process volumes
	/// </summary>
	protected U GetWeighted<U>( System.Func<T, U> value, U defaultVal = default, bool onlyLerpBetweenVolumes = false )
	{
		U v = defaultVal;
		var lerper = Interpolator.GetDefault<U>();

		int i = 0;
		foreach ( var e in context.Components )
		{
			var target = value( (T)e.Effect );
			v = lerper.Interpolate( v, target, e.Weight );

			if ( onlyLerpBetweenVolumes && i == 0 )
				v = target;

			i++;
		}

		return v;
	}
}
