using Sandbox.Rendering;
using Sandbox.UI;
using static Sandbox.Painter;

namespace Sandbox;

/// <summary>
/// Appends shader instances and draw calls to a command list. Its buffers live until the list resets.
/// </summary>
internal partial class PainterBatcher
{
	readonly CommandList _commands;
	internal CommandList CommandList => _commands;
	[ConVar( "ui_visualize_batches", Help = "Visualize UI draw batches with colored overlays" )]
	internal static bool DebugVisualizeBatches { get; set; }
	readonly record struct TextureUse( Texture Texture, bool Presented );
	readonly List<TextureUse> _textures = [];
	internal readonly List<ClipEntry> DrawClips = [];
	static readonly List<string> _layerNames = [];
	int _layerIndex;
	int _batchStart;
	int _batchIndex;
	BlendMode _blendMode;
	Target[] _targetStack = [];
	ClipRestore[] _clipStack = [];
	int _targetDepth;
	int _clipDepth;

	internal struct ClipRestore
	{
		internal Rect TopRect;
		internal int Count;
		internal bool Invert;
		internal int? Index;
	}

	internal struct Target
	{
		Matrix _transform = Matrix.Identity;
		Matrix _localTransform;
		Matrix _resolvedTransform;
		int? _transformIndex;
		int? _scissorIndex;

		internal Matrix Transform
		{
			readonly get => _transform;
			set
			{
				if ( _transform == value ) return;

				_transform = value;
				_transformIndex = null;
			}
		}

		internal Rect Viewport;
		internal Scissoring Scissor;

		internal void SetScissor( in Scissoring scissor )
		{
			if ( Scissor.Equals( in scissor ) ) return;

			Scissor = scissor;
			_scissorIndex = null;
		}

		internal void PushClip( Rect rect, BorderRadii radii, Matrix transform )
		{
			Scissor.Push( rect, radii, transform );
			_scissorIndex = null;
		}

		internal readonly void SaveClip( ref ClipRestore previous )
		{
			previous.Count = Scissor.Count;
			previous.Invert = Scissor.Invert;
			previous.Index = _scissorIndex;
			// Push either appends a clip or intersects the top rect; its radii and matrix stay intact.
			if ( Scissor.Count > 0 ) previous.TopRect = Scissor.Clips[Scissor.Count - 1].Rect;
		}

		internal void RestoreClip( in ClipRestore previous )
		{
			Scissor.Count = previous.Count;
			Scissor.Invert = previous.Invert;
			if ( previous.Count > 0 ) Scissor.Clips[previous.Count - 1].Rect = previous.TopRect;
			_scissorIndex = previous.Index;
		}

		internal Spatial ResolveSpatial( PainterBatcher batcher, in Matrix localTransform )
		{
			_scissorIndex ??= batcher.GetOrAddScissor( Scissor );
			if ( !_transformIndex.HasValue || _localTransform != localTransform )
			{
				_localTransform = localTransform;
				_resolvedTransform = localTransform == Matrix.Identity ? _transform : localTransform * _transform;
				_transformIndex = batcher.GetOrAddTransform( _resolvedTransform );
			}
			return new( _resolvedTransform, _scissorIndex.Value, _transformIndex.Value );
		}
		internal Matrix LayerMatrix = Matrix.Identity;
		internal bool? PlaybackPaused;
		internal bool Layered;
		internal bool? GammaOutput;
		internal int WorldPanelCombo;
		internal Matrix? WorldMatrix;

		public Target() { }
	}

	internal Target Destination = new();
	internal int DrawCalls { get; private set; }
	internal int FrameGrabs { get; private set; }
	internal void CountDraw( int grabs = 0 )
	{
		DrawCalls++;
		FrameGrabs += grabs;
	}
	internal int Count => Instances.Count;

	internal PainterBatcher( CommandList commands )
	{
		_commands = commands;
		_tables = [_textTable, _boxTable, _scissorTable, _transformTable, _gradientTable, _shapeTable, _pathTable, _pathNodeTable, _polygonPointTable];
	}

	internal void SetViewport( Rect bounds, Matrix? worldMatrix )
	{
		// A new viewport ends both the pending batch and the shared capture.
		Flush();
		Destination.Viewport = bounds;
		Destination.SetScissor( Scissoring.Single( bounds, BorderRadii.Zero, Matrix.Identity ) );
		Destination.WorldMatrix = worldMatrix;
		Destination.WorldPanelCombo = worldMatrix.HasValue ? 1 : 0;
		ApplyDestinationAttributes();
	}

