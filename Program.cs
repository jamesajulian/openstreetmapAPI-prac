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

                string overpassQuery = $"[out:json];way(around:1000,{midLat},{midLon});out;";
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

                List<dynamic> filteredFeatures = new List<dynamic>();
                foreach (var feature in geoJsonObject.features ?? Enumerable.Empty<dynamic>())
                {
                    var highwayValue = feature?["properties"]?["highway"];

                    if (highwayValue != null && IsRoad(highwayValue.ToString()))
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

                    // Print each filtered feature
                    Console.WriteLine("Filtered Features Details:");
                    foreach (var feature in filteredFeatures)
                    {
                        Console.WriteLine($"Type: {feature.type}");
                        Console.WriteLine($"ID: {feature.id}");
                        Console.WriteLine($"Nodes: {string.Join(", ", feature.nodes)}");
                        Console.WriteLine($"Tags: {JsonConvert.SerializeObject(feature.tags)}");
                        Console.WriteLine();
                    }

                    // Process filtered GeoJSON and create a new graph
                    Graph graph = ProcessGeoJson(filteredFeatures);

                    // Find the shortest path using Dijkstra's algorithm
                    List<Vertex> shortestPath = graph.FindShortestPath(new Vertex(lat1, lon1), new Vertex(lat2, lon2));

                    // Convert shortest path to GeoJSON LineString
                    string shortestPathGeoJson = ConvertToGeoJsonLineString(shortestPath);

                    // Save the shortest path GeoJSON to a file
                    string shortestPathFilePath = Path.Combine(Directory.GetCurrentDirectory(), "shortest-path.geojson");
                    File.WriteAllText(shortestPathFilePath, shortestPathGeoJson);

                    // Print the path to the shortest path GeoJSON file
                    Console.WriteLine("Shortest Path GeoJSON File Path:");
                    Console.WriteLine(shortestPathFilePath);
                }
                else
                {
                    Console.WriteLine("No features were filtered.");
                }



                Console.WriteLine();
                Console.ReadKey();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

        }
        private static bool IsRoad(string highwayValue)
        {
            if (string.IsNullOrWhiteSpace(highwayValue))
                return false;

            // Add relevant road types here
            string[] roadTypes = { "residential", "motorway", "trunk" };

            return roadTypes.Contains(highwayValue.ToLower());
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
        static Graph ProcessGeoJson(List<dynamic> filteredFeatures)
        {
            Graph graph = new Graph();

            foreach (var feature in filteredFeatures)
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

            FeatureCollection featureCollection = new FeatureCollection();
            featureCollection.Features.Add(feature);

            return JsonConvert.SerializeObject(featureCollection);
        }
    }

    public class Vertex
    {
        public double Latitude { get; }
        public double Longitude { get; }

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
            Dictionary<Vertex, double> distances = new Dictionary<Vertex, double>();
            Dictionary<Vertex, Vertex> previous = new Dictionary<Vertex, Vertex>();
            HashSet<Vertex> unvisited = new HashSet<Vertex>();

            foreach (var v in adjacencyList.Keys)
            {
                distances[v] = double.MaxValue;
                previous[v] = null;
                unvisited.Add(v);
            }

            distances[startVertex] = 0;

            while (unvisited.Count > 0)
            {
                Vertex currentVertex = null;
                double minDistance = double.MaxValue;

                foreach (var vertex in unvisited)
                {
                    if (distances[vertex] < minDistance)
                    {
                        minDistance = distances[vertex];
                        currentVertex = vertex;
                    }
                }

                if (currentVertex == null)
                {
                    break;
                }

                unvisited.Remove(currentVertex);

                if (currentVertex == endVertex)
                {
                    break;
                }

                foreach (var neighborVertex in adjacencyList[currentVertex])
                {
                    double distance = distances[currentVertex] + CalculateDistance(currentVertex, neighborVertex);

                    if (distance < distances[neighborVertex])
                    {
                        distances[neighborVertex] = distance;
                        previous[neighborVertex] = currentVertex;
                    }
                }
            }

            if (previous[endVertex] == null)
            {
                return new List<Vertex>();
            }

            List<Vertex> shortestPath = new List<Vertex>();
            Vertex pathVertex = endVertex;

            while (pathVertex != null)
            {
                shortestPath.Add(pathVertex);
                pathVertex = previous[pathVertex];
            }

            shortestPath.Reverse();
            return shortestPath;
        }



        private double CalculateDistance(Vertex vertex1, Vertex vertex2)
        {
            // Replace with your distance calculation logic (e.g., Haversine formula)
            // Example:
            double lat1 = vertex1.Latitude;
            double lon1 = vertex1.Longitude;
            double lat2 = vertex2.Latitude;
            double lon2 = vertex2.Longitude;

            double theta = lon1 - lon2;
            double dist = Math.Sin(DegreesToRadians(lat1)) * Math.Sin(DegreesToRadians(lat2))
                          + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
                          * Math.Cos(DegreesToRadians(theta));
            dist = Math.Acos(dist);
            dist = RadiansToDegrees(dist);
            dist = dist * 60 * 1.1515;
            dist = dist * 1.609344; // Convert miles to kilometers

            return dist;
        }

        private double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }
    }
}
