﻿using Microsoft.CodeAnalysis.Semantics;
using Pchp.Syntax;
using Roslyn.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AST = Pchp.Syntax.AST;

namespace Pchp.CodeAnalysis.Semantics
{
    /// <summary>
    /// Binds syntax nodes (<see cref="AST.LangElement"/>) to semantic nodes (<see cref="IOperation"/>).
    /// </summary>
    internal class SemanticsBinder
    {
        #region Construction

        public SemanticsBinder(/*PhpCompilation compilation, AST.GlobalCode ast, bool ignoreAccessibility*/)
        {
        }

        #endregion

        #region Helpers

        public IEnumerable<BoundStatement> BindStatements(IEnumerable<AST.Statement> statements)
        {
            return statements.Select(BindStatement);
        }

        public ImmutableArray<BoundExpression> BindExpressions(IEnumerable<AST.Expression> expressions)
        {
            return expressions.Select(BindExpression).ToImmutableArray();
        }

        BoundExpression BindExpression(AST.Expression expr) => BindExpression(expr, AccessType.Read);

        ImmutableArray<BoundArgument> BindArguments(IEnumerable<AST.Expression> expressions)
        {
            return BindExpressions(expressions)
                .Select(x => new BoundArgument(x))
                .ToImmutableArray();
        }

        #endregion

        public BoundStatement BindStatement(AST.Statement stmt)
        {
            Debug.Assert(stmt != null);

            if (stmt is AST.EchoStmt) return new BoundExpressionStatement(new BoundEcho(BindArguments(((AST.EchoStmt)stmt).Parameters)));
            if (stmt is AST.ExpressionStmt) return new BoundExpressionStatement(BindExpression(((AST.ExpressionStmt)stmt).Expression, AccessType.None));
            if (stmt is AST.JumpStmt) return BindJumpStmt((AST.JumpStmt)stmt);

            throw new NotImplementedException(stmt.GetType().FullName);
        }

        BoundStatement BindJumpStmt(AST.JumpStmt stmt)
        {
            if (stmt.Type == AST.JumpStmt.Types.Return)
            {
                return new BoundReturnStatement(
                    (stmt.Expression != null)
                        ? BindExpression(stmt.Expression, AccessType.Read)   // ReadRef in case routine returns an aliased value
                        : null);
            }

            throw ExceptionUtilities.Unreachable;
        }

        public BoundExpression BindExpression(AST.Expression expr, AccessType access = AccessType.Read)
        {
            Debug.Assert(expr != null);

            if (expr is AST.Literal) return BindLiteral((AST.Literal)expr).WithAccess(access);
            if (expr is AST.VarLikeConstructUse) return BindVarLikeConstructUse((AST.VarLikeConstructUse)expr, access);
            if (expr is AST.BinaryEx) return BindBinaryEx((AST.BinaryEx)expr).WithAccess(access);
            if (expr is AST.AssignEx) return BindAssignEx((AST.AssignEx)expr, access);
            if (expr is AST.UnaryEx) return BindUnaryEx((AST.UnaryEx)expr).WithAccess(access);
            if (expr is AST.GlobalConstUse) return BindGlobalConstUse((AST.GlobalConstUse)expr).WithAccess(access);

            throw new NotImplementedException(expr.GetType().FullName);
        }

        BoundExpression BindVarLikeConstructUse(AST.VarLikeConstructUse expr, AccessType access)
        {
            if (expr is AST.DirectVarUse) return BindDirectVarUse((AST.DirectVarUse)expr, access);

            throw new NotImplementedException(expr.GetType().FullName);
        }

        BoundExpression BindDirectVarUse(AST.DirectVarUse expr, AccessType access)
        {
            if (expr.IsMemberOf == null)
            {
                return new BoundVariableRef(expr.VarName.Value).WithAccess(access);
            }

            throw new NotImplementedException();
        }

        BoundExpression BindGlobalConstUse(AST.GlobalConstUse expr)
        {
            // translate built-in constants directly
            if (expr.Name == QualifiedName.True) return new BoundLiteral(true);
            if (expr.Name == QualifiedName.False) return new BoundLiteral(false);
            if (expr.Name == QualifiedName.Null) return new BoundLiteral(null);

            // bind constant
            throw new NotImplementedException();
        }

        BoundExpression BindBinaryEx(AST.BinaryEx expr)
        {
            return new BoundBinaryEx(
                BindExpression(expr.LeftExpr, AccessType.Read),
                BindExpression(expr.RightExpr, AccessType.Read),
                expr.Operation);
        }

        BoundExpression BindUnaryEx(AST.UnaryEx expr)
        {
            return new BoundUnaryEx(BindExpression(expr.Expr, AccessType.Read), expr.Operation);
        }

        BoundExpression BindAssignEx(AST.AssignEx expr, AccessType access)
        {
            var op = expr.Operation;
            var target = (BoundReferenceExpression)BindExpression(expr.LValue,
                (op == AST.Operations.AssignValue || op == AST.Operations.AssignRef) ? AccessType.Write : AccessType.ReadAndWrite);
            BoundExpression value;

            if (expr is AST.ValueAssignEx)
            {
                value = BindExpression(((AST.ValueAssignEx)expr).RValue, AccessType.Read);
            }
            else
            {
                Debug.Assert(expr is AST.RefAssignEx);
                Debug.Assert(op == AST.Operations.AssignRef);
                target.Access = AccessType.WriteRef;
                value = BindExpression(((AST.RefAssignEx)expr).RValue, AccessType.ReadRef);
            }

            //
            if (op == AST.Operations.AssignValue || op == AST.Operations.AssignRef)
                return new BoundAssignEx(target, value).WithAccess(access);
            else
                return new BoundCompoundAssignEx(target, value, op).WithAccess(access);
        }

        static BoundExpression BindLiteral(AST.Literal expr)
        {
            if (expr is AST.IntLiteral) return new BoundLiteral(((AST.IntLiteral)expr).Value);
            if (expr is AST.LongIntLiteral) return new BoundLiteral(((AST.LongIntLiteral)expr).Value);
            if (expr is AST.StringLiteral) return new BoundLiteral(((AST.StringLiteral)expr).Value);
            if (expr is AST.DoubleLiteral) return new BoundLiteral(((AST.DoubleLiteral)expr).Value);
            if (expr is AST.BoolLiteral) return new BoundLiteral(((AST.BoolLiteral)expr).Value);
            if (expr is AST.NullLiteral) return new BoundLiteral(null);
            if (expr is AST.BinaryStringLiteral) return new BoundLiteral(((AST.BinaryStringLiteral)expr).Value);

            throw new NotImplementedException();
        }
    }
}
