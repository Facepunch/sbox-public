using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

public sealed partial class CommandList
{
	RenderAttributes drawAttributes;
	AttributeAccess drawAttributeAccess;

	// The draw set while playing back into a block whose attributes are managed (a backend other than Source 2's)
	RenderAttributes managedDrawAttributes;
	RenderAttributes currentDrawAttributes;

	/// <summary>Starts a private attribute set for one draw, inheriting the caller's attributes at playback.</summary>
	internal unsafe AttributeAccess BeginDrawAttributes()
	{
		drawAttributes ??= new();
		drawAttributeAccess ??= new( this, () => currentDrawAttributes ?? drawAttributes );
		static void Execute( ref Entry entry, CommandList list )
		{
			var scope = Graphics.Attributes;
			var draw = scope.IsManaged ? list.managedDrawAttributes ??= RenderAttributes.CreateManaged() : list.drawAttributes;
			list.currentDrawAttributes = draw;
			draw.Clear( false );
			scope.MergeTo( draw );
		}
		AddEntry( &Execute, default );
		return drawAttributeAccess;
	}

	internal unsafe void DrawQuad( Rect rect, Material material, Color color, AttributeAccess attributes )
	{
		static void Execute( ref Entry entry, CommandList list )
		{
			Graphics.DrawQuad( new Rect( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w ),
				(Material)entry.Object1, new Color( entry.Data2.x, entry.Data2.y, entry.Data2.z, entry.Data2.w ),
				((AttributeAccess)entry.Object2)._get() );
		}
		AddEntry( &Execute, new Entry
		{
			Data1 = new Vector4( rect.Left, rect.Top, rect.Width, rect.Height ),
			Data2 = new Vector4( color.r, color.g, color.b, color.a ),
			Object1 = material,
			Object2 = attributes
		} );
	}

	internal unsafe void DrawIndexedInstanced( GpuBuffer indices, Material material, int count, AttributeAccess attributes )
	{
		static void Execute( ref Entry entry, CommandList list )
		{
			Graphics.DrawIndexedInstanced( (GpuBuffer)entry.Object1, (Material)entry.Object2, entry.Count,
				((AttributeAccess)entry.Object3)._get() );
		}
		AddEntry( &Execute, new Entry { Object1 = indices, Object2 = material, Object3 = attributes, Count = count } );
	}

	public unsafe partial class AttributeAccess
	{
		// Writes (Grab*Texture) and the clear (Reset) happen under the owning CommandList's _lock,
		// so they're already serialized. GetRenderTarget is the only access not already under that
		// lock, so it takes it explicitly - see below.
		private readonly Dictionary<string, RenderTarget> _renderTargets = new();

		internal Func<RenderAttributes> _get;
		internal CommandList list;

		RenderAttributes attributes => _get();

		internal AttributeAccess( CommandList cmdlist, Func<RenderAttributes> _getter )
		{
			list = cmdlist;
			_get = _getter;
		}

