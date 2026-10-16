namespace Sandbox.Rendering;

public sealed unsafe partial class CommandList
{
	static long nextViewId;

	/// <summary>
	/// Assign an identity shared across command lists, including lists inserted into another playback.
	/// </summary>
	static ViewHandle CreateViewHandle()
	{
		return new ViewHandle( System.Threading.Interlocked.Increment( ref nextViewId ) );
	}

	/// <summary>
	/// Read a view from this playback only. Missing, skipped and default handles resolve to zero/false.
	/// </summary>
	ViewData GetViewData( long id )
	{
		return id != 0 && state.views.TryGetValue( id, out var view ) ? view : default;
	}

	public partial class AttributeAccess
	{

		/// <summary>
		/// Bind the view's ProjectionToView value when this command executes.
		/// </summary>
		public void Set( StringToken token, ViewHandle.ProjectionToViewRef value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var access = (AttributeAccess)entry.Object4;
				access.attributes.Set( entry.Token, commandList.GetViewData( entry.ViewId ).ProjectionToView );
			}

			list.AddEntry( &Execute, new Entry { Token = token, ViewId = value.Id, Object4 = this } );
		}

		/// <summary>
		/// Bind the view's DepthUvScaleBias value when this command executes.
		/// </summary>
		public void Set( StringToken token, ViewHandle.DepthUvScaleBiasRef value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var access = (AttributeAccess)entry.Object4;
				access.attributes.Set( entry.Token, commandList.GetViewData( entry.ViewId ).DepthUvScaleBias );
			}

			list.AddEntry( &Execute, new Entry { Token = token, ViewId = value.Id, Object4 = this } );
		}

		/// <summary>
		/// Bind the view's Viewport value when this command executes.
		/// </summary>
		public void Set( StringToken token, ViewHandle.ViewportRef value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var access = (AttributeAccess)entry.Object4;
				access.attributes.Set( entry.Token, commandList.GetViewData( entry.ViewId ).Viewport );
			}

			list.AddEntry( &Execute, new Entry { Token = token, ViewId = value.Id, Object4 = this } );
		}

		/// <summary>
		/// Bind the view's DepthRange value when this command executes.
		/// </summary>
		public void Set( StringToken token, ViewHandle.DepthRangeRef value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var access = (AttributeAccess)entry.Object4;
				access.attributes.Set( entry.Token, commandList.GetViewData( entry.ViewId ).DepthRange );
			}

			list.AddEntry( &Execute, new Entry { Token = token, ViewId = value.Id, Object4 = this } );
		}

		/// <summary>
		/// Bind the view's IsValid value when this command executes.
		/// </summary>
		public void Set( StringToken token, ViewHandle.IsValidRef value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var access = (AttributeAccess)entry.Object4;
				access.attributes.Set( entry.Token, commandList.GetViewData( entry.ViewId ).IsValid );
			}

			list.AddEntry( &Execute, new Entry { Token = token, ViewId = value.Id, Object4 = this } );
		}
	}
}
