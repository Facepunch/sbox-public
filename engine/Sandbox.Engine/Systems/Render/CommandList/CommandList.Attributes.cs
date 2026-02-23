using System.Collections.Concurrent;

namespace Sandbox.Rendering;

public sealed partial class CommandList
{
	public unsafe class AttributeAccess
	{
		private readonly ConcurrentDictionary<string, RenderTarget> _renderTargets = new();

		internal Func<RenderAttributes> _get;
		internal CommandList list;

		RenderAttributes attributes => _get();

		internal AttributeAccess( CommandList cmdlist, Func<RenderAttributes> _getter )
		{
			list = cmdlist;
			_get = _getter;
		}

		public void Set( StringToken token, float f )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, entry.Data1.x );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( f, 0, 0, 0 ) } );
		}

		public void Set( StringToken token, double f ) => Set( token, (float)f );

		public void Set( StringToken token, Vector2 vector2 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, new Vector2( entry.Data1.x, entry.Data1.y ) );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( vector2.x, vector2.y, 0, 0 ) } );
		}

		public void Set( StringToken token, Vector3 vector3 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, new Vector3( entry.Data1.x, entry.Data1.y, entry.Data1.z ) );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( vector3.x, vector3.y, vector3.z, 0 ) } );
		}

		public void Set( StringToken token, Vector4 vector4 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, entry.Data1 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = vector4 } );
		}

		public void Set( StringToken token, int i )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, (int)entry.Data1.x );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( i, 0, 0, 0 ) } );
		}

		public void Set( StringToken token, bool b )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, (int)entry.Data1.x != 0 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( b ? 1 : 0, 0, 0, 0 ) } );
		}

		public void Set( StringToken token, Matrix matrix )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, (Matrix)entry.Object2 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = matrix, Object4 = this } );
		}

		public void Set( StringToken token, GpuBuffer buffer )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, (GpuBuffer)entry.Object2 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = buffer, Object4 = this } );
		}

		public void Set( StringToken token, Texture texture, int mip = -1 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, (Texture)entry.Object2, (int)entry.Data1.x );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = texture, Object4 = this, Data1 = new Vector4( mip, 0, 0, 0 ) } );
		}

		public void Set( StringToken token, SamplerState samplerState )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, (SamplerState)entry.Object2 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = samplerState, Object4 = this } );
		}

		public void SetCombo( StringToken token, int value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.SetCombo( entry.Token, (int)entry.Data1.x );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( value, 0, 0, 0 ) } );
		}

		public void SetCombo( StringToken token, bool value )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.SetCombo( entry.Token, (int)entry.Data1.x != 0 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( value ? 1 : 0, 0, 0, 0 ) } );
		}

		public void SetCombo<T>( StringToken token, T t ) where T : unmanaged, Enum
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.SetComboEnum( entry.Token, (T)entry.Object2 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = t, Object4 = this } );
		}

		public void SetData<T>( StringToken token, T data ) where T : unmanaged
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.SetData( entry.Token, (T)entry.Object2 );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = data, Object4 = this } );
		}

		/// <summary>
		/// Set a special value
		/// </summary>
		public void SetValue( StringToken token, RenderValue value )
		{
			switch ( value )
			{
				case RenderValue.ColorTarget:
					{
						static void Execute( ref Entry entry, CommandList commandList )
						{
							var attrAccess = (AttributeAccess)entry.Object4;
							attrAccess.attributes.Set( entry.Token, Graphics.SceneLayer.GetColorTarget() );
						}
						list.AddEntry( &Execute, new Entry { Token = token, Object4 = this } );
					}
					break;

				case RenderValue.DepthTarget:
					{
						static void Execute( ref Entry entry, CommandList commandList )
						{
							var attrAccess = (AttributeAccess)entry.Object4;
							attrAccess.attributes.Set( entry.Token, Graphics.SceneLayer.GetDepthTarget() );
						}
						list.AddEntry( &Execute, new Entry { Token = token, Object4 = this } );
					}
					break;

				case RenderValue.MsaaCombo:
					{
						static void Execute( ref Entry entry, CommandList commandList )
						{
							var attrAccess = (AttributeAccess)entry.Object4;
							attrAccess.attributes.SetCombo( entry.Token, Graphics.IdealMsaaLevel != MultisampleAmount.MultisampleNone ? 1 : 0 );
						}
						list.AddEntry( &Execute, new Entry { Token = token, Object4 = this } );
					}
					break;
			}
		}

		/// <summary>
		/// Set the color texture from this named render target to this attribute
		/// </summary>
		public void Set( StringToken token, RenderTargetHandle.ColorTextureRef buffer, int mip = -1 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
				{
					Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
					return;
				}

				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, target.ColorTarget, (int)entry.Data1.x );
			}

			list.AddEntry( &Execute, new Entry { Token = token, Object5 = buffer.Name, Object4 = this, Data1 = new Vector4( mip, 0, 0, 0 ) } );
		}

		/// <summary>
		/// Set the color texture from this named render target to this attribute
		/// </summary>
		public void Set( StringToken token, RenderTargetHandle.ColorIndexRef buffer )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
				{
					Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
					return;
				}

				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, target.ColorTarget.Index );
			}

			list.AddEntry( &Execute, new Entry { Token = token, Object5 = buffer.Name, Object4 = this } );
		}

		/// <summary>
		/// Set the size of this named render target to this float2 attribute
		/// </summary>
		public void Set( StringToken token, RenderTargetHandle.SizeHandle size, bool inverse = false )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
				{
					Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
					return;
				}

				var s = target.ColorTarget.Size;

				var attrAccess = (AttributeAccess)entry.Object4;
				if ( (int)entry.Data1.x != 0 ) attrAccess.attributes.Set( entry.Token, new Vector2( 1.0f / s.x, 1.0f / s.y ) );
				else attrAccess.attributes.Set( entry.Token, s );
			}

			list.AddEntry( &Execute, new Entry { Token = token, Object5 = size.Name, Object4 = this, Data1 = new Vector4( inverse ? 1 : 0, 0, 0, 0 ) } );
		}

		/// <summary>
		/// Takes a copy of the current viewport's color texture and stores it in targetName on renderAttributes.
		/// </summary>
		public RenderTargetHandle GrabFrameTexture( string token = "FrameTexture", bool withMips = false )
		{
			return GrabFrameTexture( token, withMips ? Graphics.DownsampleMethod.GaussianBlur : Graphics.DownsampleMethod.None );
		}

		/// <summary>
		/// Takes a copy of the current viewport's color texture and stores it in targetName on renderAttributes.
		/// </summary>
		public RenderTargetHandle GrabFrameTexture( string token, Graphics.DownsampleMethod downsampleMethod )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object2;
				var temp = Graphics.GrabFrameTexture( (string)entry.Object5, attrAccess.attributes, (Graphics.DownsampleMethod)(int)entry.Data1.x );
				commandList.state.renderTargets[(string)entry.Object5] = temp;
				attrAccess._renderTargets[(string)entry.Object5] = temp;
			}

			list.AddEntry( &Execute, new Entry { Object5 = token, Object2 = this, Data1 = new Vector4( (int)downsampleMethod, 0, 0, 0 ) } );

			return new RenderTargetHandle { Name = token };
		}

		/// <summary>
		/// Takes a copy of the current viewport's depth texture and stores it in targetName on renderAttributes.
		/// </summary>
		public RenderTargetHandle GrabDepthTexture( string token = "DepthTexture" )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object2;
				var temp = Graphics.GrabDepthTexture( (string)entry.Object5, attrAccess.attributes );
				commandList.state.renderTargets[(string)entry.Object5] = temp;
				attrAccess._renderTargets[(string)entry.Object5] = temp;
			}

			list.AddEntry( &Execute, new Entry { Object5 = token, Object2 = this } );

			return new RenderTargetHandle { Name = token };
		}

		/// <summary>
		/// Get the actual render target by name. Useful for externals that need to access the render target directly.
		/// </summary>
		public RenderTarget GetRenderTarget( string name )
		{
			if ( _renderTargets.TryGetValue( name, out var rt ) )
				return rt;
			return null;
		}

		internal void ClearRenderTargets()
		{
			_renderTargets.Clear();
		}
	}

	/// <summary>
	/// These are the attributes for the current view. Setting a variable here will let you pass it down to
	/// other places in the render pipeline.
	/// </summary>
	public AttributeAccess GlobalAttributes { get; private set; }

	/// <summary>
	/// Access to the local attributes. What these are depends on where the command list is being called.
	/// If we're calling from a renderable, these are the attributes for that renderable.
	/// </summary>
	public AttributeAccess Attributes { get; private set; }

	RenderAttributes GetFrameAttributes() => Graphics.FrameAttributes;
	RenderAttributes GetLocalAttributes() => Graphics.Attributes;

}


public enum RenderValue
{
	/// <summary>
	/// The color texure we're currently rendering to
	/// </summary>
	ColorTarget,

	/// <summary>
	/// The depth texture we're currently rendering to
	/// </summary>
	DepthTarget,

	/// <summary>
	/// Will set the named combo to 1 if MSAA is active, otherwise 0.
	/// </summary>
	MsaaCombo,
}



