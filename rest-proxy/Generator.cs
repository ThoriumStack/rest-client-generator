using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DotLiquid;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.FileSystemGlobbing;
using MyBucks.Mvc.Tools;
using rest_proxy.DotLiquid.ViewModel;
using rest_proxy.Model.Proxies;

namespace rest_proxy
{
    public class Generator
    {
        private string _language;
        private string _namespaceName;

        public Generator(string language, string namespaceName)
        {
            _language = language;
            _namespaceName = namespaceName;
        }

        public void Generate()
        {
            var asmPath =
                @"C:\projects\mybucks\getsure-api-claims\GetSure.ClaimsServer\bin\Debug\netcoreapp2.1\GetSure.ClaimsServer.dll";

            var asm = Assembly.LoadFrom(asmPath);
            var controllers = asm.GetTypes().Where(c => c.BaseType == typeof(ApiController)).ToList();

            //  var controllers = GetMatchingTypesInAssembly(asm, c =>  c.IsAssignableFrom(typeof(ControllerBase)));

            if (!Directory.Exists("output"))
            {
                Directory.CreateDirectory("output");
            }

            foreach (var controller in controllers)
            {
                
                var classData = new ClassData();
                classData.NamespaceName = _namespaceName;
                classData.ControllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;
                var controllerType = controller;
                var methods = controller.GetMethods().Where(c => c.DeclaringType == controllerType).ToList();
                classData.ControllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;
                var apiVersionAttrib = controller.GetCustomAttribute<ApiVersionAttribute>();

                if (apiVersionAttrib != null)
                {
                    classData.ApiVersion = apiVersionAttrib.Versions.First().ToString();
                    classData.ControllerRoute = classData.ControllerRoute.Replace("{version:apiVersion}", classData.ApiVersion);
                    classData.ApiVersion = classData.ApiVersion.Replace(".", "_");
                }
                
                classData.ClassName = controller.Name.Replace("Controller", "Client");


                foreach (var controllerMethod in methods)
                {
                    if (controllerMethod.GetCustomAttributesData()
                        .Any(c => c.AttributeType.BaseType == typeof(HttpMethodAttribute)))
                    {
                        var restCall = new RestCall();

                        restCall.ReturnType = "object";
                        restCall.ControllerRoute = "";
                        
                        var responseTypeAttrib = controllerMethod.GetCustomAttribute<ProducesResponseTypeAttribute>();
                        if (responseTypeAttrib != null)
                        {
                            restCall.ReturnType = GetFriendlyName(responseTypeAttrib.Type);
                        }

                        restCall.Name = controllerMethod.Name;

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
                                restCall.Parameters.Add(new CallParameter
                                {
                                    ParameterName = postVar,
                                    ParameterType = GetFriendlyName(parameterInfo.ParameterType),
                                    HttpParameterType = "body"
                                });
                            }

                            parms.Add((parameterInfo.ParameterType.Name, parameterInfo.Name));
                        }

                        var parmString = string.Join(",", parms.Select(c => $"{c.type} {c.name}"));

                        var methodHttp = controllerMethod.GetCustomAttribute<HttpMethodAttribute>();

                        var rawverb = methodHttp.HttpMethods.First().ToLower();

                        var uriPaths = new List<string>();

                        if (methodHttp.Template != null)
                        {
                            foreach (var uriVars in methodHttp.Template.Split("/"))
                            {
                                if (uriVars == "")
                                {
                                    continue;
                                }
                                var fixedRoute = !uriVars.Contains("{");
                                var removedBrackets = uriVars.Replace("{", "").Replace("}", "");
                                restCall.Parameters.Add(new CallParameter
                                {
                                    ParameterName = removedBrackets,
                                    ParameterType = parms.FirstOrDefault(c => c.name == removedBrackets).type,
                                    HttpParameterType = "uri",
                                    Fixed = fixedRoute
                                });
                                
                                usedParms.Add(removedBrackets);
                            }
                        }

                        CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
                        TextInfo textInfo = cultureInfo.TextInfo;

                        restCall.Verb = textInfo.ToTitleCase(rawverb);

                        foreach (var parm in parms.Where(c => !usedParms.Contains(c.name)))
                        {
                           
                            restCall.Parameters.Add(new CallParameter
                            {
                                ParameterName = parm.name,
                                ParameterType = parm.type,
                                HttpParameterType = "query"
                            });
                        }

                        restCall.Parameters.Reverse(); // reverse because body parms should show up last
                        restCall.FunctionParameters.AddRange(restCall.Parameters.Where(c=>!c.Fixed).ToList());
                        classData.Calls.Add(restCall);
                    }
                }

                var apiVersionDir = "output\\";
                
                if (!string.IsNullOrWhiteSpace(classData.ApiVersion))
                {
                    apiVersionDir = $"output\\v{classData.ApiVersion}\\";
                    Directory.CreateDirectory(apiVersionDir);
                }

                var generatedCode = Parse(classData, File.ReadAllText("Templates\\proxy\\csharp.liquid"));

                 generatedCode = CleanWhiteSpace(generatedCode);
                
                var filePath =
                    $"{apiVersionDir}{controller.Name.Replace("Controller", "Client")}_v{classData.ApiVersion}.cs";
                
                File.WriteAllText(filePath, generatedCode);
            }


            Console.WriteLine("Done!");
        }

        private string CleanWhiteSpace(string generatedCode)
        {
            var result = "";
            foreach (var s in generatedCode.Split("\n"))
            {
                if (!string.IsNullOrWhiteSpace(s))
                {
                    result += s;
                }
            }

            return result;
        }

        public string Parse<T>(T model, string template)
        {
            LiquidFunctions.RegisterViewModel(model.GetType());

            var tmpl = Template.Parse(template);


            var result = tmpl.RenderViewModel(model);
            return result;
        }

        public static string GetFriendlyName(Type type)
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