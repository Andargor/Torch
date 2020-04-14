using System;
using System.Net;
using System.Net.Http;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace TorchWizard.Actions
{
    [Verb("queryplugins", HelpText = "Search for plugins on the Torch website.")]
    public class QueryPlugins : VerbBase
    {
        private static readonly WebClient _webClient = new WebClient();
        
        /// <inheritdoc />
        public override void Execute()
        {
            var json = JObject.Parse(_webClient.DownloadString("https://torchapi.net/api/plugins"));

            foreach (var plugin in json["plugins"])
            {
                Console.WriteLine($"{plugin["id"]} - {plugin["name"]}");
            }
        }
    }
}