﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DotLiquid;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Thorium.Core.MicroServices.ProxyGenerator.DotLiquid.ViewModel;
using Thorium.Core.MicroServices.ProxyGenerator.Model.Proxies;
using Thorium.Mvc.Tools;

namespace Thorium.Core.MicroServices.ProxyGenerator
{
    public class Generator
    {
        private readonly string _language;
        private readonly string _namespaceName;
        private readonly string _outputFolder;
        private readonly string _endpointKey;
        private List<string> _specificControllers = new List<string>();

        public Generator(string language, string namespaceName, string outputFolder, string endpointKey)
        {
            _language = language;
            _namespaceName = namespaceName;
            _outputFolder = outputFolder;
            _endpointKey = endpointKey;
        }

        public void Generate()
        {
            var asm = Assembly.GetEntryAssembly();
            //  var controllers = asm.GetTypes().Where(c => c.BaseType == typeof(ApiController)).ToList();
            // var controllers = asm.GetTypes().Where(c => c.IsAssignableFrom(typeof(ApiController))).ToList();
            var controllers = asm.GetTypes().Where(c => c.IsSubclassOf(typeof(ApiController))).ToList();
            //  var controllers = GetMatchingTypesInAssembly(asm, c =>  c.IsAssignableFrom(typeof(ControllerBase)));

            if (!Directory.Exists(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
            }

            foreach (var controller in controllers.Where(c =>
                _specificControllers.Contains(c.Name) || !_specificControllers.Any()))
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
            classData.EndpointKey = _endpointKey;
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

            var apiVersionDir = $"{_outputFolder}";

            if (!string.IsNullOrWhiteSpace(classData.ApiVersion))
            {
                apiVersionDir = Path.Combine(_outputFolder, $"v{classData.ApiVersion}");
                Directory.CreateDirectory(apiVersionDir);
            }


            var template = GetTemplateText();

            var generatedCode = Parse(classData, template);

            generatedCode = CleanWhiteSpace(generatedCode);
          
            var controllerFileName =
                $"{controller.Name.Replace("Controller", "Client")}_v{classData.ApiVersion}.{GetExtension()}";

            var filePath = Path.Combine(apiVersionDir, controllerFileName);


            File.WriteAllText(filePath, generatedCode);
        }

        private string GetExtension()
        {
            var extLookup = new Dictionary<string, string>
            {
                ["csharp"] = "cs",
                ["javascript"] = "js"
            };
            if (extLookup.ContainsKey(_language))
            {
                return extLookup[_language];
            }

            return ".unknown";
        }

        private string GetTemplateText()
        {
            string result;
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"Thorium.Core.MicroServices.ProxyGenerator.Templates.Proxy.{_language}.liquid";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                result = reader.ReadToEnd();
            }

            return result;
        }

        private static void GenerateControllerMethod(MethodInfo controllerMethod, ClassData classData)
        {
            try
            {
                var restCall = new RestCall();

                restCall.ReturnType = "object";
                restCall.ControllerRoute = "";

                GetCallresponseType(controllerMethod, restCall);

                restCall.Name = controllerMethod.Name;

                var parms = new List<(string type, string name)>();
                var usedParms = new List<string>();

                var postParms = new List<CallParameter>();
                var methodHttp = controllerMethod.GetCustomAttribute<HttpMethodAttribute>();
                if (methodHttp.Template != null)
                {
                    CreateUriParameters(methodHttp, restCall, controllerMethod.GetParameters(), usedParms);
                }

                foreach (var parameterInfo in controllerMethod.GetParameters())
                {
                    CreateBodyParameters(parameterInfo, usedParms, postParms);

                    parms.Add((GetFriendlyName(parameterInfo.ParameterType), parameterInfo.Name));
                }


                var rawVerb = methodHttp.HttpMethods.First().ToLower();
                if (postParms.Any())
                {
                    restCall.Parameters.AddRange(postParms);
                }


                VerbToTitleCase(restCall, rawVerb);

                CreateQueryStringParameters(parms, usedParms, restCall);

                // restCall.Parameters.Reverse(); // reverse because body parms should show up last

                restCall.FunctionParameters.AddRange(restCall.Parameters.Where(c => !c.Fixed).ToList());
                classData.Calls.Add(restCall);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to generate {controllerMethod.Name}");
                throw;
            }
        }

        private static void CreateQueryStringParameters(List<(string type, string name)> parms, List<string> usedParms,
            RestCall restCall)
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
            ParameterInfo[] parms, List<string> usedParms)
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
                    ParameterType = parms.FirstOrDefault(c => c.Name == removedBrackets)?.ParameterType.Name ?? "",
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

        public void SpecifyControllers(List<string> values)
        {
            _specificControllers = values;
        }
    }
}