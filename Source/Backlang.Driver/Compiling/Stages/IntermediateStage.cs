using Backlang.Codeanalysis.Parsing.AST;
using Backlang.Driver.Compiling.Targets.Dotnet;
using Flo;
using Furesoft.Core.CodeDom.Compiler;
using Furesoft.Core.CodeDom.Compiler.Analysis;
using Furesoft.Core.CodeDom.Compiler.Core;
using Furesoft.Core.CodeDom.Compiler.Core.Collections;
using Furesoft.Core.CodeDom.Compiler.Core.Names;
using Furesoft.Core.CodeDom.Compiler.Core.TypeSystem;
using Furesoft.Core.CodeDom.Compiler.Flow;
using Furesoft.Core.CodeDom.Compiler.TypeSystem;
using Loyc.Syntax;
using System.Collections.Immutable;

namespace Backlang.Driver.Compiling.Stages;

public sealed class IntermediateStage : IHandler<CompilerContext, CompilerContext>
{
    public static readonly ImmutableDictionary<string, Type> TypenameTable = new Dictionary<string, Type>()
    {
        ["obj"] = typeof(object),

        ["bool"] = typeof(bool),

        ["u8"] = typeof(byte),
        ["u16"] = typeof(ushort),
        ["u32"] = typeof(uint),
        ["u64"] = typeof(ulong),

        ["i8"] = typeof(sbyte),
        ["i16"] = typeof(short),
        ["i32"] = typeof(int),
        ["i64"] = typeof(long),

        ["f16"] = typeof(Half),
        ["f32"] = typeof(float),
        ["f64"] = typeof(double),

        ["char"] = typeof(char),
        ["string"] = typeof(string),
    }.ToImmutableDictionary();

    public static IType GetType(LNode type, CompilerContext context)
    {
        //function without return type set
        if (type.ArgCount > 0)
            type = type.Args[0].Args[0];

        if (type == LNode.Missing || type.ArgCount > 0 && type.Args[0].Name.Name == "#")
            return ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(void));

        if (type.Name == CodeSymbols.Fn)
        {
            string typename = string.Empty;
            typename = type.Args[0] == LNode.Missing ? "Action`" + (type.Args[2].ArgCount) : "Func`" + (type.Args[2].ArgCount + 1);

            var fnType = ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typename, "System");
            foreach (var garg in type.Args[2])
            {
                fnType.AddGenericParameter(new DescribedGenericParameter(fnType, garg.Name.Name.ToString())); //ToDo: replace primitive aliases with real .net typenames
            }

