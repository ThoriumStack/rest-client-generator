using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DotLiquid;
using Thorium.Core.MicroServices.ProxyGenerator.DotLiquid.ViewModel;
using Thorium.Core.MicroServices.ProxyGenerator.Model;

namespace Thorium.Core.MicroServices.ProxyGenerator
{
    public class ProjectGenerator
    {
        private string _namespaceName;
        private readonly string _language;
        private string _outputDirectory;
        private ProjectTemplateModel _projectModel;

        public ProjectGenerator(string language, string namespaceName, string outputDirectory)
        {
            _language = language;
            _namespaceName = namespaceName;
            _outputDirectory = outputDirectory;

            _projectModel = new ProjectTemplateModel();
        }

        public void Generate()
        {
            string result;
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames()
                    .Where(
                        c => c.StartsWith($"Thorium.Core.MicroServices.ProxyGenerator.Templates.Project.{_language}"))
                ;

            foreach (var resourceName in resourceNames)
            {
                Console.WriteLine(resourceName);
                var template = GetResourceText(resourceName);

                var fileName = resourceName.Replace(".liquid", "");
                
                fileName = fileName.Replace($"Thorium.Core.MicroServices.ProxyGenerator.Templates.Project.{_language}.", "");
                fileName = fileName.Replace("ProjectName", _namespaceName);
                fileName = Path.Combine(_outputDirectory, fileName);
                var parsedTemplate = Parse(_projectModel, template);
                File.WriteAllText(fileName, parsedTemplate);
            }
        }
        
        private static string Parse<T>(T model, string template)
        {
            LiquidFunctions.RegisterViewModel(model.GetType());

            var tmpl = Template.Parse(template);


            var result = tmpl.RenderViewModel(model);
            return result;
        }

        private string GetResourceText(string resourceName)
        {
            string result;
            var assembly = Assembly.GetExecutingAssembly();


            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                result = reader.ReadToEnd();
            }

            return result;
        }

        public void AddNugetPackages(List<string> values)
        {
            _projectModel.AddNugetPackages(values);
        }
    }
}