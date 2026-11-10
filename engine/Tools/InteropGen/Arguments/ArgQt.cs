namespace Facepunch.InteropGen;


[TypeName( "qreal" )]
public class ArgQReal : Arg
{
	public override string ManagedType => "float";
}

[TypeName( "qicon" )]
public class ArgQIcon : ArgString
{
	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"FindOrCreateQIcon( {code} )" : base.ToInterop( side, code );
	}
}

[TypeName( "qpointf" )]
public class ArgQPointF : Arg
{
	public override string ManagedType => "Vector3";
	public override string NativeType => "Vector";

	public override string ReturnWrapCall( string call, Side side )
	{
		if ( side == Side.Native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.x(), __r.y(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, side );
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"QPointF( {code}.x, {code}.y )" : base.FromInterop( side, code );
	}
}

[TypeName( "qbytearray" )]
public class ArgQByteArray : ArgString
{
	public override string ReturnWrapCall( string call, Side side )
	{
		return side == Side.Native ? $"\t\treturn SafeReturnString( ({call}).toBase64() );" : base.ReturnWrapCall( call, side );
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"QByteArray::fromBase64( {code} )" : base.FromInterop( side, code );
	}
}

[TypeName( "qpoint" )]
public class ArgQPoint : ArgQPointF
{

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"QPoint( {code}.x, {code}.y )" : base.FromInterop( side, code );
	}
}

[TypeName( "qsize" )]
public class ArgQSize : ArgQPointF
{
	public override string ReturnWrapCall( string call, Side side )
	{
		if ( side == Side.Native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.width(), __r.height(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, side );
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"QSize( {code}.x, {code}.y )" : base.FromInterop( side, code );
	}
}

[TypeName( "qsizef" )]
public class ArgQSizeF : ArgQSize
{
	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"QSizeF( {code}.x, {code}.y )" : base.FromInterop( side, code );
	}
}

[TypeName( "qcolor" )]
public class ArgQColor : Arg
{
	public override string ManagedType => "Color32";
	public override string NativeType => "Color";

	public override string ReturnWrapCall( string call, Side side )
	{
		if ( side == Side.Native )
		{
			string str = $"auto __r = {call};\n";
			str += $"\t\treturn Vector( __r.x(), __r.y(), 0 );";

			return str;
		}

		return base.ReturnWrapCall( call, side );
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Native ? $"QColor::fromRgb( {code}.r(), {code}.g(), {code}.b(), {code}.a() )" : base.FromInterop( side, code );
	}
}

[TypeName( "qrectf" )]
[TypeName( "qrect" )]
public class ArgQRectf : Arg
{
	public override string ManagedType => "QRectF";
	public override string NativeType => "QRectF";
}

[TypeName( "qstring" )]
public class ArgQString : Arg
{
	public override string ManagedType => "string";
	public override string ManagedDelegateType => "IntPtr";
	public override string NativeType => "QString";
	public override string NativeDelegateType => "const QChar *";
	public override bool WrapsManagedCall => true;

	public override string ReturnWrapCall( string call, Side side )
	{
		return side == Side.Native ? $"return (const QChar*) SafeReturnWString( (const wchar_t *) {call} );" : base.ReturnWrapCall( call, side );
	}

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"(IntPtr)_str_{Name}" : $"{code}.unicode()";
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"{StringTools}.GetWString( {code} )" : $"QString( {code} )";
	}

	public override string WrapFunctionCall( string functionCall, Side side )
	{
		if ( side == Side.Managed && HasFlag( "out" ) )
		{
			return $"IntPtr _outptr_{Name} = default;\n\n" +
				$"try\n" +
				$"{{\n" +
				$"	{functionCall}\n" +
				$"}}\n" +
				$"finally\n" +
				$"{{\n" +
				$"	{Name} = {StringTools}.GetWString( _outptr_{Name} );\n" +
				$"}}\n";
		}
		else if ( side == Side.Managed )
		{
			return $"fixed( char* _str_{Name} = {Name} ) {{ {functionCall} }} ";
		}

		return base.WrapFunctionCall( functionCall, side );
	}
}

[TypeName( "qdir" )]
public class ArgQDir : Arg
{
	public override string ManagedType => "string";
	public override string ManagedDelegateType => "IntPtr";
	public override string NativeType => "QDir";
	public override string NativeDelegateType => "const QChar *";
	public override bool WrapsManagedCall => true;

	public override string ReturnWrapCall( string call, Side side )
	{
		return side == Side.Native ? $"return (const QChar*) SafeReturnWString( (const wchar_t *) {call} );" : base.ReturnWrapCall( call, side );
	}

	public override string ToInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"_str_{Name}.Pointer" : $"{code}.absolutePath().unicode()";
	}

	public override string FromInterop( Side side, string code = null )
	{
		code ??= Name;

		return side == Side.Managed ? $"{StringTools}.GetWString( {code} )" : $"QDir( {code} )";
	}

	public override string WrapFunctionCall( string functionCall, Side side )
	{
		if ( side == Side.Managed && HasFlag( "out" ) )
		{
			return $"IntPtr _outptr_{Name} = default;\n\n" +
				$"try\n" +
				$"{{\n" +
				$"	{functionCall}\n" +
				$"}}\n" +
				$"finally\n" +
				$"{{\n" +
				$"	{Name} = {StringTools}.GetWString( _outptr_{Name} );\n" +
				$"}}\n";
		}
		else if ( side == Side.Managed )
		{
			return $"var _str_{Name} = new {StringTools}.InteropWString( {Name} ); try {{ {functionCall} }} finally {{ _str_{Name}.Free(); }} ";
		}

		return base.WrapFunctionCall( functionCall, side );
	}
}
