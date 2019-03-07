using System.Collections.Generic;

namespace Thorium.Core.MicroServices.ProxyGenerator.Model
{
    public class ProjectTemplateModel
    {
        public List<PackageInclude> NugetPackages { get; set; } = new List<PackageInclude>();

        public void AddNugetPackages(List<string> values)
        {
            foreach (var value in values)
            {
                if (value.Contains(":"))
                {
                    var version = value.Split(':')[1];
                    var fullName = value.Split(':')[0];
                    NugetPackages.Add(new PackageInclude
                    {
                        FullName = fullName,
                        Version = version
                    });
                }
                else
                {
                    NugetPackages.Add(new PackageInclude
                    {
                        FullName = value,
                        Version = "*"
                    });
                }
            }
        }
    }

    public class PackageInclude
    {
        public string FullName { get; set; }
        public string Version { get; set; }
    }
}