	internal void ApplyDestinationAttributes( bool objectSpace = false )
	{
		ref readonly var target = ref Destination;
		var attributes = _commands.Attributes;
		attributes.Set( "LayerMat", target.LayerMatrix );
		attributes.Set( "TransformMat", target.Transform );
		attributes.SetCombo( "D_WORLDPANEL", target.WorldPanelCombo );
		attributes.Set( "UIInPanelLayer", target.Layered );
		if ( target.WorldMatrix.HasValue )
			attributes.Set( "WorldMat", objectSpace ? ScenePanelObject.BuildPanelToObjectMatrix() : target.WorldMatrix.Value );
		SetScissorAttributes( _commands, target.Scissor );
	}

	internal void RetainTexture( Texture texture, bool presented )
	{
		_textures.Add( new( texture, presented ) );
	}

	internal void BeginExecute()
	{
		foreach ( var usage in _textures )
		{
			if ( usage.Texture.ParentObject is VideoPlayer player )
				player.TrackPresentation( usage.Presented );
			if ( usage.Presented ) usage.Texture.MarkUsed();
		}
	}

	internal readonly record struct Checkpoint( int Instances, int Textures, int DrawCalls, int FrameGrabs, int Layers, int Targets, int Clips );

	internal Checkpoint GetCheckpoint()
	{
		Flush();
		return new( Count, _textures.Count, DrawCalls, FrameGrabs, _layerIndex, _targetDepth, _clipDepth );
	}

	internal void Rewind( Checkpoint checkpoint )
	{
		InvalidateBackdrop();
		Rewind( checkpoint.Instances );
		_batchStart = checkpoint.Instances;
		_layerIndex = checkpoint.Layers;
		DrawCalls = checkpoint.DrawCalls;
		FrameGrabs = checkpoint.FrameGrabs;
		_textures.RemoveRange( checkpoint.Textures, _textures.Count - checkpoint.Textures );
		RestoreScopeDepth( checkpoint );
		ClearClips();
	}

	internal int PushTarget()
	{
		if ( _targetDepth == _targetStack.Length )
			Array.Resize( ref _targetStack, Math.Max( 4, _targetDepth * 2 ) );

		_targetStack[_targetDepth] = Destination;
		return _targetDepth++;
	}

	internal void PopTarget( int index )
	{
		if ( index != _targetDepth - 1 )
			throw new InvalidOperationException( "Restore paint targets in reverse order." );

		Destination = _targetStack[index];
		_targetDepth = index;
	}

	internal int PushClip( Rect rect, BorderRadii radii, Matrix transform )
	{
		if ( _clipDepth == _clipStack.Length )
			Array.Resize( ref _clipStack, Math.Max( 8, _clipDepth * 2 ) );

		Destination.SaveClip( ref _clipStack[_clipDepth] );
		Destination.PushClip( rect, radii, transform );
		return _clipDepth++;
	}

	internal void PopClip( int index )
	{
		if ( index != _clipDepth - 1 )
			throw new InvalidOperationException( "Restore destination clips in reverse order." );

		Destination.RestoreClip( in _clipStack[index] );
		_clipDepth = index;
	}

	internal void RestoreScopeDepth( Checkpoint checkpoint )
	{
		_targetDepth = checkpoint.Targets;
		_clipDepth = checkpoint.Clips;
	}

	internal void Flush()
	{
		FlushBatch();
		InvalidateBackdrop();
	}

	void FlushBatch()
	{
		int count = Count - _batchStart;
		if ( count == 0 ) return;

		if ( DebugVisualizeBatches )
			Tint( _batchStart, count, new ColorHsv( (_batchIndex++ * 137.508f) % 360f, 0.7f, 0.9f, 0.85f ) );
		Draw( _batchStart, count );
		_batchStart = Count;
		DrawCalls++;
	}

	void Append( in UICssBoxBatched.BoxInstance instance, BlendMode blendMode )
	{
		if ( _blendMode != blendMode ) FlushBatch();
		_blendMode = blendMode;
		Add( instance );
		TrackBackdropWrite( instance );
	}

	internal void Add( in BoxDescriptor descriptor, Matrix? transform = null, int clipIndex = -1 )
	{
		Add( descriptor, 1, descriptor.OverrideBlendMode, transform, clipIndex );
	}

