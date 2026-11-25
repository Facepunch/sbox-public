using System;
using ShimSkiaSharp;
using Svg.Model.Drawables;
using Svg.Skia;
using SvgLib = Svg;

namespace Sandbox.Utility.Svg;

/// <summary>
/// How to determine which sections of the path are filled.
/// </summary>
public enum PathFillType
{
	/// <summary>
	/// Clockwise paths are filled, counter-clockwise are empty.
	/// </summary>
	Winding = 0,

	/// <summary>
	/// Regions that are enclosed by an odd number of paths are filled, other regions are empty.
	/// </summary>
	EvenOdd = 1
}

/// <summary>
/// Controls arc size in <see cref="ArcToPathCommand"/>.
/// </summary>
public enum PathArcSize
{
	Small = 0,
	Large = 1
}

/// <summary>
/// Controls arc direction in <see cref="ArcToPathCommand"/>.
/// </summary>
public enum PathDirection
{
	Clockwise = 0,
	CounterClockwise = 1
}

/// <summary>
/// Base class for SVG path commands.
/// </summary>
public abstract record PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Element/circle"/>.
/// </summary>
public record AddCirclePathCommand( float X, float Y, float Radius ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Element/ellipse"/>.
/// </summary>
public record AddOvalPathCommand( Rect Rect ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Element/polyline"/>, <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Element/polygon"/>.
/// </summary>
public record AddPolyPathCommand( IReadOnlyList<Vector2> Points, bool Close ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Element/rect"/>.
/// </summary>
public record AddRectPathCommand( Rect Rect ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Element/rect"/>.
/// </summary>
public record AddRoundRectPathCommand( Rect Rect, float Rx, float Ry ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths#arcs"/>.
/// </summary>
public record ArcToPathCommand( float Rx, float Ry, float XAxisRotate, PathArcSize LargeArc, PathDirection Sweep, float X, float Y ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths#line_commands"/>.
/// </summary>
public record ClosePathCommand : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths#b%C3%A9zier_curves"/>.
/// </summary>
public record CubicToPathCommand( float X0, float Y0, float X1, float Y1, float X2, float Y2 ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths#line_commands"/>.
/// </summary>
public record LineToPathCommand( float X, float Y ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths#line_commands"/>.
/// </summary>
public record MoveToPathCommand( float X, float Y ) : PathCommand;

/// <summary>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths#b%C3%A9zier_curves"/>.
/// </summary>
public record QuadToPathCommand( float X0, float Y0, float X1, float Y1 ) : PathCommand;

/// <summary>
/// A shape in a <see cref="SvgDocument"/>, described as a vector path.
/// </summary>
public class SvgPath
{
	/// <summary>
	/// How to determine which sections of the path are filled.
	/// </summary>
	public PathFillType FillType { get; }

	/// <summary>
	/// Description of how the path is constructed out of basic elements.
	/// </summary>
	public IReadOnlyList<PathCommand> Commands { get; }

	/// <summary>
	/// If true, this path has no commands.
	/// </summary>
	public bool IsEmpty => Commands.Count == 0;

	/// <summary>
	/// Enclosing bounding box for this path.
	/// </summary>
	public Rect Bounds { get; }

	/// <summary>
	/// Optional outline color for this path.
	/// </summary>
	public Color? StrokeColor { get; }

	/// <summary>
	/// Optional fill color for this path.
	/// </summary>
	public Color? FillColor { get; }

	internal SvgPath( SKPath skPath, SKColor? strokeColor, SKColor? fillColor )
	{
		var commands = new List<PathCommand>();

		FillType = (PathFillType)skPath.FillType;
		Commands = commands;
		Bounds = SvgDocument.ConvertRect( skPath.Bounds );

		StrokeColor = SvgDocument.ConvertColor( strokeColor );
		FillColor = SvgDocument.ConvertColor( fillColor );

		if ( skPath.Commands == null )
		{
			return;
		}

		foreach ( var cmd in skPath.Commands )
		{
			switch ( cmd )
			{
				case ShimSkiaSharp.MoveToPathCommand moveToPathCommand:
					commands.Add( new MoveToPathCommand( moveToPathCommand.X, moveToPathCommand.Y ) );
					break;
				case ShimSkiaSharp.LineToPathCommand lineToPathCommand:
					commands.Add( new LineToPathCommand( lineToPathCommand.X, lineToPathCommand.Y ) );
					break;
				case ShimSkiaSharp.ArcToPathCommand arcToPathCommand:
					commands.Add( new ArcToPathCommand( arcToPathCommand.Rx, arcToPathCommand.Ry,
						arcToPathCommand.XAxisRotate, (PathArcSize)arcToPathCommand.LargeArc,
						(PathDirection)arcToPathCommand.Sweep, arcToPathCommand.X, arcToPathCommand.Y ) );
					break;
				case ShimSkiaSharp.QuadToPathCommand quadToPathCommand:
					commands.Add( new QuadToPathCommand( quadToPathCommand.X0, quadToPathCommand.Y0,
						quadToPathCommand.X1, quadToPathCommand.Y1 ) );
					break;
				case ShimSkiaSharp.CubicToPathCommand cubicToPathCommand:
					commands.Add( new CubicToPathCommand( cubicToPathCommand.X0, cubicToPathCommand.Y0,
						cubicToPathCommand.X1, cubicToPathCommand.Y1,
						cubicToPathCommand.X2, cubicToPathCommand.Y2 ) );
					break;
				case ShimSkiaSharp.ClosePathCommand:
					commands.Add( new ClosePathCommand() );
					break;
				case ShimSkiaSharp.AddRectPathCommand addRectPathCommand:
					commands.Add( new AddRectPathCommand( SvgDocument.ConvertRect( addRectPathCommand.Rect ) ) );
					break;
				case ShimSkiaSharp.AddRoundRectPathCommand addRoundRectPathCommand:
					commands.Add( new AddRoundRectPathCommand( SvgDocument.ConvertRect( addRoundRectPathCommand.Rect ), addRoundRectPathCommand.Rx, addRoundRectPathCommand.Ry ) );
					break;
				case ShimSkiaSharp.AddOvalPathCommand addOvalPathCommand:
					commands.Add( new AddOvalPathCommand( SvgDocument.ConvertRect( addOvalPathCommand.Rect ) ) );
					break;
				case ShimSkiaSharp.AddCirclePathCommand addCirclePathCommand:
					commands.Add( new AddCirclePathCommand( addCirclePathCommand.X, addCirclePathCommand.Y, addCirclePathCommand.Radius ) );
					break;
				case ShimSkiaSharp.AddPolyPathCommand addPolyPathCommand:
					commands.Add( new AddPolyPathCommand(
						addPolyPathCommand.Points?.Select( x => new Vector2( x.X, x.Y ) ).ToList() ??
						(IReadOnlyList<Vector2>)Array.Empty<Vector2>(), addPolyPathCommand.Close ) );
					break;
			}
		}
	}
}

/// <summary>
/// Helper class for reading Scalable Vector Graphics files.
/// </summary>
public class SvgDocument
{
	internal static Rect ConvertRect( SKRect rect )
	{
		return new Rect( rect.Left, rect.Top, rect.Width, rect.Height );
	}

	internal static Color? ConvertColor( SKColor? color )
	{
		if ( color is null )
		{
			return null;
		}

		return Color.FromBytes( color.Value.Red, color.Value.Green, color.Value.Blue, color.Value.Alpha );
	}

	private static void ExtractPaths( SKDrawable drawable, List<SvgPath> outPaths )
	{
		switch ( drawable )
		{
			case DrawablePath { Path: { } path } drawablePath:
				outPaths.Add( new SvgPath( path, drawablePath.Stroke?.Color, drawablePath.Fill?.Color ) );
				break;
			case DrawableContainer { ChildrenDrawables: { } children }:
				{
					foreach ( var child in children )
					{
						ExtractPaths( child, outPaths );
					}

					break;
				}
		}
	}

	/// <summary>
	/// Reads an SVG document from the given string, returning a list of path elements
	/// describing the shapes in the image.
	/// </summary>
	/// <param name="contents">SVG document contents.</param>
	public static SvgDocument FromString( string contents )
	{
		var svgDoc = SvgSanitizer.Parse( contents );

		using var svg = new SKSvg();

		svg.FromSvgDocument( svgDoc );

		var paths = new List<SvgPath>();

		ExtractPaths( svg.Drawable, paths );

		return new SvgDocument( paths );
	}

	/// <summary>
	/// List of all shapes in the document.
	/// </summary>
	public IReadOnlyList<SvgPath> Paths { get; }

	private SvgDocument( IReadOnlyList<SvgPath> paths )
	{
		Paths = paths;
	}
}