            return fnType;
        }

        var name = type.ArgCount > 0 ? type.Args[0].Name.ToString().Replace("#", "") : type.Name.ToString();

        if (TypenameTable.ContainsKey(name))
        {
            return ClrTypeEnvironmentBuilder.ResolveType(context.Binder, TypenameTable[name]);
        }
        else
        {
            return ClrTypeEnvironmentBuilder.ResolveType(context.Binder, name, context.Assembly.Name.Qualify().FullName);
        }
    }

    public async Task<CompilerContext> HandleAsync(CompilerContext context, Func<CompilerContext, Task<CompilerContext>> next)
    {
        context.Assembly = new DescribedAssembly(new QualifiedName(context.OutputFilename.Replace(".dll", "")));
        context.ExtensionsType = new DescribedType(new SimpleName(Names.Extensions).Qualify(string.Empty), context.Assembly)
        {
            IsStatic = true
        };

        foreach (var tree in context.Trees)
        {
            var modulename = Utils.GetModuleName(tree);

            foreach (var st in tree.Body)
            {
                ConvertTypesOrInterfaces(context, st, modulename);
                ConvertEnums(context, st, modulename);
                ConvertDiscriminatedUnions(context, st, modulename);
            }
        }

        context.Assembly.AddType(context.ExtensionsType);
        context.Binder.AddAssembly(context.Assembly);

        return await next.Invoke(context);
    }

    private static void ConvertEnums(CompilerContext context, LNode @enum, QualifiedName modulename)
    {
        if (!(@enum.IsCall && @enum.Name == CodeSymbols.Enum)) return;

        var name = @enum.Args[0].Name;

        var type = new DescribedType(new SimpleName(name.Name).Qualify(modulename), context.Assembly);
        type.AddBaseType(context.Binder.ResolveTypes(new SimpleName("Enum").Qualify("System")).First());

        type.AddAttribute(AccessModifierAttribute.Create(AccessModifier.Public));

        context.Assembly.AddType(type);
    }

    private static void ConvertTypesOrInterfaces(CompilerContext context, LNode st, QualifiedName modulename)
    {
        if (!(st.IsCall && (st.Name == CodeSymbols.Struct || st.Name == CodeSymbols.Class || st.Name == CodeSymbols.Interface))) return;

        var name = st.Args[0].Name;

        var type = new DescribedType(new SimpleName(name.Name).Qualify(modulename), context.Assembly);
        if (st.Name == CodeSymbols.Struct)
        {
            type.AddBaseType(context.Binder.ResolveTypes(new SimpleName("ValueType").Qualify("System")).First()); // make it a struct
        }
        else if (st.Name == CodeSymbols.Interface)
        {
            type.AddAttribute(FlagAttribute.InterfaceType);
        }

        Utils.SetAccessModifier(st, type);
        SetOtherModifiers(st, type);

        context.Assembly.AddType(type);
    }

    private static void SetOtherModifiers(LNode node, DescribedType type)
    {
        if (node.Attrs.Contains(LNode.Id(CodeSymbols.Static)))
        {
            type.IsStatic = true;
        }
        if (node.Attrs.Contains(LNode.Id(CodeSymbols.Abstract)))
        {
            type.IsAbstract = true;
        }
    }

    private void ConvertDiscriminatedUnions(CompilerContext context, LNode discrim, QualifiedName modulename)
    {
        if (!(discrim.IsCall && discrim.Name == Symbols.DiscriminatedUnion)) return;

        var name = discrim.Args[0].Name;

        var baseType = new DescribedType(new SimpleName(name.Name).Qualify(modulename), context.Assembly);
        Utils.SetAccessModifier(discrim, baseType);
        baseType.IsAbstract = true;

        context.Assembly.AddType(baseType);

        foreach (var type in discrim.Args[1].Args)
        {
            var discName = type.Args[0].Name;
            var discType = new DescribedType(new SimpleName(discName.Name).Qualify(modulename), context.Assembly);
            Utils.SetAccessModifier(discrim, discType);
            discType.AddBaseType(baseType);
            context.Assembly.AddType(discType);

            foreach (var field in type.Args[1].Args)
            {
                var fieldName = field.Args[1].Args[0].Name;
                var fieldType = new DescribedField(discType, new SimpleName(fieldName.Name), false, TypeInheritanceStage.ResolveTypeWithModule(field.Args[0].Args[0].Args[0], context, modulename, Utils.GetQualifiedName(field.Args[0].Args[0].Args[0])));
                if (field.Attrs.Any(_ => _.Name == Symbols.Mutable))
                {
                    fieldType.AddAttribute(Attributes.Mutable);
                }
                fieldType.IsPublic = true;
                discType.AddField(fieldType);
            }

            var constructor = new DescribedBodyMethod(discType, new SimpleName(".ctor"), false, context.Environment.Void);
            constructor.IsConstructor = true;
            constructor.IsPublic = true;

            foreach (var field in discType.Fields)
            {
                constructor.AddParameter(new Parameter(field.FieldType, field.Name));
            }

            var graph = new FlowGraphBuilder();
            // Use a permissive exception delayability model to make the optimizer's
            // life easier.
            graph.AddAnalysis(
                new ConstantAnalysis<ExceptionDelayability>(
                    PermissiveExceptionDelayability.Instance));

            // Grab the entry point block.
            var block = graph.EntryPoint;

            block.Flow = new ReturnFlow();

            var objType = context.Binder.ResolveTypes(new SimpleName("Object").Qualify("System")).First();
            var baseCtor = objType.Methods.First(_ => _.IsConstructor);

            block.AppendInstruction(Instruction.CreateLoadArg(new Parameter(discType))); //this ptr
            block.AppendInstruction(Instruction.CreateCall(baseCtor, Furesoft.Core.CodeDom.Compiler.Instructions.MethodLookup.Static, new[] { new ValueTag() }));

            for (var i = 0; i < constructor.Parameters.Count; i++)
            {
                var p = constructor.Parameters[i];
                var f = discType.Fields[i];

                block.AppendInstruction(Instruction.CreateLoadArg(new Parameter(discType))); //this ptr

                block.AppendInstruction(Instruction.CreateLoadArg(p));
                block.AppendInstruction(Instruction.CreateStoreFieldPointer(f));
            }

            block.Flow = new ReturnFlow();

            constructor.Body = new MethodBody(new Parameter(), new Parameter(discType), EmptyArray<Parameter>.Value, graph.ToImmutable());

            discType.AddMethod(constructor);
        }
    }
}