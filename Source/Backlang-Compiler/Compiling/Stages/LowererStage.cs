using Backlang_Compiler.Compiling.Passes.Lowerer;
using Flo;

namespace Backlang_Compiler.Compiling.Stages;

public sealed class LowererStage : IHandler<CompilerContext, CompilerContext>
{
    private PassManager _optimization = new();

    public LowererStage()
    {
        _optimization.AddPass<ForLowerer>();
    }

    public async Task<CompilerContext> HandleAsync(CompilerContext context, Func<CompilerContext, Task<CompilerContext>> next)
    {
        for (int i = 0; i < context.Trees.Count; i++)
        {
            context.Trees[i] = _optimization.Process(context.Trees[i]);
        }

        return await next.Invoke(context);
    }
}