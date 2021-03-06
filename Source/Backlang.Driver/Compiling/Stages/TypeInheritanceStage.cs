using Backlang.Codeanalysis.Parsing.AST;
using Backlang.Driver.Compiling.Targets.Dotnet;
using Flo;
using Furesoft.Core.CodeDom.Compiler;
using Furesoft.Core.CodeDom.Compiler.Analysis;
using Furesoft.Core.CodeDom.Compiler.Core;
using Furesoft.Core.CodeDom.Compiler.Core.Collections;
using Furesoft.Core.CodeDom.Compiler.Core.Constants;
using Furesoft.Core.CodeDom.Compiler.Core.Names;
using Furesoft.Core.CodeDom.Compiler.Core.TypeSystem;
using Furesoft.Core.CodeDom.Compiler.Flow;
using Furesoft.Core.CodeDom.Compiler.Instructions;
using Furesoft.Core.CodeDom.Compiler.TypeSystem;
using Loyc;
using Loyc.Syntax;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Backlang.Driver.Compiling.Stages;

public sealed class TypeInheritanceStage : IHandler<CompilerContext, CompilerContext>
{
    private static readonly ImmutableDictionary<string, string> Aliases = new Dictionary<string, string>()
    {
        ["bool"] = "Boolean",

        ["i8"] = "Byte",
        ["i16"] = "Int16",
        ["i32"] = "Int32",
        ["i64"] = "Int64",

        ["u8"] = "Byte",
        ["u16"] = "UInt16",
        ["u32"] = "UInt32",
        ["u64"] = "UInt64",

        ["f16"] = "Half",
        ["f32"] = "Single",
        ["f64"] = "Double",

        ["char"] = "Char",
        ["string"] = "String",
        ["none"] = "Void",
    }.ToImmutableDictionary();

    private static List<MethodBodyCompilation> _bodyCompilations = new();

    public static MethodBody CompileBody(LNode function, CompilerContext context, IMethod method,
        QualifiedName? modulename)
    {
        var graph = new FlowGraphBuilder();
        // Use a permissive exception delayability model to make the optimizer's
        // life easier.
        graph.AddAnalysis(
            new ConstantAnalysis<ExceptionDelayability>(
                PermissiveExceptionDelayability.Instance));

        // Grab the entry point block.
        var block = graph.EntryPoint;

        var testBlock = graph.AddBasicBlock("testBlock");

        testBlock.AppendInstruction(Instruction.CreateLoad(context.Environment.Int32,
            testBlock.AppendInstruction(Instruction.CreateConstant(new IntegerConstant(65),
            context.Environment.Int32))));

        AppendBlock(function.Args[3], block, context, method, modulename);

        return new MethodBody(
            method.ReturnParameter,
            new Parameter(method.ParentType),
            EmptyArray<Parameter>.Value,
            graph.ToImmutable());
    }

    record struct MethodBodyCompilation(LNode function, CompilerContext context, DescribedBodyMethod method, QualifiedName? modulename);

    public static DescribedBodyMethod ConvertFunction(CompilerContext context, DescribedType type,
        LNode function, QualifiedName modulename, string methodName = null, bool hasBody = true)
    {
        if (methodName == null) methodName = GetMethodName(function);

        var returnType = ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(void));

        var method = new DescribedBodyMethod(type,
            new QualifiedName(methodName).FullyUnqualifiedName,
            function.Attrs.Contains(LNode.Id(CodeSymbols.Static)), returnType);

        Utils.SetAccessModifier(function, method);

        ConvertAnnotations(function, method, context, modulename,
            AttributeTargets.Method, (attr, t) => ((DescribedBodyMethod)t).AddAttribute(attr));

