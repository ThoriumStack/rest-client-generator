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
     
    }
}