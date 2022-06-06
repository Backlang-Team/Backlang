﻿using Furesoft.Core.CodeDom.Backends.CLR.Emit;
using Furesoft.Core.CodeDom.Compiler.Core;
using Furesoft.Core.CodeDom.Compiler.Core.Names;
using Furesoft.Core.CodeDom.Compiler.Core.TypeSystem;
using Furesoft.Core.CodeDom.Compiler.Pipeline;
using Furesoft.Core.CodeDom.Compiler.TypeSystem;
using Mono.Cecil;
using System.Runtime.Versioning;

namespace Backlang.Driver.Compiling.Targets;

public class DotNetAssembly : ITargetAssembly
{
    private readonly IAssembly _assembly;
    private readonly AssemblyContentDescription _description;
    private readonly TypeEnvironment _environment;
    private AssemblyDefinition _assemblyDefinition;

    public DotNetAssembly(AssemblyContentDescription description)
    {
        _assembly = description.Assembly;

        var name = new AssemblyNameDefinition(_assembly.FullName.ToString(),
            new Version(1, 0));

        _assemblyDefinition = AssemblyDefinition.CreateAssembly(name, "Module", ModuleKind.Dll);

        _description = description;
        _environment = description.Environment;

        SetTargetFramework();

        //_assemblyDefinition.MainModule.AssemblyReferences.Add(new AssemblyNameReference("mscorlib", new Version(4, 0, 0, 0)));
        _assemblyDefinition.MainModule.AssemblyReferences.Add(new AssemblyNameReference("System.Private.CoreLib", new Version(7, 0, 0, 0)));
    }

    public void WriteTo(Stream output)
    {
        foreach (var type in _assembly.Types)
        {
            var clrType = new TypeDefinition(type.FullName.Qualifier.ToString(),
                type.Name.ToString(), TypeAttributes.Class | TypeAttributes.Public);

            if (type.BaseTypes.Any())
            {
                if (type.BaseTypes.First().Name.ToString() == "ValueType")
                {
                    clrType.BaseType = _assemblyDefinition.MainModule.ImportReference(typeof(System.ValueType));

                    clrType.ClassSize = 1;
                    clrType.PackingSize = 0;
                }
                else
                {
                    clrType.BaseType = Resolve(type.BaseTypes.First().FullName);
                }
            }
            else
            {
                clrType.BaseType = _assemblyDefinition.MainModule.ImportReference(typeof(System.Object));
            }

            foreach (DescribedBodyMethod m in type.Methods)
            {
                var clrMethod = new MethodDefinition(m.Name.ToString(),
                    MethodAttributes.Public | MethodAttributes.Static,
                    _assemblyDefinition.MainModule.ImportReference(Type.GetType(m.ReturnParameter.Type == null ? "System.Void" : m.ReturnParameter.Type.FullName.ToString())));

                if (m == _description.EntryPoint)
                {
                    _assemblyDefinition.EntryPoint = clrMethod;
                }

                foreach (var p in m.Parameters)
                {
                    clrMethod.Parameters.Add(new ParameterDefinition(p.Name.ToString(), ParameterAttributes.None,
                        Resolve(p.Type.FullName)));
                }

                clrMethod.Body = ClrMethodBodyEmitter.Compile(m.Body, clrMethod, _environment);

                clrType.Methods.Add(clrMethod);
            }

            _assemblyDefinition.MainModule.Types.Add(clrType);
        }

        _assemblyDefinition.Write(output);
    }

    private TypeReference Resolve(QualifiedName name)
    {
        return _assemblyDefinition.MainModule.ImportReference(Type.GetType(name.ToString()));
    }

    private void SetTargetFramework()
    {
        var tf = _assemblyDefinition.MainModule.ImportReference(typeof(TargetFrameworkAttribute).GetConstructors().First());

        var item = new CustomAttribute(tf);
        item.ConstructorArguments.Add(
            new CustomAttributeArgument(_assemblyDefinition.MainModule.ImportReference(typeof(string)), ".NETCoreApp,Version=v6.0"));

        _assemblyDefinition.CustomAttributes.Add(item);
    }
}