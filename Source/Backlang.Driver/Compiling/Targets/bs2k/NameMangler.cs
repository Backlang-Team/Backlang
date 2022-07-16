﻿using Furesoft.Core.CodeDom.Compiler.Core;
using System.Text;

namespace Backlang.Driver.Compiling.Targets.bs2k;

public class NameMangler
{
    public static string Mangle(IMethod method)
    {
        var sb = new StringBuilder();

        sb.Append("$").Append(method.FullName.Qualifier.ToString());

        foreach (var param in method.Parameters)
        {
            sb.Append("$").Append(MangleTypeName(param.Type));
        }

        return sb.ToString();
    }

    private static string MangleTypeName(IType type)
    {
        if (type.FullName.Qualifier.ToString() == "System")
        {
            return type.Name.ToString().ToUpper();
        }

        return string.Empty;
    }
}