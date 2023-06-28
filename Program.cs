using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Code
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Define the OpenStreetMap API endpoint
            string apiEndpoint = "https://api.openstreetmap.org/api/0.6/";

            // Set your OpenStreetMap username and password
            string username = "jamesjulian";
            string password = "usBQmzPJEJ7<";

            // Create a new node
            Dictionary<string, string> tags = new Dictionary<string, string>()
            {
                { "name", "My New Node" },
                { "amenity", "restaurant" }
            };

            string newNodeXml = BuildNewNodeXml(1.2345, 2.3456, tags);

            // Convert the credentials to Base64
            string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));

            // Send the request to the OpenStreetMap API
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
                var content = new StringContent(newNodeXml, Encoding.UTF8, "application/xml");
                var response = await client.PutAsync(apiEndpoint + "node/create", content);
                string responseContent = await response.Content.ReadAsStringAsync();

                // Handle the response
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Node created successfully!");
                    Console.WriteLine("Response Content:");
                    Console.WriteLine(responseContent);
                }
                else
                {
                    Console.WriteLine("Failed to create node!");
                    Console.WriteLine("Response Content:");
                    Console.WriteLine(responseContent);
                }
            }

            Console.ReadKey();
        }

        static string BuildNewNodeXml(double latitude, double longitude, Dictionary<string, string> tags)
        {
            string nodeXml =
                "<?xml version='1.0' encoding='UTF-8'?>\n" +
                "<osm version='0.6' upload='true' generator='Code'>\n" +
                $"<node id='0' lat='{latitude}' lon='{longitude}' changeset='0'>\n";

            foreach (var tag in tags)
            {
                nodeXml += $"<tag k='{tag.Key}' v='{tag.Value}' />\n";
            }

            nodeXml +=
                "</node>\n" +
                "</osm>";

            return nodeXml;
        }
    }
}
