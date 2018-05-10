using System.Collections.Generic;

namespace rest_proxy.Model.Proxies
{
    public class RestCall
    {
        public string Verb { get; set; }
        public List<CallParameter> Parameters { get; set; } = new List<CallParameter>();
        public string ReturnType { get; set; }
        public string Name { get; set; }
        public string ControllerRoute { get; set; }
        public List<CallParameter> FunctionParameters { get; set; } = new List<CallParameter>();
    }
}