namespace Facepunch.InteropGen;


[TypeName( "qreal" )]
public class ArgQReal : Arg
{
	public override string ManagedType => "float";
	public override string ManagedDelegateType => ManagedType;
}

[TypeName( "qicon" )]
public class ArgQIcon : ArgString
{
	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"FindOrCreateQIcon( {code} )" : base.ToInterop( native, code );
	}
}

[TypeName( "qpointf" )]
public class ArgQPointF : Arg
{
	public override string ManagedType => "Vector3";
	public override string NativeType => "Vector";

	public override string ReturnWrapCall( string call, bool native )
	{
		if ( native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.x(), __r.y(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, native );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"QPointF( {code}.x, {code}.y )" : base.FromInterop( native, code );
	}
}

[TypeName( "qbytearray" )]
public class ArgQByteArray : ArgString
{
	public override string ReturnWrapCall( string call, bool native )
	{
		return native ? $"\t\treturn SafeReturnString( ({call}).toBase64() );" : base.ReturnWrapCall( call, native );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"QByteArray::fromBase64( {code} )" : base.FromInterop( native, code );
	}
}

[TypeName( "qpoint" )]
public class ArgQPoint : ArgQPointF
{

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"QPoint( {code}.x, {code}.y )" : base.FromInterop( native, code );
	}
}

[TypeName( "qsize" )]
public class ArgQSize : ArgQPointF
{
	public override string ReturnWrapCall( string call, bool native )
	{
		if ( native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.width(), __r.height(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, native );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"QSize( {code}.x, {code}.y )" : base.FromInterop( native, code );
	}
}

[TypeName( "qsizef" )]
public class ArgQSizeF : ArgQSize
{
	public override string ReturnWrapCall( string call, bool native )
	{
		if ( native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.width(), __r.height(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, native );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"QSizeF( {code}.x, {code}.y )" : base.FromInterop( native, code );
	}
}

[TypeName( "qcolor" )]
public class ArgQColor : Arg
{
	public override string ManagedType => "Color32";
	public override string NativeType => "Color";

	public override string ReturnWrapCall( string call, bool native )
	{
		if ( native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.x(), __r.y(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, native );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return native ? $"QColor::fromRgb( {code}.r(), {code}.g(), {code}.b(), {code}.a() )" : base.FromInterop( native, code );
	}
}

[TypeName( "qrectf" )]
[TypeName( "qrect" )]
public class ArgQRectf : Arg
{
	public override string ManagedType => "QRectF";
	public override string NativeType => "QRectF";

	public override string ReturnWrapCall( string call, bool native )
	{
		if ( native )
		{
			//	var str = $"auto __r = {call};\n";
			//	str += $"\t\treturn __r;";

			//	return str;
		}

		return base.ReturnWrapCall( call, native );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		if ( native )
		{
			//return $"QRectF( {code}.x, {code}.y, {code}.width, {code}.height )";
		}

		return base.FromInterop( native, code );
	}
}

[TypeName( "qstring" )]
public class ArgQString : Arg
{
	public override string ManagedType => "string";
	public override string ManagedDelegateType => "IntPtr";
	public override string NativeType => "QString";
	public override string NativeDelegateType => "const QChar *";

	public override string ReturnWrapCall( string call, bool native )
	{
		return native ? $"return (const QChar*) SafeReturnWString( (const wchar_t *) {call} );" : base.ReturnWrapCall( call, native );
	}

	public override string ToInterop( bool native, string code = null )
	{
		code ??= Name;

		return !native ? $"(IntPtr)_str_{Name}" : native ? $"{code}.unicode()" : base.ToInterop( native, code );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return !native
			? $"{Definition.Current.StringTools}.GetWString( {code} )"
			: native ? $"QString( {code} )" : base.ToInterop( native, code );
	}

	public override string WrapFunctionCall( string functionCall, bool native )
	{
		if ( !native && HasFlag( "out" ) )
		{
			return $"IntPtr _outptr_{Name} = default;\n\n" +
				$"try\n" +
				$"{{\n" +
				$"	{functionCall}\n" +
				$"}}\n" +
				$"finally\n" +
				$"{{\n" +
				$"	{Name} = {Definition.Current.StringTools}.GetWString( _outptr_{Name} );\n" +
				$"}}\n";
		}
		else if ( !native )
		{
			return $"fixed( char* _str_{Name} = {Name} ) {{ {functionCall} }} ";
		}

		return base.WrapFunctionCall( functionCall, native );
	}
}

[TypeName( "qdir" )]
public class ArgQDir : Arg
{
	public override string ManagedType => "string";
	public override string ManagedDelegateType => "IntPtr";
	public override string NativeType => "QDir";
	public override string NativeDelegateType => "const QChar *";

	public override string ReturnWrapCall( string call, bool native )
	{
		return native ? $"return (const QChar*) SafeReturnWString( (const wchar_t *) {call} );" : base.ReturnWrapCall( call, native );
	}

	public override string ToInterop( bool native, string code = null )
	{
		code ??= Name;

		return !native ? $"_str_{Name}.Pointer" : native ? $"{code}.absolutePath().unicode()" : base.ToInterop( native, code );
	}

	public override string FromInterop( bool native, string code = null )
	{
		code ??= Name;

		return !native
			? $"{Definition.Current.StringTools}.GetWString( {code} )"
			: native ? $"QDir( {code} )" : base.ToInterop( native, code );
	}

	public override string WrapFunctionCall( string functionCall, bool native )
	{
		if ( !native && HasFlag( "out" ) )
		{
			return $"IntPtr _outptr_{Name} = default;\n\n" +
				$"try\n" +
				$"{{\n" +
				$"	{functionCall}\n" +
				$"}}\n" +
				$"finally\n" +
				$"{{\n" +
				$"	{Name} = {Definition.Current.StringTools}.GetWString( _outptr_{Name} );\n" +
				$"}}\n";
		}
		else if ( !native )
		{
			return $"var _str_{Name} = new {Definition.Current.StringTools}.InteropWString( {Name} ); try {{ {functionCall} }} finally {{ _str_{Name}.Free(); }} ";
		}

		return base.WrapFunctionCall( functionCall, native );
	}
}
