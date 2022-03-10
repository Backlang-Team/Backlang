﻿using Backlang.Codeanalysis.Core;
using Backlang.Codeanalysis.Core.Attributes;
using Backlang.Codeanalysis.Parsing.AST;
using Backlang.Codeanalysis.Parsing.AST.Expressions;
using System.Reflection;

namespace Backlang.Codeanalysis.Parsing;

public class Expression : SyntaxNode
{
    public static List<OperatorInfo> Operators = new List<OperatorInfo>();

    static Expression()
    {
        var typeValues = (TokenType[])Enum.GetValues(typeof(TokenType));

        foreach (var op in typeValues)
        {
            var attributes = op.GetType().GetField(Enum.GetName<TokenType>(op)).GetCustomAttributes<OperatorInfoAttribute>(true);

            if (attributes != null && attributes.Any())
            {
                foreach (var attribute in attributes)
                {
                    Operators.Add(new OperatorInfo(op, attribute.Precedence, attribute.IsUnary, attribute.IsPostUnary));
                }
            }
        }
    }

    public static Expression Parse<TNode, TLexer, TParser>(BaseParser<TNode, TLexer, TParser> parser, int parentPrecedence = 0)
        where TParser : BaseParser<TNode, TLexer, TParser>
        where TLexer : BaseLexer, new()
    {
        Expression left;
        var unaryOperatorPrecedence = GetUnaryOperatorPrecedence(parser.Iterator.Current.Type);

        if (unaryOperatorPrecedence != 0 && unaryOperatorPrecedence >= parentPrecedence)
        {
            Token? operatorToken = parser.Iterator.NextToken();
            Expression? operand = Parse(parser, unaryOperatorPrecedence + 1);

            left = new UnaryExpression(operatorToken, operand, false);
        }
        else
        {
            left = parser.ParsePrimary();

            if (IsPostUnary(parser.Iterator.Current.Type))
            {
                Token? operatorToken = parser.Iterator.NextToken();

                left = new UnaryExpression(operatorToken, left, true);
            }
        }

        while (true)
        {
            var precedence = GetBinaryOperatorPrecedence(parser.Iterator.Current.Type);
            if (precedence == 0 || precedence <= parentPrecedence)
                break;

            var operatorToken = parser.Iterator.NextToken();
            var right = Parse(parser, precedence);

            left = new BinaryExpression(left, operatorToken, right);
        }

        return left;
    }

    public override T Accept<T>(IVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }

    private static int GetBinaryOperatorPrecedence(TokenType kind)
    {
        return Operators.FirstOrDefault(_ => _.Token == kind && !_.IsUnary).Precedence;
    }

    private static int GetUnaryOperatorPrecedence(TokenType kind)
    {
        return Operators.FirstOrDefault(_ => _.Token == kind && _.IsUnary).Precedence;
    }

    private static bool IsPostUnary(TokenType kind)
    {
        return Operators.FirstOrDefault(_ => _.Token == kind && _.IsUnary).IsPostUnary;
    }
}