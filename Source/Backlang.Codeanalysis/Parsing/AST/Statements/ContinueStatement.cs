﻿using Loyc.Syntax;

namespace Backlang.Codeanalysis.Parsing.AST.Statements;
public sealed class ContinueStatement : IParsePoint<LNode>
{

    public static LNode Parse(TokenIterator iterator, Parser parser)
    {
        iterator.Match(TokenType.Semicolon);

        return LNode.Call(CodeSymbols.Continue);
    }

}
