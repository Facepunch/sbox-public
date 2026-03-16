using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Sandbox.Generator
{
	internal sealed class Worker : CSharpSyntaxRewriter
	{
		/// <summary>
		/// Create a new thread pool task to process this syntax tree
		/// </summary>
		internal static Worker Process( CSharpCompilation compilation, SyntaxTree tree, Dictionary<string, string> map, bool isInGame, Sync sync, Processor processor )
		{
			var m = new Worker( compilation, tree, map, isInGame, sync, processor );
			m.Run();
			return m;
		}

		/// <summary>
		/// Don't access this willy nilly! It's not thread safe!
		/// </summary>
		public Processor Processor { get; set; }

		/// <summary>
		/// The compilation model
		/// </summary>
		internal CSharpCompilation Compilation { get; }

		/// <summary>
		/// The original Syntax Tree
		/// </summary>
		public SyntaxTree TreeInput { get; private set; }

		/// <summary>
		/// True if we're doing an engine build, so should be doing more than adding syntax trees.
		/// If it's false then we're probably intellisense or compiling externally
		/// </summary>
		public bool IsFullGeneration { get; private set; }

		/// <summary>
		/// True when corelib polyfills should run. Requires a full generation pass and the processor flag.
		/// </summary>
		public bool CorelibPolyfillsEnabled => IsFullGeneration && Processor?.EnableCorelibPolyfills == true;

		/// <summary>
		/// Any syntax trees we added
		/// </summary>
		public List<SyntaxTree> AddedTrees { get; private set; } = new List<SyntaxTree>();

		/// <summary>
		/// Any loose code we want to add, but don't care where it ends up
		/// </summary>
		public string AddedCode { get; set; } = "";

		/// <summary>
		/// Semantic version of the original SyntaxTree
		/// </summary>
		internal SemanticModel Model;

		/// <summary>
		/// Allows workers to sync
		/// </summary>
		internal Sync Sync { get; private set; }

		/// <summary>
		/// The processed syntax tree
		/// </summary>
		public CSharpSyntaxNode OutputNode { get; private set; }

		public Dictionary<string, string> AddonFileMap { get; }

		internal Worker( CSharpCompilation compilation, SyntaxTree tree, Dictionary<string, string> map, bool isInGame, Sync sync, Processor processor )
		{
			Processor = processor;
			Sync = sync;
			IsFullGeneration = isInGame;
			Compilation = compilation;
			TreeInput = tree;
			Model = Compilation.GetSemanticModel( tree );
			AddonFileMap = map;
		}

		/// <summary>
		/// Keep track of what classes we visited this run, so we don't end up putting duplicate DescriptionAttributes on partial classes
		/// </summary>
		internal static List<string> VisitedClasses = new List<string>();

		/// <summary>
		/// Runs in the thread pool, processes the syntax tree and returns
		/// </summary>
		internal void Run()
		{
			var node = TreeInput.GetRoot() as CSharpSyntaxNode;
			OutputNode = Visit( node ) as CSharpSyntaxNode;
		}

		/// <summary>
		/// Members to be added to the "current" class. Additions are added in a separate file, so
		/// that intellisense will pick them up (they work with Source Generator)
		/// </summary>
		internal List<string> ClassAdditions = new List<string>();

		/// <summary>
		/// Members to be added to the current class, but not necessarily in an external file. Things that
		/// don't need intellisensing use this, like backing fields.
		/// </summary>
		List<string> ClassModifiers = new List<string>();

		/// <summary>
		/// Attributes to be added to the current class.
		/// </summary>
		List<string> ClassBaseTypes = new List<string>();

		public void AddToCurrentClass( string text, bool useSourceGen )
		{
			if ( useSourceGen ) ClassAdditions.Add( text );
			else ClassModifiers.Add( text );
		}

		public void AddBaseTypeToCurrentClass( string text )
		{
			ClassBaseTypes.Add( text );
		}

		//
		// Classblocks are things that aren't self contained, but have to exist in other blocks
		// For example, when an RPC comes in we need to check which RPC it is. This is one of them.
		//

		internal List<ClassBlock> ClassBlocks = new List<ClassBlock>();

		internal struct ClassBlock
		{
			public string Group;
			public string Text;
			public ITypeSymbol TypeSymbol;
		}

		internal void AddClassBlock( string group, string text, ITypeSymbol clss )
		{
			var b = new ClassBlock
			{
				Group = group,
				Text = text,
				TypeSymbol = clss
			};

			ClassBlocks.Add( b );
		}

		public override SyntaxNode VisitMethodDeclaration( MethodDeclarationSyntax _node )
		{
			var symbol = Model.GetDeclaredSymbol( _node );

			ComponentSubscriberInterfaces.VisitMethod( _node, symbol, this );

			var node = base.VisitMethodDeclaration( _node ) as MethodDeclarationSyntax;

			Description.VisitMethod( ref node, symbol, this );
			CodeGen.VisitMethod( ref node, symbol, this );

			node = LinePreserve.AddLineNumber( node, _node, TreeInput, this );
			node = ClassFileLocation.VisitNode( node, _node, symbol, this, TreeInput ) as MethodDeclarationSyntax;

			return node;
		}

		bool IsGeneratedRazorFile()
		{
			return TreeInput.FilePath.StartsWith( "_gen_" ) && TreeInput.FilePath.Contains( ".razor_" );
		}

		public override SyntaxNode VisitExpressionStatement( ExpressionStatementSyntax _node )
		{
			var node = base.VisitExpressionStatement( _node ) as ExpressionStatementSyntax;

			// Razor already does this
			if ( IsGeneratedRazorFile() ) return node;

			node = LinePreserve.AddLineNumber( node, _node, TreeInput, this );

			return node;
		}

		public override SyntaxNode VisitAnonymousMethodExpression( AnonymousMethodExpressionSyntax _node )
		{
			var node = base.VisitAnonymousMethodExpression( _node ) as AnonymousMethodExpressionSyntax;

			node = LinePreserve.AddLineNumber( node, _node, TreeInput, this );

			return node;
		}

		public override SyntaxNode VisitInvocationExpression( InvocationExpressionSyntax node )
		{
			var location = node.GetLocation();
			var symbolInfo = Model.GetSymbolInfo( node.Expression );
			node = base.VisitInvocationExpression( node ) as InvocationExpressionSyntax;

			var symlist = symbolInfo.CandidateSymbols;
			if ( symbolInfo.Symbol is not null ) symlist = ImmutableArray.Create( symbolInfo.Symbol );

			CloudAssetProvider.VisitInvocation( ref node, location, symlist, this );
			StringTokenUpgrader.VisitInvocation( ref node, location, symlist, this );

			return node;
		}

		public override SyntaxNode VisitMemberAccessExpression( MemberAccessExpressionSyntax node )
		{
			var visited = base.VisitMemberAccessExpression( node ) as ExpressionSyntax;
			if ( visited is null )
			{
				return visited;
			}

			var rewritten = ArrayPoolSharedRedirect.VisitMemberAccess( node, visited, this );
			return rewritten ?? visited;
		}

		public override SyntaxNode VisitBlock( BlockSyntax node )
		{
			node = base.VisitBlock( node ) as BlockSyntax;

			if ( IsGeneratedRazorFile() ) return node;

			bool changes = false;
			var statements = node.Statements;

			// only add these blocks when actually generating the code
			if ( false && IsFullGeneration )
			{
				for ( int i = 0; i < statements.Count; i++ )
				{
					//
					// Put EnsureSufficientExecutionStack before any method call
					//
					if ( statements[i] is ExpressionStatementSyntax exprStatement && exprStatement.Expression is InvocationExpressionSyntax )
					{
						changes = true;
						statements = statements.Insert( i, SyntaxFactory.ParseStatement( "global::System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();\r\n" ) );
						i++;
					}
				}
			}

			if ( changes )
				return node.WithStatements( statements );

			return node;
		}

		public override SyntaxNode VisitFieldDeclaration( FieldDeclarationSyntax _node )
		{
			var symbol = Model.GetDeclaredSymbol( _node );
			var node = base.VisitFieldDeclaration( _node ) as FieldDeclarationSyntax;

			node = ClassFileLocation.VisitNode( node, _node, symbol, this, TreeInput ) as FieldDeclarationSyntax;

			return node;
		}

		public override SyntaxNode VisitEnumMemberDeclaration( EnumMemberDeclarationSyntax _node )
		{
			var symbol = Model.GetDeclaredSymbol( _node );
			var node = base.VisitEnumMemberDeclaration( _node ) as EnumMemberDeclarationSyntax;

			Description.VisitEnumMember( ref node, symbol, this );

			return node;
		}

		public override SyntaxNode VisitPropertyDeclaration( PropertyDeclarationSyntax _node )
		{
			var symbol = Model.GetDeclaredSymbol( _node );
			var node = base.VisitPropertyDeclaration( _node ) as PropertyDeclarationSyntax;

			DefaultValue.VisitProperty( ref node, symbol, this );
			Description.VisitProperty( ref node, symbol, this );
			CodeGen.VisitProperty( ref node, symbol, this );

			node = LinePreserve.AddLineNumber( node, _node, TreeInput, this );
			node = ClassFileLocation.VisitNode( node, _node, symbol, this, TreeInput ) as PropertyDeclarationSyntax;

			return node;
		}

		public override SyntaxNode VisitClassDeclaration( ClassDeclarationSyntax _node )
		{
			var symbol = Model.GetDeclaredSymbol( _node ) as INamedTypeSymbol;

			var oldClassAdditions = ClassAdditions;
			var oldClassModifiers = ClassModifiers;
			var oldClassAttributes = ClassBaseTypes;

			ClassAdditions = new List<string>();
			ClassModifiers = new List<string>();
			ClassBaseTypes = new List<string>();

			var node = _node;

			try
			{
				node = base.VisitClassDeclaration( _node ) as ClassDeclarationSyntax;

				Description.VisitClass( ref node, symbol, this );
				node = ClassFileLocation.VisitNode( node, _node, symbol, this, TreeInput ) as ClassDeclarationSyntax;

				//
				// Create new Syntax Trees for the additions
				//
				if ( ClassAdditions.Count > 0 )
				{
					if ( !node.Modifiers.Any( m => m.IsKind( SyntaxKind.PartialKeyword ) ) )
					{
						AddError( node.GetLocation(), $"Please declare class '{symbol.Name}' as a partial so we can add codegen to it" );
						return node;
					}

					var filename = $"{System.IO.Path.GetFileNameWithoutExtension( TreeInput.FilePath )}_{node.Identifier}.cs";

					//
					// The same file can have several of the same class, make sure we give it a unique filename for each generated class
					// Otherwise the generator is gonna absolutely shit itself
					//
					if ( AddedTrees.Any( t => t.FilePath == filename ) )
					{
						filename = $"{System.IO.Path.GetFileNameWithoutExtension( TreeInput.FilePath )}_{node.Identifier}_{node.SpanStart}.cs";
					}

					var code = new CodeWriter();

					AddNamespaces( code, symbol );

					code.WriteLine( "" );
					code.StartClass( symbol );

					foreach ( var add in ClassAdditions )
					{
						code.WriteLines( add );
					}

					code.EndClass( symbol );

					var st = SyntaxFactory.ParseSyntaxTree( code.ToString(), TreeInput.Options, filename, TreeInput.Encoding );
					AddedTrees.Add( st );
				}
			}
			catch ( System.Exception e )
			{
				Console.WriteLine( e );
			}
			finally
			{

				//
				// Add to this class node
				//
				if ( ClassModifiers.Count > 0 )
				{
					var members = ClassModifiers.Select( x => SyntaxFactory.ParseMemberDeclaration( x ) ).ToArray();
					node = node.AddMembers( members );
				}

				if ( ClassBaseTypes.Count > 0 )
				{
					var baseTypes = ClassBaseTypes.Select( x => SyntaxFactory.SimpleBaseType( SyntaxFactory.ParseTypeName( x ) ) ).ToArray();

					// Console.WriteLine( $"{symbol.Name} - {string.Join( ";", baseTypes.Select( x => x.ToString() ) )}" );

					node = node.AddBaseListTypes( baseTypes );
				}


				ClassAdditions = oldClassAdditions;
				ClassModifiers = oldClassModifiers;
				ClassBaseTypes = oldClassAttributes;
			}

			node = LinePreserve.AddLineNumber( node, _node, TreeInput, this );

			return node;
		}

		private void AddNamespaces( CodeWriter code, INamedTypeSymbol symbol )
		{
			//
			// Collect usings
			//
			SyntaxList<UsingDirectiveSyntax> allUsings = SyntaxFactory.List<UsingDirectiveSyntax>();
			foreach ( var syntaxRef in symbol.DeclaringSyntaxReferences )
			{
				foreach ( var parent in syntaxRef.GetSyntax().Ancestors( false ) )
				{
					if ( parent is NamespaceDeclarationSyntax nsParent )
						allUsings = allUsings.AddRange( nsParent.Usings );
					else if ( parent is CompilationUnitSyntax cuParent )
						allUsings = allUsings.AddRange( cuParent.Usings );
				}
			}

			// shitty DistinctBy - no .net6
			SyntaxList<UsingDirectiveSyntax> distinctUsings = SyntaxFactory.List<UsingDirectiveSyntax>();
			foreach ( var u in allUsings )
			{
				// skip global using, since they're already added
				if ( !u.GlobalKeyword.IsKind( SyntaxKind.None ) )
					continue;

				if ( distinctUsings.Any( x => x.Name.ToString() == u.Name.ToString() ) )
					continue;

				distinctUsings = distinctUsings.Add( u );
			}

			//
			// Add these if we don't already have them
			//
			if ( !distinctUsings.Any( x => x.Name.ToString() == "Sandbox" ) )
				code.WriteLine( "using Sandbox;" );

			if ( !distinctUsings.Any( x => x.Name.ToString() == "System.Collections.Generic" ) )
				code.WriteLine( "using System.Collections.Generic;" );

			//
			// Replicate the same usings - means we don't have to massively overcomplicate initializer fields
			//
			code.WriteLines( distinctUsings.ToFullString() );

		}

		public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

		internal void AddError( Location location, DiagnosticDescriptor diagnostic, params object[] messageArgs )
		{
			Diagnostics.Add( Diagnostic.Create( diagnostic, location, messageArgs ) );
		}

		internal void AddError( Location location, string error )
		{
			AddError( location, new DiagnosticDescriptor( "SB2000", "Net Not Supported", error, "generator", DiagnosticSeverity.Error, true ) );
		}

		internal void Log( string v, Location location = null )
		{
			v = v.Replace( "\n", "" );
			v = v.Replace( "\r", "" );

			var d = new DiagnosticDescriptor( "SB0002", "Net Not Supported", v, "generator", DiagnosticSeverity.Warning, true );

			Diagnostics.Add( Diagnostic.Create( d, location ) );
		}

		internal Dictionary<string, INamedTypeSymbol> Types = new Dictionary<string, INamedTypeSymbol>();

		public INamedTypeSymbol GetOrCreateTypeByMetadataName( string name )
		{
			if ( Types.TryGetValue( name, out INamedTypeSymbol type ) )
				return type;

			type = Compilation.GetTypeByMetadataName( name );
			Types[name] = type;

			return type;
		}


	}
}
