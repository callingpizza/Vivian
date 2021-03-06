﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Vivian.CodeAnalysis.Binding;
using Vivian.CodeAnalysis.Syntax;
using Vivian.CodeAnalysis.Symbols;

namespace Vivian.CodeAnalysis.Lowering
{
    internal sealed class Lowerer : BoundTreeRewriter
    {
        private int _labelCount;
        
        private Lowerer()
        {
        }

        private BoundLabel GenerateLabel()
        {
            var name = $"Label{++_labelCount}";
            return new BoundLabel(name);
        }

        public static BoundBlockStatement Lower(FunctionSymbol function, BoundStatement statement)
        {
            var lowerer = new Lowerer();
            var result = lowerer.RewriteStatement(statement);
            return RemoveDeadCode(Flatten(function, result));
        }
        
        private static BoundBlockStatement Flatten(FunctionSymbol function, BoundStatement statement)
        {
            var builder = ImmutableArray.CreateBuilder<BoundStatement>();
            var stack = new Stack<BoundStatement>();
            stack.Push(statement);

            while (stack.Count > 0)
            {
                var current = stack.Pop();

                if (current is BoundBlockStatement block)
                {
                    foreach (var s in block.Statements.Reverse())
                        stack.Push(s);
                }
                else
                {
                    builder.Add(current);
                }
            }

            if (function.Type == TypeSymbol.Void)
            {
                if (builder.Count == 0 || CanFallThrough(builder.Last()))
                {
                    builder.Add(new BoundReturnStatement(null));
                }
            }

            return new BoundBlockStatement(builder.ToImmutable());
        }

        private static bool CanFallThrough(BoundStatement boundStatement)
        {
            return boundStatement.Kind != BoundNodeKind.ReturnStatement &&
                   boundStatement.Kind != BoundNodeKind.GotoStatement;
        }
        
        private static BoundBlockStatement RemoveDeadCode(BoundBlockStatement node)
        {
            var controlFlow = ControlFlowGraph.Create(node);
            var reachableStatements = new HashSet<BoundStatement>(controlFlow.Blocks.SelectMany(b => b.Statements));

            var builder = node.Statements.ToBuilder();

            for (var i = builder.Count - 1; i >= 0; i--)
            {
                if (!reachableStatements.Contains(builder[i]))
                {
                    builder.RemoveAt(i);
                }
            }

            return new BoundBlockStatement(builder.ToImmutable());
        }

        protected override BoundStatement RewriteIfStatement(BoundIfStatement node)
        {
            if (node.ElseStatement == null)
            {
                // if <condition>
                //      <then>
                //
                // ---->
                //
                // gotoFalse <condition> end
                // <then> 
                // end:
                //
                var endLabel = GenerateLabel();
                var gotoFalse = new BoundConditionalGotoStatement(endLabel, node.Condition, false);
                var endLabelStatement = new BoundLabelStatement(endLabel);
                var result = new BoundBlockStatement(ImmutableArray.Create<BoundStatement>(gotoFalse, node.ThenStatement, endLabelStatement));
                return RewriteStatement(result);
            }

            else
            {
                // if <condition>
                //      <then>
                // else 
                //      <else>
                // 
                // ---->
                //
                // gotoFalse <condition> else 
                // <then> 
                // goto end
                // else:
                // <else>
                // end:

                var elseLabel = GenerateLabel();
                var endLabel = GenerateLabel();
                
                var gotoFalse = new BoundConditionalGotoStatement(elseLabel, node.Condition, false);
                var gotoEndStatement = new BoundGotoStatement(endLabel);
                var elseLabelStatement = new BoundLabelStatement(elseLabel);
                var endLabelStatement = new BoundLabelStatement(endLabel);

                var result = new BoundBlockStatement(ImmutableArray.Create<BoundStatement>(
                    gotoFalse, 
                    node.ThenStatement, 
                    gotoEndStatement,
                    elseLabelStatement,
                    node.ElseStatement,
                    endLabelStatement
                ));
                
                return RewriteStatement(result);
            }
        }

