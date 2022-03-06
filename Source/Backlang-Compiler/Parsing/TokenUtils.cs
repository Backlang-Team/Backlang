﻿using Backlang_Compiler.Core;
using System.Reflection;

namespace Backlang_Compiler.Parsing;

public static class TokenUtils
{
    private static readonly Dictionary<string, TokenType> TokenTypeRepresentations = new Dictionary<string, TokenType>();

    static TokenUtils()
    {
        var typeValues = (TokenType[])Enum.GetValues(typeof(TokenType));

        foreach (var keyword in typeValues)
        {
            var attributes = keyword.GetType().GetField(Enum.GetName<TokenType>(keyword)).GetCustomAttributes<KeywordAttribute>(true);

            if (attributes != null && attributes.Any())
            {
                foreach (var attribute in attributes)
                {
                    TokenTypeRepresentations.Add(attribute.Keyword, keyword);
                }
            }
        }
    }

    public static TokenType GetTokenType(string text)
    {
        if (TokenTypeRepresentations.ContainsKey(text))
        {
            return TokenTypeRepresentations[text];
        }

        return TokenType.Identifier;
    }
}