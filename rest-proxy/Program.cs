using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace rest_proxy
{
    class Program
    {
        static void Main(string[] args)
        {
           var generator = new Generator("csharp", "MyBucks.Core.ApiGateway.Product");
            generator.Generate();

            Console.WriteLine("Done!");
        }
        
        
        
        /// <summary>
        /// Get the types within the assembly that match the predicate.
        /// <para>for example, to get all types within a namespace</para>
        /// <para>    typeof(SomeClassInAssemblyYouWant).Assembly.GetMatchingTypesInAssembly(item => "MyNamespace".Equals(item.Namespace))</para>
        /// </summary>
        /// <param name="assembly">The assembly to search</param>
        /// <param name="predicate">The predicate query to match against</param>
        /// <returns>The collection of types within the assembly that match the predicate</returns>
        public static ICollection<Type> GetMatchingTypesInAssembly(Assembly assembly, Predicate<Type> predicate)
        {
            ICollection<Type> types = new List<Type>();
            try
            {
                types = assembly.GetTypes().Where(i => i != null && predicate(i) && i.Assembly == assembly).ToList();
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Type theType in ex.Types)
                {
                    try
                    {
                        if (theType != null && predicate(theType) && theType.Assembly == assembly)
                            types.Add(theType);
                    }
                    // This exception list is not exhaustive, modify to suit any reasons
                    // you find for failure to parse a single assembly
                    catch (BadImageFormatException)
                    {
                        // Type not in this assembly - reference to elsewhere ignored
                    }
                }
            }
            return types;
        }
    }
}