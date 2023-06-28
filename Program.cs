using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GeoJSON.Net;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;

namespace API
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Enter the first coordinate (latitude,longitude):");
                string firstCoordinateInput = Console.ReadLine();
                string[] firstCoordinate = firstCoordinateInput.Split(',');

                Console.WriteLine("Enter the second coordinate (latitude,longitude):");
                string secondCoordinateInput = Console.ReadLine();
                string[] secondCoordinate = secondCoordinateInput.Split(',');

                double lat1 = double.Parse(firstCoordinate[0]);
                double lon1 = double.Parse(firstCoordinate[1]);
                double lat2 = double.Parse(secondCoordinate[0]);
                double lon2 = double.Parse(secondCoordinate[1]);

                double midLat = (lat1 + lat2) / 2;
                double midLon = (lon1 + lon2) / 2;

                string overpassQuery = $"[out:json];way(around:10000,{midLat},{midLon});out;";
                string overpassUrl = $"https://lz4.overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";

                // Download GeoJSON data and store it in a JSON file
                string directory = @"D:\c# work\NEA\NEA PRACTICE\NEA OPENSTREETMAP PRACTICE\data2";
                string filePath = Path.Combine(directory, "data.geojson");
                await DownloadAndSaveGeoJson(overpassUrl, filePath);


                // Read the GeoJSON data from the file
                string geoJson = File.ReadAllText(filePath);

                // Deserialize GeoJSON into a dynamic object
                dynamic geoJsonObject = JsonConvert.DeserializeObject<dynamic>(geoJson);

                // Check if the 'type' property is missing and add it if necessary
                if (geoJsonObject.type == null)
                {
                    geoJsonObject.type = "FeatureCollection";
                }

                // Filter for highways (roads) in the features collection
                List<dynamic> filteredFeatures = new List<dynamic>();
                foreach (var feature in geoJsonObject.features ?? Enumerable.Empty<dynamic>())
                {
                    var highwayValue = feature?["properties"]?["highway"];

                    if (highwayValue != null && highwayValue.ToString().ToLower() == "road")
                    {
                        filteredFeatures.Add(feature);
                    }
                }

                // Check if features have been filtered or not
                bool isFiltered = filteredFeatures.Count > 0;
                Console.WriteLine("Filtered Features:");
                if (isFiltered)
                {
                    // Convert the filtered features to a JSON string
                    string filteredGeoJsonString = JsonConvert.SerializeObject(filteredFeatures);
                    Console.WriteLine(filteredGeoJsonString);

                    // Save the filtered GeoJSON to a file
                    string filteredFilePath = Path.Combine(Directory.GetCurrentDirectory(), "filtered-data.geojson");
                    File.WriteAllText(filteredFilePath, filteredGeoJsonString);

                    // Print the path to the filtered GeoJSON file
                    Console.WriteLine("Filtered GeoJSON File Path:");
                    Console.WriteLine(filteredFilePath);
                }
                else
                {
                    Console.WriteLine("No features were filtered.");
                }

                Console.WriteLine();
                Console.ReadKey();

                // Create a new dynamic object with the modified features array
                dynamic modifiedGeoJsonObject = new
                {
                    type = geoJsonObject.type,
                    features = filteredFeatures
                };

                // Convert the modified dynamic object to a JSON string
                string modifiedGeoJsonString = JsonConvert.SerializeObject(modifiedGeoJsonObject);

                // Deserialize the modified GeoJSON string into a FeatureCollection
                FeatureCollection featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(modifiedGeoJsonString);

                // Process GeoJSON and find the shortest path
                Graph graph = ProcessGeoJson(featureCollection);
                List<Vertex> shortestPath = graph.FindShortestPath(new Vertex(lat1, lon1), new Vertex(lat2, lon2));

                // Convert shortest path to GeoJSON LineString
                string grahamScanPath = ConvertToGeoJsonLineString(shortestPath);

                // Create GeoJSON.io URL
                string grahamScanUrl = $"https://geojson.io/#data=data:application/json,{Uri.EscapeDataString(grahamScanPath)}";

                // Print results
                Console.WriteLine("Graham Scan Path:");
                Console.WriteLine(grahamScanPath);
                Console.WriteLine();
                Console.WriteLine("Graham Scan URL:");
                Console.WriteLine(grahamScanUrl);
                Console.ReadKey();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        static async Task DownloadAndSaveGeoJson(string url, string filePath)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                string geoJson = await response.Content.ReadAsStringAsync();
                File.WriteAllText(filePath, geoJson);
            }
        }

        static Graph ProcessGeoJson(FeatureCollection featureCollection)
        {
            Graph graph = new Graph();

            foreach (var feature in featureCollection.Features)
            {
                if (feature.Geometry.Type == GeoJSONObjectType.LineString)
                {
                    LineString lineString = feature.Geometry as LineString;
                    List<Vertex> vertices = new List<Vertex>();

                    foreach (var position in lineString.Coordinates)
                    {
                        double latitude = position.Latitude;
                        double longitude = position.Longitude;

                        Vertex vertex = new Vertex(latitude, longitude);
                        vertices.Add(vertex);

                        graph.AddNode(vertex);
                    }

                    for (int i = 0; i < vertices.Count - 1; i++)
                    {
                        Vertex startVertex = vertices[i];
                        Vertex endVertex = vertices[i + 1];

                        graph.AddEdge(startVertex, endVertex);
                    }
                }
            }

            return graph;
        }

        static string ConvertToGeoJsonLineString(List<Vertex> vertices)
        {
            List<IPosition> lineStringPositions = vertices.Select(vertex => (IPosition)new Position(vertex.Latitude, vertex.Longitude)).ToList();

            LineString lineString = new LineString(lineStringPositions);
            Feature feature = new Feature(lineString);

            return JsonConvert.SerializeObject(feature);
        }
    }

    public class Vertex
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public Vertex(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }
    }

    public class Graph
    {
        private Dictionary<Vertex, List<Vertex>> adjacencyList;

        public Graph()
        {
            adjacencyList = new Dictionary<Vertex, List<Vertex>>();
        }

        public void AddNode(Vertex vertex)
        {
            if (!adjacencyList.ContainsKey(vertex))
            {
                adjacencyList[vertex] = new List<Vertex>();
            }
        }

        public void AddEdge(Vertex vertex1, Vertex vertex2)
        {
            AddNode(vertex1);
            AddNode(vertex2);

            adjacencyList[vertex1].Add(vertex2);
            adjacencyList[vertex2].Add(vertex1);
        }

        public List<Vertex> FindShortestPath(Vertex startVertex, Vertex endVertex)
        {
            Queue<Vertex> queue = new Queue<Vertex>();
            Dictionary<Vertex, Vertex> visited = new Dictionary<Vertex, Vertex>();
            Dictionary<Vertex, int> distances = new Dictionary<Vertex, int>();

            foreach (Vertex v in adjacencyList.Keys.ToList())
            {
                visited[v] = null;
                distances[v] = int.MaxValue;
            }

            visited[startVertex] = startVertex;
            distances[startVertex] = 0;
            queue.Enqueue(startVertex);

            while (queue.Count > 0)
            {
                Vertex currentVertex = queue.Dequeue();

                if (currentVertex == endVertex)
                {
                    break;
                }

                foreach (Vertex neighborVertex in adjacencyList[currentVertex])
                {
                    if (visited[neighborVertex] == null)
                    {
                        visited[neighborVertex] = currentVertex;
                        distances[neighborVertex] = distances[currentVertex] + 1;
                        queue.Enqueue(neighborVertex);
                    }
                }
            }

            if (visited[endVertex] == null)
            {
                return new List<Vertex>();
            }

            List<Vertex> shortestPath = new List<Vertex>();
            Vertex vertex = endVertex;
            while (vertex != startVertex)
            {
                shortestPath.Add(vertex);
                vertex = visited[vertex];
            }
            shortestPath.Add(startVertex);
            shortestPath.Reverse();

            return shortestPath;
        }
    }
}
