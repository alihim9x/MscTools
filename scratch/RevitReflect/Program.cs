using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

class Program
{
    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string simpleName = new AssemblyName(args.Name).Name;
            string path = Path.Combine(@"C:\Program Files\Autodesk\Revit 2025\", simpleName + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
            return null;
        };

        Run();
    }

    static void Run()
    {
        Console.WriteLine("FamilyManager methods:");
        foreach (var m in typeof(FamilyManager).GetMethods().Where(m => m.Name.Contains("Parameter") || m.Name.Contains("Set")))
        {
            Console.WriteLine($"   METHOD: {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
        }
    }
}
