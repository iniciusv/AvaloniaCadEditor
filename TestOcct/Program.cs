using System;
using System.Reflection;
using OcctNet.Wrapper;

class Program {
    static void Main() {
        foreach (var method in typeof(OcctShape).GetMethods()) {
            Console.WriteLine(method.Name);
        }
    }
}
