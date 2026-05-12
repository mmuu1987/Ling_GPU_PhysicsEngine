using System;
using System.Reflection;

namespace MassGPUPhysics.Stage7
{
    internal static class UnitTypeTypeResolver
    {
        public static Type Resolve(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return typeof(DefaultSwordUnit);

            Type direct = Type.GetType(className);
            if (direct != null)
                return direct;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(className);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
