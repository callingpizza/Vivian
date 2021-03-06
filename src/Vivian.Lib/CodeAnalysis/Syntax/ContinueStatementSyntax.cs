﻿namespace Vivian.CodeAnalysis.Syntax
{
    public sealed partial class ContinueStatementSyntax : StatementSyntax
    {
        public ContinueStatementSyntax(SyntaxTree syntaxTree, SyntaxToken keyword, SyntaxToken semicolonToken) : base(syntaxTree)
        {
            Keyword = keyword;
            SemicolonToken = semicolonToken;
        }

        public override SyntaxKind Kind => SyntaxKind.ContinueStatement;
        public SyntaxToken Keyword { get; }
        public SyntaxToken SemicolonToken { get; }
    }
}