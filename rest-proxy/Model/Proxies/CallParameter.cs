﻿namespace rest_proxy.Model.Proxies
{
    public class CallParameter
    {
        public string ParameterType { get; set; }
        public string ParameterName { get; set; }
        public string HttpParameterType { get; set; }
        public bool Fixed { get; set; }
    }
}