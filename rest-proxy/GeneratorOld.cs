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
    public class GeneratorOld
    {
         static void Generate(string[] args)
        {
            var asmPath =
                @"C:\projects\mybucks\getsure-api-product\GetSure.ProductServer\bin\Debug\netcoreapp2.0\GetSure.ProductServer.dll";

            var asm = Assembly.LoadFrom(asmPath);
            var controllers = asm.GetTypes().Where(c => c.BaseType == typeof(ControllerBase)).ToList();

          //  var controllers = GetMatchingTypesInAssembly(asm, c =>  c.IsAssignableFrom(typeof(ControllerBase)));

            if (!Directory.Exists("output"))
            {
                Directory.CreateDirectory("output");
            }

            foreach (var controller in controllers)
            {
                var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;
                var controllerType = controller;
                var methods = controller.GetMethods().Where(c=>c.DeclaringType == controllerType).ToList();

                var classText = $@"using System.Collections.Generic;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using GetSure.ClaimsServer.Models.ServiceContract;
using MyBucks.Core.ApiGateway.ApiClient;
using MyBucks.Core.ApiGateway.ApiClient.Models;
using MyBucks.Core.DataTools.Models;
using MyBucks.Core.Model;
using MyBucks.Services.ClaimsServer.ServiceContract.ServiceContract;

namespace MyBucks.Core.ApiGateway
{{
    public class {controller.Name.Replace("Controller", "Client")}Client : MyBucksApiClient
    {{
        private string _baseUrl;

        public InsuranceClaimsDirectClient(string baseUrl, TokenAuthenticationCredentials tokenAuthenticationCredentials,
            string context) : base(baseUrl, tokenAuthenticationCredentials, context) 
        {{
            _baseUrl = baseUrl;
        }}";
                
                foreach (var controllerMethod in methods)
                {
                    if (controllerMethod.GetCustomAttributesData()
                        .Any(c => c.AttributeType.BaseType == typeof(HttpMethodAttribute)))
                    {
                        var returnType = "object";
                        var responseTypeAttrib = controllerMethod.GetCustomAttribute<ProducesResponseTypeAttribute>();
                        if (responseTypeAttrib != null)
                        {
                            returnType = GetFriendlyName(responseTypeAttrib.Type);
                        }
                        
                        var methodName = controllerMethod.Name;

                        var parms = new List<(string type, string name)>();
                        var parmFlurl = "";
                        var postVar = "";
                        var usedParms = new List<string>();
                        foreach (var parameterInfo in controllerMethod.GetParameters())
                        {
                            if (parameterInfo.GetCustomAttribute<FromBodyAttribute>() != null)
                            {
                                postVar = parameterInfo.Name;
                                usedParms.Add(postVar);
                            }
                            parms.Add((parameterInfo.ParameterType.Name, parameterInfo.Name));
                        }

                        var parmString = string.Join(",", parms.Select(c => $"{c.type} {c.name}"));

                        var methodHttp = controllerMethod.GetCustomAttribute<HttpMethodAttribute>();
                        
                        var verb = methodHttp.HttpMethods.First().ToLower();

                        var uriPaths = new List<string>();
                        
                        if (methodHttp.Template != null)
                        {
                            foreach (var uriVars in methodHttp.Template.Split("/"))
                            {
                                var removedBrackets = uriVars.Replace("{", "").Replace("}", "");
                                usedParms.Add(removedBrackets);
                                uriPaths.Add($"      .AppendPathSegment({removedBrackets})");
                            }
                        }

                        CultureInfo cultureInfo   = Thread.CurrentThread.CurrentCulture;
                        TextInfo textInfo = cultureInfo.TextInfo;

                        verb = textInfo.ToTitleCase(verb);
                        
                        var methodSigStart = $"public async Task<{returnType}> {methodName}({parmString})\n";
                        methodSigStart += "{\n";
                        methodSigStart += $" var result = await GetRequest().AppendPathSegment(\"/{controllerRoute}\")\n";
                        foreach (var uriPath in uriPaths)
                        {
                            methodSigStart += uriPath + "\n";
                        }

                        foreach (var parm in parms.Where(c=>!usedParms.Contains(c.name)))
                        {
                            methodSigStart += $"      .SetQueryParam(\"{parm.name}\", {parm.name})\n";
                        }

                        
                        if (verb.ToLower() == "post" || verb.ToLower() == "patch")
                        {
                            methodSigStart += $"      .{verb}JsonAsync({postVar})\n";
                            methodSigStart += $"      .ReceiveJson<{returnType}>({postVar});\n";
                            
                        }
                        else if (verb.ToLower() == "get")
                        {
                            methodSigStart += $"      .{verb}JsonAsync<{returnType}>({postVar});\n";
                        }
                        
                        else if (verb.ToLower() == "delete")
                        {
                            methodSigStart += $"      .{verb}Async();\n";
                        }

                        methodSigStart += "return result;";
                        
                        methodSigStart += "}\n";
                        Console.WriteLine(methodSigStart);

                        classText += methodSigStart;
                    }
                }

                classText += "\n}\n}\n";
                File.WriteAllText($"output\\{controller.Name.Replace("Controller", "Client")}.cs", classText);
            }
            
            

            Console.WriteLine("Done!");
        }
        
        public static string GetFriendlyName( Type type)
        {
            string friendlyName = type.Name;
            if (type.IsGenericType)
            {
                int iBacktick = friendlyName.IndexOf('`');
                if (iBacktick > 0)
                {
                    friendlyName = friendlyName.Remove(iBacktick);
                }
                friendlyName += "<";
                Type[] typeParameters = type.GetGenericArguments();
                for (int i = 0; i < typeParameters.Length; ++i)
                {
                    string typeParamName = GetFriendlyName(typeParameters[i]);
                    friendlyName += (i == 0 ? typeParamName : "," + typeParamName);
                }
                friendlyName += ">";
            }

            return friendlyName;
        }
    }
}