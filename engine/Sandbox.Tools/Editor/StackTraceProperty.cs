using System;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace Editor;

/// <summary>
/// Marks a method as a custom handler for stack trace lines matching a certain pattern.
/// The method must take in a <see cref="Match"/> parameter, and return
/// a <see cref="StackRow"/> (or null).
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class StackLineHandlerAttribute : Attribute
{
	[RegexPattern]
	public string Regex { get; }
	public int Order { get; set; }

	public StackLineHandlerAttribute( [RegexPattern] string regex )
	{
		Regex = regex;
	}
}

internal class StackTraceProperty : Widget
{
	private delegate StackRow StackLineHandler( Match match );

	private record struct StackLineHandlerInfo
		( Regex Regex, StackLineHandler Handler, int Order ) : IComparable<StackLineHandlerInfo>
	{
		public int CompareTo( StackLineHandlerInfo other ) => Order - other.Order;
	}

	[SkipHotload]
	private static List<StackLineHandlerInfo> StackLineHandlers { get; set; }

	[EditorEvent.Hotload]
	private static void OnHotload()
	{
		StackLineHandlers = null;
	}

	private static void UpdateStackLineHandlers()
	{
		if ( StackLineHandlers != null )
		{
			return;
		}

		StackLineHandlers = new List<StackLineHandlerInfo>();

		var methods = EditorTypeLibrary.GetMethodsWithAttribute<StackLineHandlerAttribute>();

		foreach ( var (method, attrib) in methods )
		{
			if ( method.Parameters.Length != 1 )
			{
				continue;
			}

			try
			{
				var regex = new Regex( attrib.Regex );
				var handler = (StackLineHandler)Delegate.CreateDelegate( typeof( StackLineHandler ),
					(MethodInfo)method.MemberInfo );
				StackLineHandlers.Add( new( regex, handler, attrib.Order ) );
			}
			catch ( Exception e )
			{
				Log.Warning( $"{method.MemberInfo.ToSimpleString()}: {e}" );
			}
		}

		StackLineHandlers.Sort();
	}

	public StackTraceProperty( Widget parent, LogEvent e ) : base( parent )
	{
		Layout = Layout.Column();

		MinimumWidth = 400;

		var scroller = new ScrollArea( this );
		Layout.Add( scroller, 1 );

		scroller.Canvas = new Widget( scroller );
		scroller.Canvas.Layout = Layout.Column();

		var row = scroller.Canvas.Layout.AddRow();

		var message = row.Add( new Label( e.Message, this ), 1 );
		message.Margin = 8;
		message.WordWrap = true;
		message.Cursor = CursorShape.Finger;
		message.Name = "Message";
		message.ToolTip = "Copy this message to your clipboard";
		message.MouseClick = () =>
		{
			var message = e.Message;
			message += "\n";
			message += e.Stack;
			EditorUtility.Clipboard.Copy( message );
		};

		var lines = (e.Stack ?? "").Split( '\n', '\r' );

		var index = 0;

		foreach ( var line in lines )
		{
			if ( AddStackLine( line, scroller.Canvas.Layout ) is { } addedRow )
			{
				addedRow.Index = index++;
			}
		}

		scroller.Canvas.Layout.AddStretchCell();
	}

	[StackLineHandler( @"^at (.+?) in ([A-Za-z]:\\.+|/.+|\\\\.+):line (\d+)$", Order = 1000 )]
	public static StackRow DefaultStackLineHandler( Match match )
	{
		var hasFile = match.Groups[2].Success;
		var isGenerated = match.Groups[2].Value.StartsWith( "Sandbox.Generator." );
		var functionName = match.Groups[1].Value;
		if ( functionName.IndexOf( '(' ) > 0 ) functionName = functionName.Substring( 0, functionName.IndexOf( '(' ) );

		if ( !hasFile )
		{
			return UnknownStackLineHandler( match );
		}

		var fileName = match.Groups[2].Value;
		var fileLine = match.Groups[3].Value;

		var row = new StackRow( functionName, $"{fileName}:{fileLine}" )
		{
			IsFromEngine = match.Value.Contains( "\\engine\\Sandbox." )
		};

		row.MouseClick += () => CodeEditor.OpenFile( fileName, fileLine.ToInt() );

		return row;
	}

	[StackLineHandler( @".+", Order = 1001 )]
	public static StackRow UnknownStackLineHandler( Match match )
	{
		return new StackRow( match.Value, null )
		{
			IsFromEngine = match.Value.Contains( "\\engine\\Sandbox." )
		};
	}

	private StackRow AddStackLine( string line, Layout target )
	{
		if ( string.IsNullOrWhiteSpace( line ) )
			return null;

		UpdateStackLineHandlers();

		line = line.Trim();

		foreach ( var (regex, handler, _) in StackLineHandlers )
		{
			var match = regex.Match( line );

			if ( !match.Success )
				continue;

			if ( handler( match ) is not { } row )
				return null;

			target.Add( row );
			return row;
		}

		return null;
	}
}

public class StackRow : Frame
{
	string FunctionName;
	string FileName;
	public bool IsFromEngine;
	public bool IsFromAction;
	public int Index;

	public StackRow( string functionName, string fileName ) : base( null )
	{
		FunctionName = functionName;
		FileName = fileName;
		MinimumSize = FileName != null ? 48 : 24;

		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		var r = new Rect( 0, Size );
		var c = IsFromEngine ? Theme.Blue : IsFromAction ? Theme.Yellow : Theme.Green;

		if ( FileName == null ) c = Theme.Blue.Darken( 0.3f ).Desaturate( 0.5f );

		if ( IsUnderMouse ) c = c.Lighten( 0.3f );

		bool hasFileName = FileName != null;
		// Push message rect up a bit
		if ( hasFileName ) r.Top = r.Top - 16.0f;

		Paint.ClearPen();
		Paint.SetBrush( c.Darken( 0.8f + (Index & 1) * 0.02f ).Desaturate( 0.3f ) );
		Paint.DrawRect( r );

		r = r.Grow( -8.0f, 0 );

		Paint.SetPen( c );
		var textSize = Paint.DrawText( r, FunctionName, TextFlag.LeftCenter );

		if ( hasFileName )
		{
			r.Top = textSize.Bottom - 4;
			r = r.Shrink( 0, 0, 0, 8 );

			Paint.SetPen( c.Darken( 0.3f ) );
			Paint.DrawText( r, FileName, TextFlag.LeftBottom );
		}
	}
}
