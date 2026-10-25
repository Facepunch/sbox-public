using System;
using System.Collections.Generic;
using System.Threading;
using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Engine;

internal sealed class Cmaa2Renderer : IDisposable
{
	private const string ComputeShaderPath = "postprocess/cmaa2_cs";
	private const string CompositeShaderPath = "shaders/postprocess/cmaa2.shader";
	private const string UnsupportedMessage = "CMAA2 skipped: unsupported viewport size or color format.";
	private const int MaxDispatchGroups = 65535;
	private const int MaxCandidateGroups = MaxDispatchGroups * 128;
	private const int MaxLocationGroups = MaxDispatchGroups * 32;
	private const int MaxBlendItemAddress = 0x03ffffff;

	private static readonly object ShaderLock = new();
	private static ComputeShader _computeShader;
	private static Material _compositeMaterial;

	private readonly object _resourceLock = new();
	private readonly List<ResourceSet> _resourceSets = new();
	private int _warnedUnsupported;
	private bool _disposed;

	internal static bool IsAvailable => _computeShader is not null && _compositeMaterial is not null;

	internal static void Initialize()
	{
		lock ( ShaderLock )
		{
			if ( IsAvailable ) return;

			var compute = new ComputeShader( ComputeShaderPath );
			ValidateShaderPath( compute.ComputeMaterial, ComputeShaderPath );

			var composite = Material.FromShader( CompositeShaderPath );
			ValidateShaderPath( composite, CompositeShaderPath );

			_computeShader = compute;
			_compositeMaterial = composite;
		}
	}

	internal static void Shutdown()
	{
		lock ( ShaderLock )
		{
			// Material.Shutdown owns the cached materials' destruction.
			_computeShader = null;
			_compositeMaterial = null;
		}
	}

	private static void ValidateShaderPath( Material material, string expectedPath )
	{
		var materialShaderPath = material is { IsValid: true } ? material.ShaderName : null;
		var normalizedMaterialPath = NormalizeShaderPath( materialShaderPath );
		var shader = string.IsNullOrEmpty( normalizedMaterialPath ) ? null : Shader.Load( $"shaders/{normalizedMaterialPath}.shader" );
		var actualPath = shader?.native.GetFilename();
		if ( material is { IsValid: true } && PathsMatch( materialShaderPath, expectedPath )
			&& shader is { IsValid: true } && !shader.native.IsError() && PathsMatch( actualPath, expectedPath ) )
			return;

		var message = $"CMAA2 failed to load shader '{expectedPath}' (resolved '{actualPath ?? "<null>"}').";
		Log.Error( message );
		throw new InvalidOperationException( message );
	}

	private static bool PathsMatch( string actualPath, string expectedPath )
	{
		return string.Equals( NormalizeShaderPath( actualPath ), NormalizeShaderPath( expectedPath ), StringComparison.OrdinalIgnoreCase );
	}