        if (function.Attrs.Contains(LNode.Id(CodeSymbols.Operator)))
        {
            method.AddAttribute(new DescribedAttribute(ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(SpecialNameAttribute))));
        }
        if (function.Attrs.Contains(LNode.Id(CodeSymbols.Override)))
        {
            method.IsOverride = true;
        }
        if (function.Attrs.Contains(LNode.Id(CodeSymbols.Extern)))
        {
            method.IsExtern = true;
        }
        if (function.Attrs.Contains(LNode.Id(CodeSymbols.Abstract)))
        {
            method.AddAttribute(FlagAttribute.Abstract);
        }

        AddParameters(method, function, context, modulename);
        SetReturnType(method, function, context, modulename);

        if (methodName == ".ctor")
        {
            method.IsConstructor = true;
        }
        else if (methodName == ".dtor")
        {
            method.IsDestructor = true;
        }

        if (hasBody)
        {
            //body = CompileBody(function, context, method, type, modulename);
            _bodyCompilations.Add(new(function, context, method, modulename));
        }

        if (type.Methods.Any(_ => _.FullName.FullName.Equals(method.FullName.FullName)))
        {
            context.AddError(function, "Function '" + method.FullName + "' is already defined.");
            return null;
        }

        return method;
    }

    public static void ConvertTypeMembers(LNode members, DescribedType type, CompilerContext context, QualifiedName modulename)
    {
        foreach (var member in members.Args)
        {
            if (member.Name == CodeSymbols.Var)
            {
                ConvertFields(type, context, member, modulename);
            }
            else if (member.Calls(CodeSymbols.Fn))
            {
                type.AddMethod(ConvertFunction(context, type, member, modulename, hasBody: false));
            }
            else if (member.Calls(CodeSymbols.Property))
            {
                type.AddProperty(ConvertProperty(context, type, member));
            }
        }
    }

    public static IType GetLiteralType(LNode value, TypeResolver resolver)
    {
        if (value.Calls(CodeSymbols.String)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(string));
        else if (value.Calls(CodeSymbols.Char)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(char));
        else if (value.Calls(CodeSymbols.Bool)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(bool));
        else if (value.Calls(CodeSymbols.Int8)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(byte));
        else if (value.Calls(CodeSymbols.Int16)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(short));
        else if (value.Calls(CodeSymbols.UInt16)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(ushort));
        else if (value.Calls(CodeSymbols.Int32)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(int));
        else if (value.Calls(CodeSymbols.UInt32)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(uint));
        else if (value.Calls(CodeSymbols.Int64)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(long));
        else if (value.Calls(CodeSymbols.UInt64)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(ulong));
        else if (value.Calls(Symbols.Float16)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(Half));
        else if (value.Calls(Symbols.Float32)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(float));
        else if (value.Calls(Symbols.Float64)) return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(double));
        else if (value is IdNode id) { } //todo: symbol table

        return ClrTypeEnvironmentBuilder.ResolveType(resolver, typeof(void));
    }

    public static DescribedType ResolveTypeWithModule(LNode typeNode, CompilerContext context, QualifiedName modulename, QualifiedName fullName)
    {
        var resolvedType = context.Binder.ResolveTypes(fullName).FirstOrDefault();

        if (resolvedType == null)
        {
            resolvedType = context.Binder.ResolveTypes(fullName.Qualify(modulename)).FirstOrDefault();

            if (resolvedType == null)
            {
                var primitiveName = GetNameOfPrimitiveType(context.Binder, fullName.FullyUnqualifiedName.ToString());

                if (!primitiveName.HasValue)
                {
                    resolvedType = null;
                }
                else
                {
                    resolvedType = context.Binder.ResolveTypes(primitiveName.Value).FirstOrDefault();
                }

                if (resolvedType == null)
                {
                    context.AddError(typeNode, $"Type {fullName} cannot be found");
                }
            }
        }

        return (DescribedType)resolvedType;
    }

    public static void ConvertAnnotations(LNode st, IMember type,
        CompilerContext context, QualifiedName modulename, AttributeTargets targets,
        Action<DescribedAttribute, IMember> applyAttributeCallback)
    {
        for (var i = 0; i < st.Attrs.Count; i++)
        {
            var annotation = st.Attrs[i];
            if (annotation.Calls(Symbols.Annotation))
            {
                annotation = annotation.Attrs[0];

                var fullname = Utils.GetQualifiedName(annotation.Target);

                if (!fullname.FullyUnqualifiedName.ToString().EndsWith("Attribute"))
                {
                    fullname = AppendAttributeToName(fullname);
                }

                var resolvedType = ResolveTypeWithModule(annotation.Target, context, modulename, fullname);

                if (resolvedType == null) continue; //Todo: Add Error

                var customAttribute = new DescribedAttribute(resolvedType);

                //ToDo: add arguments to custom attribute
                //ToDo: only add attribute if attributeusage is right

                var attrUsage = (DescribedAttribute)resolvedType.Attributes
                    .GetAll()
                    .FirstOrDefault(_ => _.AttributeType.FullName.ToString() == typeof(AttributeUsageAttribute).FullName);

                if (attrUsage != null)
                {
                    var target = attrUsage.ConstructorArguments.FirstOrDefault(_ => _.Value is AttributeTargets);
                    var targetValue = (AttributeTargets)target.Value;

                    if (targetValue.HasFlag(AttributeTargets.All) || targets.HasFlag(targetValue))
                    {
                        applyAttributeCallback(customAttribute, type);
                    }
                    else
                    {
                        context.AddError(st, "Cannot apply Attribute");
                    }
                }
            }
        }
    }

    public static DescribedProperty ConvertProperty(CompilerContext context, DescribedType type, LNode member)
    {
        var property = new DescribedProperty(new SimpleName(member.Args[3].Args[0].Name.Name), IntermediateStage.GetType(member.Args[0], context), type);

        Utils.SetAccessModifier(member, property);

        if (member.Args[1] != LNode.Missing)
        {
            // getter defined
            var getter = new DescribedPropertyMethod(new SimpleName($"get_{property.Name}"), type);
            Utils.SetAccessModifier(member.Args[1], getter, property.GetAccessModifier());
            property.Getter = getter;
        }

        if (member.Args[2] != LNode.Missing)
        {
            // setter defined
            var setter = new DescribedPropertyMethod(new SimpleName($"set_{property.Name}"), type);
            setter.AddAttribute(AccessModifierAttribute.Create(AccessModifier.Private));
            Utils.SetAccessModifier(member.Args[2], setter, property.GetAccessModifier());
            property.Setter = setter;
        }

        return property;
    }

    public async Task<CompilerContext> HandleAsync(CompilerContext context, Func<CompilerContext, Task<CompilerContext>> next)
    {
        foreach (var tree in context.Trees)
        {
            var modulename = Utils.GetModuleName(tree);

            foreach (var node in tree.Body)
            {
                ConvertTypesOrInterface(context, node, modulename);

                ConvertFreeFunctions(context, node, modulename);

                ConvertEnums(context, node, modulename);

                ConvertUnion(context, node, modulename);
            }
        }

        ConvertMethodBodies();

        return await next.Invoke(context);
    }

    private static void AppendBlock(LNode blkNode, BasicBlockBuilder block, CompilerContext context, IMethod method, QualifiedName? modulename)
    {
        foreach (var node in blkNode.Args)
        {
            if (!node.IsCall) continue;

            if (node.Name == CodeSymbols.Var)
            {
                AppendVariableDeclaration(context, method, block, node, modulename);
            }
            else if (node.Name == (Symbol)"print")
            {
                AppendCall(context, block, node, context.writeMethods, "Write");
            }
            else if (node.Calls(CodeSymbols.Return))
            {
                if (node.ArgCount == 1)
                {
                    var valueNode = node.Args[0];

                    AppendExpression(block, valueNode, (DescribedType)context.Environment.Int32, method); //ToDo: Deduce Type

                    block.Flow = new ReturnFlow();
                }
                else
                {
                    block.Flow = new ReturnFlow();
                }
            }
            else if (node.Calls(CodeSymbols.Throw))
            {
                var valueNode = node.Args[0].Args[0];
                var constant = block.AppendInstruction(ConvertConstant(
                    GetLiteralType(valueNode, context.Binder), valueNode.Value));

                var msg = block.AppendInstruction(Instruction.CreateLoad(GetLiteralType(valueNode, context.Binder), constant));

                if (node.Args[0].Name.Name == "#string")
                {
                    var exceptionType = ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(Exception));
                    var exceptionCtor = exceptionType.Methods.FirstOrDefault(_ => _.IsConstructor && _.Parameters.Count == 1);

                    block.AppendInstruction(Instruction.CreateNewObject(exceptionCtor, new List<ValueTag> { msg }));
                }

                block.Flow = UnreachableFlow.Instance;
            }
            else if (node.Calls(Symbols.ColonColon))
            {
                var callee = node.Args[1];
                var typename = Utils.GetQualifiedName(node.Args[0]);

                var type = (DescribedType)context.Binder.ResolveTypes(typename).FirstOrDefault();

                AppendCall(context, block, callee, type.Methods, callee.Name.Name);
            }
            else
            {
                //ToDo: continue implementing static function call in same type
                var type = method.ParentType;
                var calleeName = node.Target;
                var callee = type.Methods.FirstOrDefault(_ => _.IsStatic && _.Name.ToString() == calleeName.Name.Name);

                if (callee != null)
                {
                    AppendCall(context, block, node, type.Methods);
                }
                else
                {
                    context.AddError(node, $"Cannot find static function '{calleeName.Name.Name}'");
                }
            }
        }
    }

    private static QualifiedName AppendAttributeToName(QualifiedName fullname)
    {
        var qualifier = fullname.Slice(0, fullname.PathLength - 1);

        return new SimpleName(fullname.FullyUnqualifiedName.ToString() + "Attribute").Qualify(qualifier);
    }

    private static string GetMethodName(LNode function)
    {
        return function.Args[1].Args[0].Args[0].Name.Name;
    }

    private static void AddParameters(DescribedBodyMethod method, LNode function, CompilerContext context, QualifiedName modulename)
    {
        var param = function.Args[2];

        foreach (var p in param.Args)
        {
            var pa = ConvertParameter(p, context, modulename);
            method.AddParameter(pa);
        }
    }

    private static void AppendCall(CompilerContext context, BasicBlockBuilder block,
        LNode node, IEnumerable<IMethod> methods, string methodName = null)
    {
        var argTypes = new List<IType>();
        var callTags = new List<ValueTag>();

        foreach (var arg in node.Args)
        {
            var type = GetLiteralType(arg, context.Binder);
            argTypes.Add(type);

            var constant = block.AppendInstruction(
            ConvertConstant(type, arg.Args[0].Value));

            block.AppendInstruction(Instruction.CreateLoad(type, constant));

            callTags.Add(constant);
        }

        if (methodName == null)
        {
            methodName = node.Name.Name;
        }

        var method = GetMatchingMethod(context, argTypes, methods, methodName);

        var call = Instruction.CreateCall(method, MethodLookup.Static, callTags);

        block.AppendInstruction(call);
    }

    private static IMethod GetMatchingMethod(CompilerContext context, List<IType> argTypes, IEnumerable<IMethod> methods, string methodname)
    {
        foreach (var m in methods)
        {
            if (m.Name.ToString() != methodname) continue;

            if (m.Parameters.Count == argTypes.Count)
            {
                if (MatchesParameters(m, argTypes))
                    return m;
            }
        }

        return null;
    }

    private static bool MatchesParameters(IMethod m, List<IType> argTypes)
    {
        bool matches = false;
        for (int i = 0; i < m.Parameters.Count; i++)
        {
            if (m.Parameters[i].Type.FullName.ToString() == argTypes[i].FullName.ToString())
            {
                matches = (matches || i == 0) && m.Parameters[i].Type.FullName.ToString() == argTypes[i].FullName.ToString();
            }
        }

        return matches;
    }

    private static void AppendVariableDeclaration(CompilerContext context, IMethod method, BasicBlockBuilder block, LNode node, QualifiedName? modulename)
    {
        var decl = node.Args[1];

        var name = Utils.GetQualifiedName(node.Args[0].Args[0].Args[0]);
        var elementType = (DescribedType)context.Binder.ResolveTypes(name.Qualify(modulename.Value)).FirstOrDefault();

        if (elementType == null)
        {
            elementType = (DescribedType)context.Binder.ResolveTypes(name).FirstOrDefault();

            if (elementType == null)
            {
                elementType = (DescribedType)IntermediateStage.GetType(node.Args[0].Args[0].Args[0], context);
            }
        }

        block.AppendParameter(new BlockParameter(elementType, decl.Args[0].Name.Name));

        AppendExpression(block, decl.Args[1], elementType, method);

        block.AppendInstruction(Instruction.CreateAlloca(elementType));
    }

    private static NamedInstructionBuilder AppendExpression(BasicBlockBuilder block, LNode node, DescribedType elementType, IMethod method)
    {
        if (node.ArgCount == 1 && node.Args[0].HasValue)
        {
            var constant = ConvertConstant(elementType, node.Args[0].Value);
            var value = block.AppendInstruction(constant);

            return block.AppendInstruction(Instruction.CreateLoad(elementType, value));
        }
        else if (node.ArgCount == 2)
        {
            var lhs = AppendExpression(block, node.Args[0], elementType, method);
            var rhs = AppendExpression(block, node.Args[1], elementType, method);

            return block.AppendInstruction(Instruction.CreateBinaryArithmeticIntrinsic(node.Name.Name.Substring(1), false, elementType, lhs, rhs));
        }
        else if (node.IsId)
        {
            var par = method.Parameters.Where(_ => _.Name.ToString() == node.Name.Name);

            if (!par.Any())
            {
                var localPrms = block.Parameters.Where(_ => _.Tag.Name.ToString() == node.Name.Name);
                if (localPrms.Any())
                {
                    return block.AppendInstruction(Instruction.CreateLoadLocal(new Parameter(localPrms.First().Type, localPrms.First().Tag.Name)));
                }
            }
            else
            {
                return block.AppendInstruction(Instruction.CreateLoadArg(par.First()));
            }
        }

        return null;
    }

    private static Instruction ConvertConstant(IType elementType, object value)
    {
        Constant constant;
        switch (value)
        {
            case uint v:
                constant = new IntegerConstant(v);
                break;

            case int v:
                constant = new IntegerConstant(v);
                break;

            case long v:
                constant = new IntegerConstant(v);
                break;

            case ulong v:
                constant = new IntegerConstant(v);
                break;

            case byte v:
                constant = new IntegerConstant(v);
                break;

            case short v:
                constant = new IntegerConstant(v);
                break;

            case ushort v:
                constant = new IntegerConstant(v);
                break;

            case float v:
                constant = new Float32Constant(v);
                break;

            case double v:
                constant = new Float64Constant(v);
                break;

            case string v:
                constant = new StringConstant(v);
                break;

            case char v:
                constant = new IntegerConstant(v);
                break;

            case bool v:
                constant = BooleanConstant.Create(v);
                break;

            default:
                constant = NullConstant.Instance;
                break;
        }

        return Instruction.CreateConstant(constant,
                                           elementType);
    }

    private static void ConvertEnums(CompilerContext context, LNode node, QualifiedName modulename)
    {
        if (!(node.IsCall && node.Name == CodeSymbols.Enum)) return;

        var name = node.Args[0].Name;
        var members = node.Args[2];

        var type = (DescribedType)context.Binder.ResolveTypes(new SimpleName(name.Name).Qualify(modulename)).First();

        var i = -1;
        foreach (var member in members.Args)
        {
            if (member.Name == CodeSymbols.Var)
            {
                IType mtype;
                if (member.Args[0] == LNode.Missing)
                {
                    mtype = context.Environment.Int32;
                }
                else
                {
                    mtype = IntermediateStage.GetType(member.Args[0], context);
                }

                var mname = member.Args[1].Args[0].Name;
                var mvalue = member.Args[1].Args[1];

                if (mvalue == LNode.Missing)
                {
                    i++;
                }
                else
                {
                    i = (int)mvalue.Args[0].Value;
                }

                var field = new DescribedField(type, new SimpleName(mname.Name), true, mtype);
                field.InitialValue = i;

                type.AddField(field);
            }
        }

        var valueField = new DescribedField(type, new SimpleName("value__"), false, context.Environment.Int32);
        valueField.AddAttribute(new DescribedAttribute(ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(SpecialNameAttribute))));

        type.AddField(valueField);
    }

    private static void ConvertFields(DescribedType type, CompilerContext context, LNode member, QualifiedName modulename)
    {
        var ftype = member.Args[0].Args[0].Args[0];
        var fullname = Utils.GetQualifiedName(ftype);

        var mtype = ResolveTypeWithModule(ftype, context, modulename, fullname);

        var mvar = member.Args[1];
        var mname = mvar.Args[0].Name;
        var mvalue = mvar.Args[1];

        var field = new DescribedField(type, new SimpleName(mname.Name), false, mtype);

        if (mvalue != LNode.Missing)
        {
            field.InitialValue = mvalue.Args[0].Value;
        }
        if (member.Attrs.Any(_ => _.Name == Symbols.Mutable))
        {
            field.AddAttribute(Attributes.Mutable);
        }

        type.AddField(field);
    }

    private static void ConvertFreeFunctions(CompilerContext context, LNode node, QualifiedName modulename)
    {
        if (!(node.IsCall && node.Name == CodeSymbols.Fn)) return;

        DescribedType type;

        if (!context.Assembly.Types.Any(_ => _.FullName.FullName == $".{Names.ProgramClass}"))
        {
            type = new DescribedType(new SimpleName(Names.ProgramClass).Qualify(string.Empty), context.Assembly);
            type.IsStatic = true;
            type.IsPublic = true;

            context.Assembly.AddType(type);
        }
        else
        {
            type = (DescribedType)context.Assembly.Types.First(_ => _.FullName.FullName == $".{Names.ProgramClass}");
        }

        string methodName = GetMethodName(node);
        if (methodName == "main") methodName = "Main";

        var method = ConvertFunction(context, type, node, modulename, methodName: methodName);

        if (method != null) type.AddMethod(method);
    }

    private static Parameter ConvertParameter(LNode p, CompilerContext context, QualifiedName modulename)
    {
        var ptype = p.Args[0].Args[0].Args[0];
        var fullname = Utils.GetQualifiedName(ptype);

        var type = ResolveTypeWithModule(ptype, context, modulename, fullname);
        var assignment = p.Args[1];

        var name = assignment.Args[0].Name;

        var param = new Parameter(type, name.ToString());

        if (!assignment.Args[1].Args.IsEmpty)
        {
            param.HasDefault = true;
            param.DefaultValue = assignment.Args[1].Args[0].Value;
        }

        return param;
    }

    private static void ConvertTypesOrInterface(CompilerContext context, LNode node, QualifiedName modulename)
    {
        if (!(node.IsCall &&
            (node.Name == CodeSymbols.Struct || node.Name == CodeSymbols.Class || node.Name == CodeSymbols.Interface))) return;

        var name = Utils.GetQualifiedName(node.Args[0]);
        var inheritances = node.Args[1];
        var members = node.Args[2];

        var type = (DescribedType)context.Binder.ResolveTypes(name.Qualify(modulename)).FirstOrDefault();

        ConvertAnnotations(node, type, context, modulename,
            AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct,
            (attr, t) => ((DescribedType)t).AddAttribute(attr));

        foreach (var inheritance in inheritances.Args)
        {
            var fullName = Utils.GetQualifiedName(inheritance);
            var btype = ResolveTypeWithModule(inheritance, context, modulename, fullName);

            if (btype != null)
            {
                if (!btype.IsSealed)
                {
                    type.AddBaseType(btype);
                }
                else
                {
                    context.AddError(inheritance, $"Cannot inherit from sealed Type {inheritance}");
                }
            }
        }

        ConvertTypeMembers(members, type, context, modulename);
    }

    private static void ConvertUnion(CompilerContext context, LNode node, QualifiedName modulename)
    {
        if (!(node.IsCall && node.Name == Symbols.Union)) return;

        var type = new DescribedType(new SimpleName(node.Args[0].Name.Name).Qualify(modulename), context.Assembly);
        type.AddBaseType(ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(ValueType)));

        var attributeType = ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(StructLayoutAttribute));

        var attribute = new DescribedAttribute(attributeType);
        attribute.ConstructorArguments.Add(
            new AttributeArgument(
                ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(LayoutKind)),
                LayoutKind.Explicit)
            );

        type.AddAttribute(attribute);

        ConvertAnnotations(node, type, context, modulename, AttributeTargets.Class,
            (attr, t) => ((DescribedType)t).AddAttribute(attr));

        foreach (var member in node.Args[1].Args)
        {
            if (member.Name == CodeSymbols.Var)
            {
                var ftype = member.Args[0].Args[0].Args[0];
                var fullname = Utils.GetQualifiedName(ftype);

                var mtype = ResolveTypeWithModule(ftype, context, modulename, fullname);

                var mvar = member.Args[1];
                var mname = mvar.Args[0].Name;
                var mvalue = mvar.Args[1];

                var field = new DescribedField(type, new SimpleName(mname.Name), false, mtype);

                attributeType = ClrTypeEnvironmentBuilder.ResolveType(context.Binder, typeof(FieldOffsetAttribute));
                attribute = new DescribedAttribute(attributeType);
                attribute.ConstructorArguments.Add(
                    new AttributeArgument(
                        mtype,
                        mvalue.Args[0].Value)
                    );

                field.AddAttribute(attribute);

                type.AddField(field);
            }
        }

        context.Assembly.AddType(type);
    }

    private static QualifiedName? GetNameOfPrimitiveType(TypeResolver binder, string name)
    {
        if (Aliases.ContainsKey(name))
        {
            name = Aliases[name];
        }

        var primitiveType = ClrTypeEnvironmentBuilder.ResolveType(binder, name, "System");

        if (primitiveType is not null)
        {
            return primitiveType.FullName;
        }

        return null;
    }

    private static void SetReturnType(DescribedBodyMethod method, LNode function, CompilerContext context, QualifiedName modulename)
    {
        var retType = function.Args[0].Args[0].Args[0];
        var fullName = Utils.GetQualifiedName(retType);

        var rtype = ResolveTypeWithModule(retType, context, modulename, fullName);

        method.ReturnParameter = new Parameter(rtype);
    }

    private void ConvertMethodBodies()
    {
        foreach (var bodyCompilation in _bodyCompilations)
        {
            bodyCompilation.method.Body =
                CompileBody(bodyCompilation.function, bodyCompilation.context,
                bodyCompilation.method, bodyCompilation.modulename);

            /*
        body = body.WithImplementation(
                body.Implementation.Transform(
                AllocaToRegister.Instance,
                CopyPropagation.Instance,
                new ConstantPropagation(),
                GlobalValueNumbering.Instance,
                CopyPropagation.Instance,
                DeadValueElimination.Instance,
                MemoryAccessElimination.Instance,
                CopyPropagation.Instance,
                new ConstantPropagation(),
                DeadValueElimination.Instance,
                ReassociateOperators.Instance,
                DeadValueElimination.Instance
            ));
        */
        }
    }
}