        protected override BoundStatement RewriteWhileStatement(BoundWhileStatement node)
        {
            // while <condition> 
            //      <body>
            //
            // ---->
            //  
            //  continue:
            //  <body>
            // check:
            // gotoTrue <condition> continue 
            // break:
            
            var checkLabel = GenerateLabel();
            
            var gotoCheck = new BoundGotoStatement(checkLabel);
            var continueLabelStatement = new BoundLabelStatement(node.ContinueLabel);
            var checkLabelStatement = new BoundLabelStatement(checkLabel);
            var gotoTrue = new BoundConditionalGotoStatement(node.ContinueLabel, node.Condition);
            var breakLabelStatement = new BoundLabelStatement(node.BreakLabel);
            
            var result = new BoundBlockStatement(ImmutableArray.Create<BoundStatement>(
                gotoCheck, 
                continueLabelStatement, 
                node.Body,
                checkLabelStatement,
                gotoTrue,
                breakLabelStatement
            ));
                
            return RewriteStatement(result);
        }

        protected override BoundStatement RewriteDoWhileStatement(BoundDoWhileStatement node)
        {
            // do
            //      <body>
            // while <condition>
            //
            // ----->
            //
            // continue:
            // <body>
            // gotoTrue <condition> continue>
            // 


            var continueLabelStatement = new BoundLabelStatement(node.ContinueLabel);
            var gotoTrue = new BoundConditionalGotoStatement(node.ContinueLabel, node.Condition);
            var breakLabelStatement = new BoundLabelStatement(node.BreakLabel);

            var result = new BoundBlockStatement(ImmutableArray.Create<BoundStatement>(
                continueLabelStatement,
                node.Body,
                gotoTrue,
                breakLabelStatement
                ));
            return RewriteStatement(result);
        }
        
        protected override BoundStatement RewriteForStatement(BoundForStatement node)
        {
            // for <var> = <lower> to <upper> 
            //       <body>
            // 
            // ---->
            // 
            // {
            //     var <var> = <lower>
            //     while (<var> <= <upper>)
            //     {
            //          <body>
            //          <var> = <var> + 1
            //     }
            // }

            var variableDeclaration = new BoundVariableDeclaration(node.Variable, node.LowerBound);
            var variableExpression = new BoundVariableExpression(node.Variable);
            var upperBoundSymbol = new LocalVariableSymbol("upperBound", true, TypeSymbol.Int, node.UpperBound.ConstantValue);
            var upperBoundDeclaration = new BoundVariableDeclaration(upperBoundSymbol, node.UpperBound);
            
            var condition = new BoundBinaryExpression(
                variableExpression, 
                BoundBinaryOperator.Bind(SyntaxKind.LessOrEqualsToken, TypeSymbol.Int, TypeSymbol.Int), 
                new BoundVariableExpression(upperBoundSymbol)
            );

            var continueLabelStatement = new BoundLabelStatement(node.ContinueLabel);
            var increment = new BoundExpressionStatement(
                new BoundAssignmentExpression(
                    node.Variable, 
                    new BoundBinaryExpression(
                        variableExpression, 
                        BoundBinaryOperator.Bind(SyntaxKind.PlusToken, TypeSymbol.Int, TypeSymbol.Int), 
                        new BoundLiteralExpression(1)
                    )
                )
            );

            var whileBody = new BoundBlockStatement(ImmutableArray.Create<BoundStatement>(
                node.Body, 
                continueLabelStatement, 
                increment
                ));
            
            var whileStatement = new BoundWhileStatement(condition, whileBody, node.BreakLabel, GenerateLabel());
            var result = new BoundBlockStatement(ImmutableArray.Create<BoundStatement>(variableDeclaration, upperBoundDeclaration, whileStatement));

            return RewriteStatement(result);
        }
        
        protected override BoundStatement RewriteConditionalGotoStatement(BoundConditionalGotoStatement node)
        {
            if (node.Condition.ConstantValue != null)
            {
                var condition = (bool) node.Condition.ConstantValue.Value;
                condition = node.JumpIfTrue ? condition : !condition;
                if (condition)
                {
                    return RewriteStatement(new BoundGotoStatement(node.Label));
                }
                else
                {
                    return RewriteStatement(new BoundNopStatement());
                }
            }

            return base.RewriteConditionalGotoStatement(node);
        }
    }
}