		public void Clear()
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object1;
				attrAccess.attributes.Clear();
			}
			list.AddEntry( &Execute, new Entry { Object1 = this } );
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
				attrAccess.attributes.Set( entry.Token, Unsafe.As<Vector4, Matrix>( ref entry.Data1 ) );
			}
			var e = new Entry { Token = token, Object4 = this };
			Unsafe.As<Vector4, Matrix>( ref e.Data1 ) = matrix;
			list.AddEntry( &Execute, e );
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

		/// <summary>
		/// Set a texture's bindless index to this int attribute, looked up as the list plays back, where the renderer draws,
		/// and tell texture streaming the texture is used then (<see cref="Texture.MarkUsed"/> with <paramref name="requiredMipSize"/>).
		/// </summary>
		internal void SetIndex( StringToken token, Texture texture, int requiredMipSize = 0 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var texture = (Texture)entry.Object2;
				texture.MarkUsed( (int)entry.Data1.x );
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, texture.Index );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object2 = texture, Object4 = this, Data1 = new Vector4( requiredMipSize, 0, 0, 0 ) } );
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
				attrAccess.attributes.SetCombo( entry.Token, (int)entry.Data1.x );
			}
			// Store the enum as its integer value in Data1.x to avoid boxing into Object2.
			// SetCombo(int) and SetComboEnum<T> both ultimately call SetComboValue(byte),
			// so the GPU behaviour is identical.
			var intValue = Unsafe.SizeOf<T>() switch
			{
				1 => Unsafe.As<T, byte>( ref t ),
				2 => (int)Unsafe.As<T, short>( ref t ),
				8 => (int)Unsafe.As<T, long>( ref t ),
				_ => Unsafe.As<T, int>( ref t )
			};
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this, Data1 = new Vector4( intValue, 0, 0, 0 ) } );
		}

		public void SetData<T>( StringToken token, T data ) where T : unmanaged
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.SetData( entry.Token, commandList.ReadPayload<T>( entry ) );
			}
			list.AddEntry( &Execute, new Entry { Token = token, Object4 = this }, data );
		}

		// What a managed block draws into: the target it bound, else its view's own (a Borealis.Gpu camera's stage)
		static RenderTarget BoundTarget() => Graphics.RenderTarget ?? (Graphics.CurrentView as BorealisGpuView)?.Target;

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

							// A block on a device of its own (a Borealis.Gpu frame's, or a camera's stage in one) draws into its
							// bound target, which has no native handle: the one it set, else its view's
							if ( attrAccess.attributes.IsManaged )
							{
								attrAccess.attributes.Set( entry.Token, BoundTarget()?.ColorTarget );
								return;
							}

							var handle = Graphics.GetColorTarget( out var owned );
							attrAccess.attributes.Set( entry.Token, handle );
							if ( owned && !handle.IsNull ) handle.DestroyStrongHandle();
						}
						list.AddEntry( &Execute, new Entry { Token = token, Object4 = this } );
					}
					break;

				case RenderValue.DepthTarget:
					{
						static void Execute( ref Entry entry, CommandList commandList )
						{
							var attrAccess = (AttributeAccess)entry.Object4;

							if ( attrAccess.attributes.IsManaged )
							{
								attrAccess.attributes.Set( entry.Token, BoundTarget()?.DepthTarget );
								return;
							}

							var handle = Graphics.GetDepthTarget( out var owned );
							attrAccess.attributes.Set( entry.Token, handle );
							if ( owned && !handle.IsNull ) handle.DestroyStrongHandle();
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

				// GetTemporary caps the mips a small target asks for, so a mip past the last one binds the last one
				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, target.ColorTarget, Math.Min( (int)entry.Data1.x, target.ColorTarget.Mips - 1 ) );
			}

			list.AddEntry( &Execute, new Entry { Token = token, Object5 = buffer.Name, Object4 = this, Data1 = new Vector4( mip, 0, 0, 0 ) } );
		}

		/// <summary>
		/// Set the depth texture from this named render target to this attribute
		/// </summary>
		public void Set( StringToken token, RenderTargetHandle.DepthTextureRef buffer, int mip = -1 )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
				{
					Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
					return;
				}

				var attrAccess = (AttributeAccess)entry.Object4;
				attrAccess.attributes.Set( entry.Token, target.DepthTarget, (int)entry.Data1.x );
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
		public RenderTargetHandle GrabFrameTexture( string token, Graphics.DownsampleMethod downsampleMethod, int maxMips = 0 )
		{
			return GrabFrameTexture( token, downsampleMethod, maxMips, out _ );
		}

		/// <summary>
		/// Takes a copy of the current viewport's color texture, like <see cref="GrabFrameTexture(string, Graphics.DownsampleMethod, int)"/>,
		/// and outputs the recorded command for <see cref="RaiseGrabFrameTextureMips"/>.
		/// </summary>
		internal RenderTargetHandle GrabFrameTexture( string token, Graphics.DownsampleMethod downsampleMethod, int maxMips, out int entry, bool engineOwned = false )
		{
			static void Execute( ref Entry entry, CommandList commandList )
			{
				var attrAccess = (AttributeAccess)entry.Object2;
				var temp = Graphics.GrabFrameTexture( (string)entry.Object5, attrAccess.attributes, (Graphics.DownsampleMethod)(int)entry.Data1.x, (int)entry.Data1.y, entry.Data1.z != 0 );
				commandList.state.renderTargets[(string)entry.Object5] = temp;
				attrAccess._renderTargets[(string)entry.Object5] = temp;
			}

			lock ( list._lock )
			{
				list.AddEntry( &Execute, new Entry { Object5 = token, Object2 = this, Data1 = new Vector4( (int)downsampleMethod, maxMips, engineOwned ? 1 : 0, 0 ) } );
				entry = list._recorded.Entries.Count - 1;
			}

			return new RenderTargetHandle { Name = token };
		}

		/// <summary>
		/// Lets a grab recorded by <see cref="GrabFrameTexture(string, Graphics.DownsampleMethod, int, out int, bool)"/> generate at least
		/// <paramref name="mips"/> mips, for later draws that sample it more deeply.
		/// </summary>
		internal void RaiseGrabFrameTextureMips( int entry, int mips )
		{
			lock ( list._lock )
			{
				list.Writing();
				ref var grab = ref CollectionsMarshal.AsSpan( list._recorded.Entries )[entry];
				grab.Data1.y = Math.Max( grab.Data1.y, mips );
			}
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
			// The render thread's replay writes and reads these without the lock: the main thread may hold it while it waits
			// for the render thread, and leaves them to the replay meanwhile (ClearRenderTargets)
			if ( ThreadSafe.IsRenderThread ) return _renderTargets.TryGetValue( name, out var played ) ? played : null;

			// Serialize against execute-thread writes / Reset clears, which hold the CommandList's _lock.
			lock ( list._lock )
			{
				if ( _renderTargets.TryGetValue( name, out var rt ) )
					return rt;
				return null;
			}
		}

		internal void ClearRenderTargets()
		{
			// Caller (Reset) already holds the CommandList's _lock. While a frame handed over may replay the list on the render
			// thread, the grabs it records are its own: the next replay's grabs replace them
			if ( RenderSync.Overlapping ) return;
			_renderTargets.Clear();
		}
	}

	/// <summary>
	/// These are the attributes for the current view. Setting a variable here will let you pass it down to
	/// other places in the render pipeline.
	/// </summary>
	[Obsolete( "Global/frame attributes are deprecated. Use a local Attributes set or pipeline texture slots instead." )]
	public AttributeAccess GlobalAttributes => Attributes;

	/// <summary>
	/// Access to the local attributes. What these are depends on where the command list is being called.
	/// If we're calling from a renderable, these are the attributes for that renderable.
	/// </summary>
	public AttributeAccess Attributes { get; private set; }

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

/// <summary>
/// Bindless texture slots within each view's pipeline texture range. AO/SSR producers publish
/// into their view's range; consumers use its PipelineTextureOffset to read the same slots.
/// New ranges start at index 0 ("none"), so skipped producers cannot inherit another view's results.
/// Values must match the <c>PipelineTextureSlot</c> enum in common/classes/Bindless.hlsl and fit
/// CSceneSystem's per-view stride.
/// </summary>
internal enum PipelineTextureSlot
{
	/// <summary>
	/// Screen-space ambient occlusion. When no AO is produced for this view the slot stays at index 0,
	/// which consumers treat as disabled (no occlusion).
	/// </summary>
	AmbientOcclusion = 0,

	/// <summary>
	/// Dynamic reflections / screen-space reflections. When none is produced for this view the slot stays
	/// at index 0, which consumers treat as disabled (no reflections).
	/// </summary>
	Reflections = 1,

	Count
}