	private static string NormalizeShaderPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return string.Empty;

		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( path.StartsWith( "shaders/", StringComparison.OrdinalIgnoreCase ) )
			path = path[8..];
		if ( path.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) )
			path = path[..^7];

		return path;
	}

	internal void Render()
	{
		var compute = _computeShader;
		var composite = _compositeMaterial;
		if ( compute is null || composite is null ) return;

		var viewport = Graphics.Viewport;
		var width = viewport.Width.CeilToInt();
		var height = viewport.Height.CeilToInt();
		var frame = Application.FrameCount;
		RetireUnusedResources( frame );

		var colorFormat = Graphics.IdealColorFormat;
		if ( width <= 0 || height <= 0 || width > 16384 || height > 16384 || (colorFormat != ImageFormat.RGBA8888 && colorFormat != ImageFormat.RGBA16161616F) )
		{
			WarnUnsupportedOnce();
			return;
		}

		var pixelCount = checked(width * height);
		var candidateCapacity = Capacity( pixelCount / 4L, MaxCandidateGroups );
		var blendItemCapacity = Capacity( pixelCount / 2L, MaxBlendItemAddress );
		var blendLocationCapacity = Capacity( (pixelCount + 3L) / 6L, MaxLocationGroups );
		var resources = ReserveResourceSet( width, height, candidateCapacity, blendItemCapacity, blendLocationCapacity, frame );
		if ( resources is null ) return;

		RenderTarget colorTarget = null;
		RenderTarget edgesTarget = null;
		RenderTarget blendHeadsTarget = null;

		try
		{
			try
			{
				colorTarget = RenderTarget.GetTemporary( width, height, colorFormat, ImageFormat.None, MultisampleAmount.MultisampleNone, 1, "Cmaa2Color" );
				edgesTarget = RenderTarget.GetTemporary( (width + 1) / 2, height, ImageFormat.R32_UINT, ImageFormat.None, MultisampleAmount.MultisampleNone, 1, "Cmaa2Edges" );
				blendHeadsTarget = RenderTarget.GetTemporary( (width + 1) / 2, (height + 1) / 2, ImageFormat.R32_UINT, ImageFormat.None, MultisampleAmount.MultisampleNone, 1, "Cmaa2BlendHeads" );
			}
			catch ( Exception exception )
			{
				Log.Error( exception, "CMAA2 failed to allocate temporary render targets." );
				RemoveResourceSet( resources );
				throw;
			}

			if ( !IsValidTarget( colorTarget ) || !IsValidTarget( edgesTarget ) || !IsValidTarget( blendHeadsTarget ) )
			{
				Log.Error( "CMAA2 failed to allocate a valid temporary render target." );
				RemoveResourceSet( resources );
				return;
			}

			RenderTools.ResolveFrameBuffer( Graphics.Context, colorTarget.ColorTarget.native, viewport );

			Graphics.ResourceBarrierTransition( colorTarget.ColorTarget, ResourceState.NonPixelShaderResource );
			Graphics.ResourceBarrierTransition( edgesTarget.ColorTarget, ResourceState.UnorderedAccess );
			Graphics.ResourceBarrierTransition( blendHeadsTarget.ColorTarget, ResourceState.UnorderedAccess );
			Graphics.ResourceBarrierTransition( resources.Candidates, ResourceState.UnorderedAccess );
			Graphics.ResourceBarrierTransition( resources.BlendLocations, ResourceState.UnorderedAccess );
			Graphics.ResourceBarrierTransition( resources.BlendItems, ResourceState.UnorderedAccess );
			if ( resources.DispatchArgsInIndirectState )
				Graphics.ResourceBarrierTransition( resources.DispatchArgs, ResourceState.IndirectArgument, ResourceState.UnorderedAccess );
			else
				Graphics.ResourceBarrierTransition( resources.DispatchArgs, ResourceState.UnorderedAccess );
			resources.DispatchArgsInIndirectState = false;

			if ( resources.ControlInUnorderedAccess )
				Graphics.ResourceBarrierTransition( resources.Control, ResourceState.UnorderedAccess, ResourceState.CopyDestination );
			else
				Graphics.ResourceBarrierTransition( resources.Control, ResourceState.CopyDestination );
			resources.ControlInUnorderedAccess = false;
			resources.Control.Clear();
			Graphics.ResourceBarrierTransition( resources.Control, ResourceState.CopyDestination, ResourceState.UnorderedAccess );
			resources.ControlInUnorderedAccess = true;

			// These resources remain UAVs between invocations, so a same-layout transition cannot order their prior writes.
			Graphics.UavBarrier( edgesTarget.ColorTarget );
			Graphics.UavBarrier( blendHeadsTarget.ColorTarget );
			Graphics.UavBarrier( resources.Candidates );
			Graphics.UavBarrier( resources.BlendLocations );
			Graphics.UavBarrier( resources.BlendItems );

			var srgb = colorFormat == ImageFormat.RGBA8888 ? 1 : 0;
			SetComputeAttributes( resources, colorTarget, edgesTarget, blendHeadsTarget, width, height, srgb, 0, includeColor: true, includeOutput: false );
			compute.DispatchWithAttributes( resources.Attributes, ((width + 27) / 28) * 16, ((height + 27) / 28) * 16, 1 );

			Graphics.UavBarrier( edgesTarget.ColorTarget );
			Graphics.UavBarrier( blendHeadsTarget.ColorTarget );
			Graphics.UavBarrier( resources.Candidates );
			Graphics.UavBarrier( resources.Control );

			SetComputeAttributes( resources, colorTarget, edgesTarget, blendHeadsTarget, width, height, srgb, 1, includeColor: false, includeOutput: false );
			compute.DispatchWithAttributes( resources.Attributes, 2, 1, 1 );
			Graphics.UavBarrier( resources.Control );
			Graphics.ResourceBarrierTransition( resources.DispatchArgs, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
			resources.DispatchArgsInIndirectState = true;

			SetComputeAttributes( resources, colorTarget, edgesTarget, blendHeadsTarget, width, height, srgb, 2, includeColor: true, includeOutput: false );
			compute.DispatchIndirectWithAttributes( resources.Attributes, resources.DispatchArgs );

			Graphics.UavBarrier( resources.BlendItems );
			Graphics.UavBarrier( blendHeadsTarget.ColorTarget );
			Graphics.UavBarrier( resources.BlendLocations );
			Graphics.UavBarrier( resources.Control );

			Graphics.ResourceBarrierTransition( resources.DispatchArgs, ResourceState.IndirectArgument, ResourceState.UnorderedAccess );
			resources.DispatchArgsInIndirectState = false;
			SetComputeAttributes( resources, colorTarget, edgesTarget, blendHeadsTarget, width, height, srgb, 1, includeColor: false, includeOutput: false );
			compute.DispatchWithAttributes( resources.Attributes, 1, 2, 1 );
			Graphics.UavBarrier( resources.Control );
			Graphics.ResourceBarrierTransition( resources.DispatchArgs, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
			resources.DispatchArgsInIndirectState = true;

			SetComputeAttributes( resources, colorTarget, edgesTarget, blendHeadsTarget, width, height, srgb, 3, includeColor: false, includeOutput: true );
			Graphics.UavBarrier( colorTarget.ColorTarget );
			Graphics.ResourceBarrierTransition( colorTarget.ColorTarget, ResourceState.UnorderedAccess );
			compute.DispatchIndirectWithAttributes( resources.Attributes, resources.DispatchArgs );

			resources.Attributes.Clear();
			Graphics.UavBarrier( colorTarget.ColorTarget );
			Graphics.ResourceBarrierTransition( colorTarget.ColorTarget, ResourceState.PixelShaderResource );
			resources.Attributes.Set( "Cmaa2Color", colorTarget.ColorTarget );
			Graphics.Blit( composite, resources.Attributes );
		}
		finally
		{
			resources.Attributes.Clear();
			blendHeadsTarget?.Dispose();
			edgesTarget?.Dispose();
			colorTarget?.Dispose();
		}
	}

	internal void ReleaseResources()
	{
		QueueAllResources( markDisposed: false );
	}

	public void Dispose()
	{
		QueueAllResources( markDisposed: true );
	}

	private void QueueAllResources( bool markDisposed )
	{
		List<ResourceSet> resources;
		lock ( _resourceLock )
		{
			if ( markDisposed ) _disposed = true;
			if ( _resourceSets.Count == 0 ) return;

			resources = new List<ResourceSet>( _resourceSets );
			_resourceSets.Clear();
		}

		foreach ( var resource in resources )
			EngineLoop.DisposeAtFrameEnd( resource );
	}

	private ResourceSet ReserveResourceSet( int width, int height, int candidateCapacity, int blendItemCapacity, int blendLocationCapacity, ulong frame )
	{
		lock ( _resourceLock )
		{
			if ( _disposed ) return null;

			foreach ( var resources in _resourceSets )
			{
				if ( resources.Width != width || resources.Height != height || resources.LastUsedFrame == frame ) continue;
				resources.LastUsedFrame = frame;
				return resources;
			}
		}

		ResourceSet created = null;
		try
		{
#pragma warning disable CA2000 // Ownership moves to the cache or the frame-end disposal queue.
			created = new ResourceSet( width, height );
#pragma warning restore CA2000
			try
			{
				if ( !created.CreateBuffers( candidateCapacity, blendItemCapacity, blendLocationCapacity ) )
				{
					Log.Error( "CMAA2 failed to allocate working buffers." );
					return null;
				}
			}
			catch ( Exception exception )
			{
				Log.Error( exception, "CMAA2 failed to allocate working buffers." );
				throw;
			}

			lock ( _resourceLock )
			{
				if ( _disposed ) return null;

				created.LastUsedFrame = frame;
				_resourceSets.Add( created );
				var resources = created;
				created = null;
				return resources;
			}
		}
		finally
		{
			if ( created is not null )
				EngineLoop.DisposeAtFrameEnd( created );
		}
	}

	private void RetireUnusedResources( ulong frame )
	{
		List<ResourceSet> retired = null;
		lock ( _resourceLock )
		{
			for ( var index = _resourceSets.Count - 1; index >= 0; index-- )
			{
				var resources = _resourceSets[index];
				if ( frame - resources.LastUsedFrame < 8 ) continue;

				retired ??= new List<ResourceSet>();
				retired.Add( resources );
				_resourceSets.RemoveAt( index );
			}
		}

		if ( retired is null ) return;
		foreach ( var resources in retired )
			EngineLoop.DisposeAtFrameEnd( resources );
	}

	private void RemoveResourceSet( ResourceSet resources )
	{
		bool removed;
		lock ( _resourceLock )
			removed = _resourceSets.Remove( resources );

		if ( removed )
			EngineLoop.DisposeAtFrameEnd( resources );
	}

	private static void SetComputeAttributes( ResourceSet resources, RenderTarget colorTarget, RenderTarget edgesTarget, RenderTarget blendHeadsTarget, int width, int height, int srgb, int pass, bool includeColor, bool includeOutput )
	{
		var attributes = resources.Attributes;
		attributes.Clear();
		attributes.SetCombo( "D_PASS", pass );
		attributes.SetCombo( "D_SRGB", srgb );
		attributes.Set( "Cmaa2Dimensions", new Vector2Int( width, height ) );
		attributes.Set( "Cmaa2CandidateCapacity", (uint)resources.Candidates.ElementCount );
		attributes.Set( "Cmaa2BlendLocationCapacity", (uint)resources.BlendLocations.ElementCount );
		attributes.Set( "Cmaa2BlendItemCapacity", (uint)resources.BlendItems.ElementCount );
		attributes.Set( "Cmaa2Edges", edgesTarget.ColorTarget );
		attributes.Set( "Cmaa2BlendHeads", blendHeadsTarget.ColorTarget );
		attributes.Set( "Cmaa2Candidates", resources.Candidates );
		attributes.Set( "Cmaa2BlendLocations", resources.BlendLocations );
		attributes.Set( "Cmaa2BlendItems", resources.BlendItems );
		attributes.Set( "Cmaa2Control", resources.Control );
		if ( pass == 1 )
			attributes.Set( "Cmaa2DispatchArgs", resources.DispatchArgs );

		if ( includeColor )
			attributes.Set( "Cmaa2Color", colorTarget.ColorTarget );
		if ( includeOutput )
			attributes.Set( "Cmaa2Output", colorTarget.ColorTarget );
	}

	private void WarnUnsupportedOnce()
	{
		if ( Interlocked.Exchange( ref _warnedUnsupported, 1 ) == 0 )
			Log.Warning( UnsupportedMessage );
	}

	private static int Capacity( long value, int maximum ) => (int)Math.Clamp( value, 1L, maximum );

	private static bool IsValidTarget( RenderTarget target )
	{
		return target?.ColorTarget is { IsValid: true, IsError: false };
	}

	private sealed class ResourceSet : IDisposable
	{
		internal readonly int Width;
		internal readonly int Height;
		internal readonly RenderAttributes Attributes = new();
		internal ulong LastUsedFrame;
		internal bool ControlInUnorderedAccess;
		internal bool DispatchArgsInIndirectState;
		internal GpuBuffer<uint> Candidates;
		internal GpuBuffer<uint> BlendLocations;
		internal GpuBuffer BlendItems;
		internal GpuBuffer<uint> Control;
		internal GpuBuffer<GpuBuffer.IndirectDispatchArguments> DispatchArgs;

		internal ResourceSet( int width, int height )
		{
			Width = width;
			Height = height;
		}

		internal bool CreateBuffers( int candidateCapacity, int blendItemCapacity, int blendLocationCapacity )
		{
			Candidates = new GpuBuffer<uint>( candidateCapacity, GpuBuffer.UsageFlags.Structured, "Cmaa2Candidates" );
			if ( !Candidates.IsValid ) return false;

			BlendLocations = new GpuBuffer<uint>( blendLocationCapacity, GpuBuffer.UsageFlags.Structured, "Cmaa2BlendLocations" );
			if ( !BlendLocations.IsValid ) return false;

			BlendItems = new GpuBuffer( blendItemCapacity, 8, GpuBuffer.UsageFlags.Structured, "Cmaa2BlendItems" );
			if ( !BlendItems.IsValid ) return false;

			Control = new GpuBuffer<uint>( 16, GpuBuffer.UsageFlags.Structured, "Cmaa2Control" );
			if ( !Control.IsValid ) return false;

			DispatchArgs = new GpuBuffer<GpuBuffer.IndirectDispatchArguments>( 1, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "Cmaa2DispatchArgs" );
			return DispatchArgs.IsValid;
		}

		public void Dispose()
		{
			Attributes.Clear();
			Candidates?.Dispose();
			BlendLocations?.Dispose();
			BlendItems?.Dispose();
			Control?.Dispose();
			DispatchArgs?.Dispose();
		}
	}
}
