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
                GenerateControllerCode(controller);
            }

            Console.WriteLine("Done!");
        }

        private void GenerateControllerCode(Type controller)
        {
            var classData = new ClassData();
            classData.NamespaceName = _namespaceName;
            classData.ControllerRoute = GetControllerRouteTemplate(controller);
            var methods = controller.GetMethods().Where(c => c.DeclaringType == controller).ToList();
           // classData.ControllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;
            
            GetApiVersion(controller, classData);

            classData.ClassName = controller.Name.Replace("Controller", "Client");


            foreach (var controllerMethod in methods)
            {
                if (HasNoHttpMethodAttributes(controllerMethod))
                {
                    continue;
                }

                GenerateControllerMethod(controllerMethod, classData);
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

        private static void GenerateControllerMethod(MethodInfo controllerMethod, ClassData classData)
        {
            var restCall = new RestCall();

            restCall.ReturnType = "object";
            restCall.ControllerRoute = "";

            GetCallresponseType(controllerMethod, restCall);

            restCall.Name = controllerMethod.Name;

            var parms = new List<(string type, string name)>();
            var usedParms = new List<string>();

            var postParms = new List<CallParameter>();

            foreach (var parameterInfo in controllerMethod.GetParameters())
            {
                CreateBodyParameters(parameterInfo, usedParms, postParms);

                parms.Add((parameterInfo.ParameterType.Name, parameterInfo.Name));
            }


            var methodHttp = controllerMethod.GetCustomAttribute<HttpMethodAttribute>();

            var rawVerb = methodHttp.HttpMethods.First().ToLower();

            if (methodHttp.Template != null)
            {
                CreateUriParameters(methodHttp, restCall, parms, usedParms);
            }

            VerbToTitleCase(restCall, rawVerb);

            CreateQueryStringParameters(parms, usedParms, restCall);

            if (postParms.Any())
            {
                restCall.Parameters.AddRange(postParms);
            }

            // restCall.Parameters.Reverse(); // reverse because body parms should show up last
            restCall.FunctionParameters.AddRange(restCall.Parameters.Where(c => !c.Fixed).ToList());
            classData.Calls.Add(restCall);
        }

        private static void CreateQueryStringParameters(List<(string type, string name)> parms, List<string> usedParms, RestCall restCall)
        {
            foreach (var parm in parms.Where(c => !usedParms.Contains(c.name)))
            {
                restCall.Parameters.Add(new CallParameter
                {
                    ParameterName = parm.name,
                    ParameterType = parm.type,
                    HttpParameterType = "query"
                });
            }
        }

        private static void GetCallresponseType(MethodInfo controllerMethod, RestCall restCall)
        {
            var responseTypeAttrib = controllerMethod.GetCustomAttribute<ProducesResponseTypeAttribute>();
            if (responseTypeAttrib != null)
            {
                restCall.ReturnType = GetFriendlyName(responseTypeAttrib.Type);
            }
        }

        private static string GetControllerRouteTemplate(Type controller)
        {
            return controller.GetCustomAttribute<RouteAttribute>()?.Template;
        }

        private static void GetApiVersion(Type controller, ClassData classData)
        {
            var apiVersionAttrib = controller.GetCustomAttribute<ApiVersionAttribute>();

            if (apiVersionAttrib != null)
            {
                classData.ApiVersion = apiVersionAttrib.Versions.First().ToString();
                classData.ControllerRoute =
                    classData.ControllerRoute.Replace("{version:apiVersion}", classData.ApiVersion);
                classData.ApiVersion = classData.ApiVersion.Replace(".", "_");
            }
        }

        private static bool HasNoHttpMethodAttributes(MethodInfo controllerMethod)
        {
            return controllerMethod.GetCustomAttributesData()
                .All(c => c.AttributeType.BaseType != typeof(HttpMethodAttribute));
        }

        private static void CreateBodyParameters(ParameterInfo parameterInfo, List<string> usedParms,
            List<CallParameter> postParms)
        {
            string postVar;
            if (parameterInfo.GetCustomAttribute<FromBodyAttribute>() != null)
            {
                postVar = parameterInfo.Name;
                usedParms.Add(postVar);
                postParms.Add(new CallParameter
                {
                    ParameterName = postVar,
                    ParameterType = GetFriendlyName(parameterInfo.ParameterType),
                    HttpParameterType = "body"
                });
            }
        }

        private static void CreateUriParameters(HttpMethodAttribute methodHttp, RestCall restCall,
            List<(string type, string name)> parms, List<string> usedParms)
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

        private static void VerbToTitleCase(RestCall restCall, string rawverb)
        {
            CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
            TextInfo textInfo = cultureInfo.TextInfo;

            restCall.Verb = textInfo.ToTitleCase(rawverb);
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