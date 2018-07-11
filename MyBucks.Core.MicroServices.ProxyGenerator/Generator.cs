using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DotLiquid;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using MyBucks.Core.MicroServices.ProxyGenerator.DotLiquid.ViewModel;
using MyBucks.Core.MicroServices.ProxyGenerator.Model.Proxies;
using MyBucks.Mvc.Tools;

namespace MyBucks.Core.MicroServices.ProxyGenerator
{
    public class Generator
    {
        private readonly string _language;
        private readonly string _namespaceName;
        private readonly string _outputFolder;

        public Generator(string language, string namespaceName, string outputFolder)
        {
            _language = language;
            _namespaceName = namespaceName;
            _outputFolder = outputFolder;
        }

        public void Generate()
        {

            var asm = Assembly.GetEntryAssembly();
            var controllers = asm.GetTypes().Where(c => c.BaseType == typeof(ApiController)).ToList();

            //  var controllers = GetMatchingTypesInAssembly(asm, c =>  c.IsAssignableFrom(typeof(ControllerBase)));

            if (!Directory.Exists(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
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

            var apiVersionDir = $"{_outputFolder}\\";

            if (!string.IsNullOrWhiteSpace(classData.ApiVersion))
            {
                apiVersionDir = $"{_outputFolder}\\v{classData.ApiVersion}\\";
                Directory.CreateDirectory(apiVersionDir);
            }

            var template = GetTemplateText();

            var generatedCode = Parse(classData, template);

            generatedCode = CleanWhiteSpace(generatedCode);

            var filePath =
                $"{apiVersionDir}{controller.Name.Replace("Controller", "Client")}_v{classData.ApiVersion}.cs";

            File.WriteAllText(filePath, generatedCode);
        }

        private string GetTemplateText()
        {
            string result;
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"MyBucks.Core.MicroServices.ProxyGenerator.Templates.Proxy.{_language}.liquid";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                result = reader.ReadToEnd();
            }

            return result;
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

        private static void GetCallresponseType(MemberInfo controllerMethod, RestCall restCall)
        {
            var responseTypeAttrib = controllerMethod.GetCustomAttribute<ProducesResponseTypeAttribute>();
            if (responseTypeAttrib != null)
            {
                restCall.ReturnType = GetFriendlyName(responseTypeAttrib.Type);
            }
        }

        private static string GetControllerRouteTemplate(MemberInfo controller)
        {
            return controller.GetCustomAttribute<RouteAttribute>()?.Template;
        }

        private static void GetApiVersion(MemberInfo controller, ClassData classData)
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
            if (parameterInfo.GetCustomAttribute<FromBodyAttribute>() == null) return;
            postVar = parameterInfo.Name;
            usedParms.Add(postVar);
            postParms.Add(new CallParameter
            {
                ParameterName = postVar,
                ParameterType = GetFriendlyName(parameterInfo.ParameterType),
                HttpParameterType = "body"
            });
        }

        private static void CreateUriParameters(HttpMethodAttribute methodHttp, RestCall restCall,
            List<(string type, string name)> parms, List<string> usedParms)
        {
            foreach (var uriVars in methodHttp.Template.Split('/'))
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
            var cultureInfo = Thread.CurrentThread.CurrentCulture;
            var textInfo = cultureInfo.TextInfo;

            restCall.Verb = textInfo.ToTitleCase(rawverb);
        }

        private static string CleanWhiteSpace(string generatedCode)
        {
            var result = "";
            foreach (var s in generatedCode.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(s))
                {
                    result += s;
                }
            }

            return result;
        }

        private static string Parse<T>(T model, string template)
        {
            LiquidFunctions.RegisterViewModel(model.GetType());

            var tmpl = Template.Parse(template);


            var result = tmpl.RenderViewModel(model);
            return result;
        }

        private static string GetFriendlyName(Type type)
        {
            var friendlyName = type.Name;
            if (!type.IsGenericType) return friendlyName;
            var iBacktick = friendlyName.IndexOf('`');
            if (iBacktick > 0)
            {
                friendlyName = friendlyName.Remove(iBacktick);
            }

            friendlyName += "<";
            var typeParameters = type.GetGenericArguments();
            for (var i = 0; i < typeParameters.Length; ++i)
            {
                var typeParamName = GetFriendlyName(typeParameters[i]);
                friendlyName += (i == 0 ? typeParamName : "," + typeParamName);
            }

            friendlyName += ">";

            return friendlyName;
        }
    }
}