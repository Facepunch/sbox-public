using Borealis.Sdl.Native;
using static Borealis.Sdl.Native.SdlNative;

namespace Borealis.Sdl;

/// <summary>
/// A connected display, as it was when asked for.
/// </summary>
public sealed record SdlDisplay
{
	/// <summary>SDL's id for it.</summary>
	public uint Id { get; init; }

	/// <summary>Its name, if the system knows one.</summary>
	public string Name { get; init; }

	/// <summary>Its position on the desktop, in window units.</summary>
	public int X { get; init; }

	/// <summary>Its position on the desktop, in window units.</summary>
	public int Y { get; init; }

	/// <summary>Its size, in window units.</summary>
	public int Width { get; init; }

	/// <summary>Its size, in window units.</summary>
	public int Height { get; init; }

	/// <summary>The scale the user chose for content on it (1.5 for 150%).</summary>
	public float ContentScale { get; init; }

	/// <summary>Pixels per window unit in its current mode (2 on Retina and scaled Wayland outputs, else 1).</summary>
	public float PixelDensity { get; init; }

	/// <summary>Its refresh rate in Hz, or 0 when unknown.</summary>
	public float RefreshRate { get; init; }

	internal static unsafe SdlDisplay From( uint id )
	{
		SdlRect bounds = default;
		SDL_GetDisplayBounds( id, &bounds );
		var mode = SDL_GetCurrentDisplayMode( id );
		return new SdlDisplay
		{
			Id = id,
			Name = SdlNative.String( SDL_GetDisplayName( id ) ),
			X = bounds.X,
			Y = bounds.Y,
			Width = bounds.W,
			Height = bounds.H,
			ContentScale = SDL_GetDisplayContentScale( id ),
			PixelDensity = mode == null ? 1 : mode->PixelDensity,
			RefreshRate = mode == null ? 0 : mode->RefreshRate,
		};
	}
}
