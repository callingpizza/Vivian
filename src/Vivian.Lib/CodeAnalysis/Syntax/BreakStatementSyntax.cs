﻿namespace Vivian.CodeAnalysis.Syntax
{
    public sealed partial class BreakStatementSyntax : StatementSyntax
    {
        public BreakStatementSyntax(SyntaxTree syntaxTree, SyntaxToken keyword, SyntaxToken semicolonToken) : base(syntaxTree)
        {
            Keyword = keyword;
            SemicolonToken = semicolonToken;
        }

        public override SyntaxKind Kind => SyntaxKind.BreakStatement;
        public SyntaxToken Keyword { get; }
        public SyntaxToken SemicolonToken { get; }
    }
}