	internal void Add( in BoxDescriptor descriptor, float opacity, BlendMode blendMode, Matrix? transform = null, int clipIndex = -1 )
	{
		Resolve( descriptor, transform ?? Matrix.Identity, clipIndex, out var instance );
		instance.ApplyOpacity( opacity );
		Append( instance, blendMode );
	}

	/// <summary>
	/// Resolves and submits a solid painter shape without the image, gradient and border
	/// fields of a CSS descriptor. Color already includes inherited and drawing opacity.
	/// </summary>
	internal void AddSolidShape( Rect bounds, Color color, int shapeIndex, BackgroundClip backgroundClip,
		in Vector4 fillInsets, in Matrix transform, int clipIndex, BlendMode blendMode )
	{
		UICssBoxBatched.BoxInstance instance = default;
		instance.Rect = new Vector4( bounds.Left, bounds.Top, bounds.Width, bounds.Height );
		instance.Color = color;
		instance.ShapeIndex = shapeIndex;
		instance.InverseScissorIndex = -1;
		instance.BackgroundClip = (int)backgroundClip;
		instance.BackgroundClipRect = fillInsets;
		ResolveSpatial( ref instance, clipIndex, Destination.ResolveSpatial( this, transform ) );
		Append( instance, blendMode );
	}

	internal void Add( in ShadowDescriptor descriptor, Matrix? transform = null, int clipIndex = -1 )
	{
		Resolve( descriptor, transform ?? Matrix.Identity, clipIndex, out var instance );
		Append( instance, descriptor.Inset ? descriptor.OverrideBlendMode : BlendMode.Normal );
	}

	internal void Add( in OutlineDescriptor descriptor, Matrix? transform = null, int clipIndex = -1 )
	{
		Resolve( descriptor, transform ?? Matrix.Identity, clipIndex, out var instance );
		Append( instance, descriptor.OverrideBlendMode );
	}

	internal void Clear( Color color )
	{
		Flush();
		_commands.Clear( new Color( color.r * color.a, color.g * color.a, color.b * color.a, color.a ), clearDepth: false, clearStencil: false );
	}

	internal void Clear()
	{
		InvalidateBackdrop();
		_layerIndex = 0;
		_textures.Clear();
		AdvanceFrame();
		_batchStart = 0;
		_batchIndex = 0;
		DrawCalls = 0;
		FrameGrabs = 0;
		_targetDepth = 0;
		_clipDepth = 0;
		Destination = new();
		ClearClips();
	}

	internal void ClearClips()
	{
		DrawClips.Clear();
		_drawClipLookup.Clear();
	}

	internal string NextLayerName()
	{
		lock ( _layerNames )
		{
			if ( _layerIndex == _layerNames.Count )
				_layerNames.Add( $"Painter.Layer.{_layerIndex}" );

			return _layerNames[_layerIndex++];
		}
	}

	static readonly string[] ScissorRectAttribute = ["ScissorRect0", "ScissorRect1", "ScissorRect2", "ScissorRect3"];
	static readonly string[] ScissorRadiiHAttribute = ["ScissorRadiiH0", "ScissorRadiiH1", "ScissorRadiiH2", "ScissorRadiiH3"];
	static readonly string[] ScissorRadiiVAttribute = ["ScissorRadiiV0", "ScissorRadiiV1", "ScissorRadiiV2", "ScissorRadiiV3"];
	static readonly string[] ScissorMatAttribute = ["ScissorMat0", "ScissorMat1", "ScissorMat2", "ScissorMat3"];

	/// <summary>
	/// The clip stack for shaders that draw one quad at a time, see ui/scissor.hlsl. Invert isn't carried - only
	/// the batched shadow path uses it.
	/// </summary>
	internal static void SetScissorAttributes( CommandList commandList, in Painter.Scissoring scissor )
	{
		commandList.Attributes.Set( "HasScissor", scissor.Count > 0 ? 1 : 0 );
		commandList.Attributes.Set( "ScissorCount", scissor.Count );

		for ( int i = 0; i < scissor.Count; i++ )
		{
			var c = scissor.Clips[i].ForShader();
			commandList.Attributes.Set( ScissorRectAttribute[i], c.Rect.ToVector4() );
			commandList.Attributes.Set( ScissorRadiiHAttribute[i], c.Radii.Horizontal );
			commandList.Attributes.Set( ScissorRadiiVAttribute[i], c.Radii.Vertical );
			commandList.Attributes.Set( ScissorMatAttribute[i], c.Matrix );
		}
	}
}
