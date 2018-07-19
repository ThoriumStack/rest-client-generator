using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Validation;

namespace MyBucks.Core.MicroServices.ProxyGenerator
{
    public class GeneratorCommandLineApp
    {
        public bool RunGenerator(string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }
            
            var app = new CommandLineApplication();
            app.Name = "test-tool";
            app.HelpOption("-?|-h|--help");
            
            
            app.Command("generate", (command) =>
            {
                command.Description = "Generate a microservice proxy";
                command.HelpOption("-?|-h|--help");


                var languageArgument = command.Argument("[language]", "Output language: values {csharp, javascript}");
                var namespaceArgument = command.Argument("[namespace]", "The root namespace for the proxy classes.");

                
                
                languageArgument.Validators.Add(new LanguageSupportedValidator());
                
                var outputDirectoryArgument = command.Argument("[output-directory]",
                    "Output path for generated proxies.");
                
                outputDirectoryArgument.Validators.Add(new DirectoryExistsValidator());

                var createProjectOption = command.Option("-p|--generate-project", "Generate a project file",
                    CommandOptionType.NoValue);


                var additionalNugetPackagesOption = command.Option("-np", "Include additional nuget packages",
                    CommandOptionType.MultipleValue);

                var specifyControllerOption = command.Option("-c|--controller", "Generate only clients for specified controllers", CommandOptionType.MultipleValue);
                
                command.OnExecute(() =>
                {
                    var outputDirectory = outputDirectoryArgument.Value;
                    var language = languageArgument.Value;
                    var namespaceName = namespaceArgument.Value; 
                        
                    var generator = new Generator(language, namespaceName, outputDirectory);
                    
                    
                    
                    try
                    {
                        if (createProjectOption.HasValue())
                        {
                            var projectGenerator = new ProjectGenerator(language, namespaceName, outputDirectory);
                            if (additionalNugetPackagesOption.HasValue())
                            {
                                projectGenerator.AddNugetPackages(additionalNugetPackagesOption.Values);
                            }
                                projectGenerator.Generate();
                            
                        }

                        if (specifyControllerOption.HasValue())
                        {
                            generator.SpecifyControllers(specifyControllerOption.Values);
                        }

                        generator.Generate();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        return 1;
                    }
                   
                    return 0;
                });
            });
            
            
            app.OnExecute(() => {
                app.HelpTextGenerator.Generate(app, Console.Out);
                return 0;
            });

            var returnCode = 1;

            try
            {
                returnCode = app.Execute(args);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                
            }

            return true;
        }
        
    }

    public class LanguageSupportedValidator : IArgumentValidator
    {
        public ValidationResult GetValidationResult(CommandArgument argument, ValidationContext context)
        {
            var supportedLanguages = new[] {"csharp", "javascript"};
            if (supportedLanguages.Contains(argument.Value))
            {
                return ValidationResult.Success;
            }
            return new ValidationResult(
            
                $"The language {argument.Value} is not supported",
                new []{argument.Name}
            );
        }
    }

    public class DirectoryExistsValidator : IArgumentValidator
    {
        public ValidationResult GetValidationResult(CommandArgument argument, ValidationContext context)
        {
            var pethExists = Directory.Exists(argument.Value);
            if (pethExists)
            {
                return ValidationResult.Success;
            }

            var argName = argument.Name;
            return new ValidationResult(
            
                $"The path {argument.Value} does not exist",
                new []{argName}
            );
        }
    }
}