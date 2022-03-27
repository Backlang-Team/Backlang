﻿using Backlang.Codeanalysis.Parsing.AST.Expressions.Match;
using Backlang.Codeanalysis.Parsing.AST.Statements;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1;

[TestClass]
public class MatchTests : ParserTestBase
{
    [TestMethod]
    public void All_Rules_Should_Pass()
    {
        var src = "match input with 12 => 1, i32 => 32, i32 num => num + 2, _ => 3, > 12 => 15;";
        var tree = ParseAndGetNodeInFunction<ExpressionStatement>(src);
        var matchExpression = (MatchExpression)tree.Expression;

        Assert.AreEqual(matchExpression.Rules.Count, 4);
    }
}