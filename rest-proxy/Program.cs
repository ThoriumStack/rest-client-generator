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
           var generator = new Generator("csharp", "MyBucks.Core.ApiGateway.Insurance");
            try
            {
                generator.Generate();
            }
            catch (ReflectionTypeLoadException e)
            {
                Console.WriteLine($"Loader exceptions: {e.LoaderExceptions.Length}");
                
                foreach (var eLoaderException in e.LoaderExceptions)
                {
                    Console.WriteLine($"Missing library: {eLoaderException.Message}");
                }
                //Console.WriteLine(e);
                throw;
            }

            Console.WriteLine("Done!");
        }
     
    }
}