using Sandbox.Rendering;
using System;
using System.Runtime.InteropServices;

namespace Editor;

internal static partial class ToolSceneViews
{
	/// <summary>
	/// <c>ToolHudManagedItem_t</c> (src/public/toolutils2/toolhud.h): a tool HUD object as <c>CToolHud::Render</c> would draw it
	/// this frame, in the view's pixels from its top left.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal unsafe struct HudItem
	{
		public Vector4 Rect;
		public float Alpha;
		public int ImageWidth;
		public int ImageHeight;
		public int ImageId;
		public byte* Pixels;
		public IntPtr Caption;
		public Vector2 CaptionAnchor;
		public int CaptionAlign;
		public float TextHeight;
	}

	/// <summary>A native view's tool HUD (<c>CMapView::GetManagedHudItems</c>): items written into a view of width x height pixels.</summary>
	internal unsafe delegate int HudSource( HudItem* items, int maxItems, int width, int height );

	/// <summary>
	/// A native view's tool HUD (<c>CToolHud</c>: tool and selection mode notifications, status icons with captions) drawn over
	/// the finished frame, after the UI stage, as native's screen-space view draws its procedural layer last: each object's
	/// image as a quad, faded by its alpha, and its caption beside it. The images are native's own, read back once.
	/// </summary>
	internal sealed unsafe class ToolHud : IDisposable
	{
		const int MaxItems = 32;

		struct Item
		{
			public Rect Rect;
			public float Alpha;
			public Texture Texture;
			public string Caption;
			public Vector2 CaptionAnchor;
			public int CaptionAlign;
			public float TextHeight;
		}

		readonly HudItem[] native = new HudItem[MaxItems];
		readonly List<Item> items = new();
		readonly Dictionary<int, Texture> textures = new();
		readonly Dictionary<IntPtr, (ulong Hash, string Text)> captions = new();
		readonly RenderAttributes attributes = new();

		/// <summary>
		/// The HUD's objects this frame for the request's view, from <paramref name="source"/>, or with none the HUD the request
		/// carries (a tool scene's own) when <paramref name="requestHud"/>. Outside a render block: images seen for the first
		/// time become textures here.
		/// </summary>
		public void Gather( HudSource source, ref Request request, bool requestHud )
		{
			items.Clear();
			var width = request.Width;
			var height = request.Height;
			if ( width < 1 || height < 1 ) return;

			int count;
			fixed ( HudItem* all = native )
			{
				if ( source is not null ) count = source( all, MaxItems, width, height );
				else if ( requestHud && request.GetHud != null && request.Hud != IntPtr.Zero ) count = request.GetHud( request.Hud, all, MaxItems, width, height );
				else return;
			}
			count = Math.Clamp( count, 0, MaxItems );

			for ( int i = 0; i < count; i++ )
			{
				ref var item = ref native[i];
				if ( item.Alpha <= 0.0f ) continue;

				items.Add( new Item
				{
					Rect = new Rect( item.Rect.x, item.Rect.y, item.Rect.z, item.Rect.w ),
					Alpha = item.Alpha,
					Texture = TextureFor( ref item ),
					Caption = CaptionFor( item.Caption ),
					CaptionAnchor = item.CaptionAnchor,
					CaptionAlign = item.CaptionAlign,
					TextHeight = item.TextHeight,
				} );
			}
		}

		Texture TextureFor( ref HudItem item )
		{
			if ( item.ImageId == 0 || item.Pixels == null || item.ImageWidth < 1 || item.ImageHeight < 1 ) return null;
			if ( textures.TryGetValue( item.ImageId, out var texture ) ) return texture;

			var bytes = item.ImageWidth * item.ImageHeight * 4;
			texture = Texture.Create( item.ImageWidth, item.ImageHeight, ImageFormat.BGRA8888 )
				.WithData( new ReadOnlySpan<byte>( item.Pixels, bytes ) )
				.Finish();
			textures[item.ImageId] = texture;
			return texture;
		}

		string CaptionFor( IntPtr utf8 )
		{
			if ( utf8 == IntPtr.Zero ) return null;

			// FNV-1a over the bytes: no string is made while they stay the same
			var bytes = (byte*)utf8;
			ulong hash = 14695981039346656037UL;
			int length = 0;
			for ( ; length < 1024 && bytes[length] != 0; length++ )
				hash = (hash ^ bytes[length]) * 1099511628211UL;
			if ( length == 0 ) return null;

			if ( captions.TryGetValue( utf8, out var known ) && known.Hash == hash ) return known.Text;

			if ( captions.Count > 64 ) captions.Clear();
			var text = System.Text.Encoding.UTF8.GetString( bytes, length );
			captions[utf8] = (hash, text);
			return text;
		}

		/// <summary>
		/// Draw the gathered objects over the frame (the camera's <see cref="Stage.AfterUI"/>).
		/// </summary>
		public void Draw()
		{
			foreach ( var item in items )
			{
				var color = Color.White.WithAlpha( item.Alpha );
				if ( item.Texture is not null )
				{
					attributes.Set( "Texture", item.Texture );
					Graphics.DrawQuad( item.Rect, Material.UI.Basic, color, attributes );
				}

				if ( string.IsNullOrEmpty( item.Caption ) || item.TextHeight <= 0 ) continue;

				// Centred on the anchor vertically; ending at it, centred on it or starting at it across
				const float Wide = 4096;
				var height = item.TextHeight;
				var top = item.CaptionAnchor.y - height * 0.5f;
				var (rect, flags) = item.CaptionAlign switch
				{
					< 0 => (new Rect( item.CaptionAnchor.x - Wide, top, Wide, height ), TextFlag.RightCenter),
					0 => (new Rect( item.CaptionAnchor.x - Wide * 0.5f, top, Wide, height ), TextFlag.CenterHorizontally | TextFlag.CenterVertically),
					_ => (new Rect( item.CaptionAnchor.x, top, Wide, height ), TextFlag.LeftCenter),
				};
				Graphics.DrawText( rect, item.Caption, color, "Roboto", height * 0.8f, 600, flags );
			}
		}

		/// <summary>Forget the images: a read-back's temporary view numbers its own from 1.</summary>
		public void Clear()
		{
			foreach ( var texture in textures.Values ) texture.Dispose();
			textures.Clear();
			items.Clear();
		}

		public void Dispose() => Clear();
	}
}
