using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Terraria
{
    /// <summary>用真实 Cecil/ILContext 执行生产写入边界安装器；原版消息执行由独立格位夹具驱动。</summary>
    internal static class IL_MessageBuffer
    {
        internal static event ILContext.Manipulator GetData
        {
            add
            {
                using var module=ModuleDefinition.CreateModule("LiquidIngressFixture",ModuleKind.Dll);
                var type=new TypeDefinition("Fixture","Receiver",TypeAttributes.Public,module.TypeSystem.Object);
                module.Types.Add(type);
                var method=new MethodDefinition("GetData",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Void);
                type.Methods.Add(method);
                var setter=new MethodReference("liquidType",module.TypeSystem.Void,
                    new TypeReference("Terraria","Tile",module,module.TypeSystem.CoreLibrary)){HasThis=true};
                setter.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Call,setter));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                using var context=new ILContext(method);
                value(context);
                if(method.Body.Instructions.Count<=2)throw new Exception("Native ingress manipulator did not inject a boundary.");
            }
            remove { }
        }
    }
}
