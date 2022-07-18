﻿using Furesoft.Core.CodeDom.Compiler.Core.TypeSystem;
using Furesoft.Core.CodeDom.Compiler.Pipeline;
using LeMP;
using Loyc.Syntax;

namespace Backlang.Driver.Compiling.Targets.bs2k;

public class BS2KTarget : ICompilationTarget
{
    public string Name => "bs2k";

    public bool HasIntrinsics => true;

    public Type IntrinsicType => typeof(Intrinsics);

    public void AfterCompiling(CompilerContext context)
    {
    }

    public void BeforeCompiling(CompilerContext context)
    {
        context.OutputFilename += ".bsm";
    }

    public void BeforeExpandMacros(MacroProcessor processor)
    {
    }

    public ITargetAssembly Compile(AssemblyContentDescription contents)
    {
        return new Bs2kAssembly(contents);
    }

    public LNode ConvertIntrinsic(LNode call)
    {
        var ns = IntrinsicType.Namespace;
        var nsSplitted = ns.Split('.');

        LNode qualifiedName = LNode.Missing;
        for (var i = 0; i < nsSplitted.Length; i++)
        {
            var n = nsSplitted[i];

            if (qualifiedName == LNode.Missing)
            {
                qualifiedName = n.dot(nsSplitted[i + 1]);

                i += 1;
            }
            else
            {
                qualifiedName = qualifiedName.dot(n);
            }
        }

        return qualifiedName.dot(IntrinsicType.Name).coloncolon(call);
    }

    public TypeEnvironment Init(TypeResolver binder)
    {
        return new Bs2KTypeEnvironment();
    }
}