using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using RT.Util.ExtensionMethods;

namespace NUnit.Direct
{
    public interface IInvokeDirect
    {
        object DoInvoke(object instance);
    }

    static class Helper
    {
        private static AssemblyBuilder asmBuilder;
        private static ModuleBuilder modBuilder;
        private static Dictionary<string, IInvokeDirect> invokes = new Dictionary<string, IInvokeDirect>();

        public static object InvokeMethodDirect(MethodInfo method, object instance, params object[] parameters)
        {
            if (!method.IsStatic && instance == null)
                throw new ArgumentException("Non-static method requires a non-null instance");
            if (method.IsStatic && instance != null)
                throw new ArgumentException("Static method requires a null instance");
            if (!method.IsStatic && instance.GetType().IsValueType)
                throw new NotSupportedException("Invoking methods on value types is not yet supported");
            if (method.IsGenericMethod || method.IsGenericMethodDefinition)
                throw new NotSupportedException("Generic methods not yet supported");

            if (asmBuilder == null)
            {
                asmBuilder = Thread.GetDomain().DefineDynamicAssembly(new AssemblyName("invoke_helper_assembly"), AssemblyBuilderAccess.RunAndSave);
                modBuilder = asmBuilder.DefineDynamicModule("invoke_helper_module");
            }

            var sig = getSignature(method);
            if (!invokes.ContainsKey(sig))
            {
                var typeBuilder = modBuilder.DefineType(
                    "invoke " + sig, TypeAttributes.Public,
                    typeof(object), new Type[] { typeof(IInvokeDirect) });

                var methodBuilder = typeBuilder.DefineMethod("DoInvoke", MethodAttributes.Public | MethodAttributes.Virtual,
                    typeof(object), new Type[] { typeof(object) });

                typeBuilder.DefineMethodOverride(methodBuilder, typeof(IInvokeDirect).GetMethod("DoInvoke"));

                var il = methodBuilder.GetILGenerator();

                if (!method.IsStatic)
                    il.Emit(OpCodes.Ldarg_1);
                int paramindex = 0;
                foreach (var param in method.GetParameters())
                {
                    il.Emit(OpCodes.Ldarg_2);
                    EmitLdcI4(il, paramindex);
                    il.Emit(OpCodes.Ldelem_Ref);
                    EmitObjectCast(il, param.ParameterType);
                    paramindex++;
                }
                il.Emit(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method);
                if (method.ReturnType == typeof(void))
                    il.Emit(OpCodes.Ldnull);
                else if (method.ReturnType.IsValueType)
                    il.Emit(OpCodes.Box, method.ReturnType);
                il.Emit(OpCodes.Ret);

                Type type = typeBuilder.CreateType();
                invokes[sig] = (IInvokeDirect) Activator.CreateInstance(type);
            }

            return invokes[sig].DoInvoke(instance);
        }

        private static void EmitLdcI4(ILGenerator il, int value)
        {
            switch (value)
            {
                case -1: il.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                case 2: il.Emit(OpCodes.Ldc_I4_2); break;
                case 3: il.Emit(OpCodes.Ldc_I4_3); break;
                case 4: il.Emit(OpCodes.Ldc_I4_4); break;
                case 5: il.Emit(OpCodes.Ldc_I4_5); break;
                case 6: il.Emit(OpCodes.Ldc_I4_6); break;
                case 7: il.Emit(OpCodes.Ldc_I4_7); break;
                case 8: il.Emit(OpCodes.Ldc_I4_8); break;
                default:
                    if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
                        il.Emit(OpCodes.Ldc_I4_S, (sbyte) value);
                    else
                        il.Emit(OpCodes.Ldc_I4, value);
                    break;
            }
        }

        private static void EmitObjectCast(ILGenerator il, Type toType)
        {
            if (toType.IsValueType)
                il.Emit(OpCodes.Unbox_Any, toType);
            else
                il.Emit(OpCodes.Castclass, toType);
        }

        public static T ReadPrivateField<T>(object instance, string name)
        {
            return (T) instance.GetType().GetAllFields().First(fi => fi.Name == name).GetValue(instance);
        }

        private static string getSignature(MethodInfo mi)
        {
            if (mi.IsGenericMethod || mi.IsGenericMethodDefinition)
                throw new NotSupportedException("Generic methods not yet supported");
            if (mi == null)
                return "";
            StringBuilder sb = new StringBuilder();

            if (mi.ReturnType == typeof(void))
                sb.Append("void ");
            else
                sb.Append(mi.ReturnType.FullName + " ");

            sb.Append(mi.DeclaringType.FullName + "::" + mi.Name + "(");

            var ps = mi.GetParameters()
              .Select(p => p.ParameterType.FullName).ToArray();
            sb.Append(string.Join(", ", ps));
            sb.Append(")");

            return sb.ToString();
        }
    }
}
