using System.Collections.Generic;

namespace Thorium.Core.MicroServices.ProxyGenerator.Model.Proxies
{
    public class ClassData
    {
        public string ClassName { get; set; }
        public List<string> Includes { get; set; } = new List<string>();
        
        public List<RestCall> Calls { get; set; } = new List<RestCall>();
        public string ControllerRoute { get; set; }
        public string NamespaceName { get; set; }
        public string ApiVersion { get; set; }
        public string EndpointKey { get; set; }
    }
}