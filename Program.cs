using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GeoJSON.Net;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Newtonsoft.Json;
using System.Linq;
using System.Dynamic;

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

                string overpassQuery = $"[out:json];(node(around:5000,{lat1},{lon1},{lat2},{lon2}); way(around:5000,{lat1},{lon1},{lat2},{lon2})[highway~" ^// (primary | secondary | tertiary | residential | motorway)$"];);out;"; ;
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

                // Process GeoJSON and create a new graph
                Graph graph = ProcessGeoJson(geoJsonObject);

                if (graph != null)
                {
                    // Find the nearest road node for the first coordinate
                    Vertex startVertex = FindNearestRoadNode(graph, new Vertex(lat1, lon1));
                    Console.WriteLine($"Start vertex: {startVertex.Latitude}, {startVertex.Longitude}");

                    // Find the nearest road node for the second coordinate
                    Vertex endVertex = FindNearestRoadNode(graph, new Vertex(lat2, lon2));
                    Console.WriteLine($"End vertex: {endVertex.Latitude}, {endVertex.Longitude}");

                    // Find the closest road node for the first coordinate
                    Console.WriteLine("Finding the closest road node for the first coordinate...");
                    int closestNodeIndex1 = FindClosestNode(graph.NodeLatLons, lat1, lon1);
                    Console.WriteLine($"Closest node index: {closestNodeIndex1}");

                    // Find the closest road node for the second coordinate
                    Console.WriteLine("Finding the closest road node for the second coordinate...");
                    int closestNodeIndex2 = FindClosestNode(graph.NodeLatLons, lat2, lon2);
                    Console.WriteLine($"Closest node index: {closestNodeIndex2}");

                    // Find the shortest path using Dijkstra's algorithm
                    List<Vertex> shortestPath = graph.FindShortestPath(startVertex, endVertex);

                    if (shortestPath.Count > 0)
                    {
                        // Convert shortest path to GeoJSON LineString
                        string shortestPathGeoJson = ConvertToGeoJsonLineString(shortestPath);

                        // Save the shortest path GeoJSON to a file
                        string shortestPathFilePath = Path.Combine(Directory.GetCurrentDirectory(), "shortest-path.geojson");
                        File.WriteAllText(shortestPathFilePath, shortestPathGeoJson);

                        // Reverse geocode the shortest path to get the coordinates for each node
                        Console.WriteLine("Reverse geocoding the shortest path...");
                        List<Vertex> geocodedShortestPath = await ReverseGeocodeShortestPath(shortestPath);

                        // Convert geocoded shortest path to GeoJSON LineString
                        string geocodedShortestPathGeoJson = ConvertToGeoJsonLineString(geocodedShortestPath);


                        dynamic features = new ExpandoObject();
                        List<dynamic> featureList = new List<dynamic>();
                        features.type = "FeatureCollection";
                        {
                            //Start Point 
                            dynamic feature = new ExpandoObject();
                            dynamic geometry = new ExpandoObject();
                            feature.type = "Feature";
                            geometry.type = "Point";
                            feature.geometry = geometry;
                            double[] coords = {startVertex.Longitude , startVertex.Latitude};
                            featureList.Add(feature);

                        }
                        {
                            //End Point 
                            dynamic feature = new ExpandoObject();
                            dynamic geometry = new ExpandoObject();
                            feature.type = "Feature";
                            geometry.type = "Point";
                            feature.geometry = geometry;
                            double[] coords = { endVertex.Longitude, endVertex.Latitude };
                            featureList.Add(feature);

                        }
                        //Graph LINE
                        
                            
                            dynamic featureLINE = new ExpandoObject();
                            dynamic geometryLINE = new ExpandoObject();
                            featureLINE.type = "Feature";
                        geometryLINE.type = "LineString";
                        geometryLINE.coodinates = new List<dynamic>();
                        foreach (var node in shortestPath)
                        {
                            double[] coords = { node.Longitude, node.Latitude };
                            geometryLINE.coodinates.Add(coords);
                        }
                        featureLINE.geometry = geometryLINE;
                        featureList.Add(featureLINE);


                        // Save the shortest path GeoJSON to a file
                        string geocodedShortestPathFilePath = Path.Combine(Directory.GetCurrentDirectory(), "geocoded-shortest-path.geojson");
                        File.WriteAllText(geocodedShortestPathFilePath, geocodedShortestPathGeoJson);

                        // Print the path to the geocoded shortest path GeoJSON file
                        Console.WriteLine("Geocoded Shortest Path GeoJSON File Path:");
                        Console.WriteLine(geocodedShortestPathFilePath);



                        // Open geojson.io with the geocoded shortest path data
                        OpenGeoJsonIO(geocodedShortestPathFilePath);

                        Console.ReadKey();
                    }
                    else
                    {
                        Console.WriteLine("No shortest path found.");
                        Console.ReadKey();
                    }
                }
                else
                {
                    Console.WriteLine("Failed to process GeoJSON data.");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.ReadKey();
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

        static Graph ProcessGeoJson(dynamic geoJsonObject)
        {
            Graph graph = new Graph();

            foreach (var feature in geoJsonObject.features ?? Enumerable.Empty<dynamic>())
            {
                if (feature.geometry?.type == "LineString")
                {
                    List<Road> roads = new List<Road>();

                    foreach (var position in feature.geometry.coordinates)
                    {
                        double latitude = position[1];
                        double longitude = position[0];

                        Vertex vertex = new Vertex(latitude, longitude);
                        graph.AddNode(vertex);

                        roads.Add(new Road(vertex, speedLimit)); // Replace 'speedLimit' with the appropriate value
                    }

                    for (int i = 0; i < roads.Count - 1; i++)
                    {
                        Road startRoad = roads[i];
                        Road endRoad = roads[i + 1];

                        graph.AddEdge(startRoad, endRoad);
                    }
                }
            }

            graph.RemoveDisconnectedNodes();

            return graph;
        }

        static Vertex FindNearestRoadNode(Graph graph, Vertex coordinate)
        {
            // Find the nearest road node by calculating the Euclidean

            Vertex nearestNode = null;
            double minDistance = double.MaxValue;

            foreach (var node in graph.adjacencyList.Keys)
            {
                double distance = graph.CalculateDistance(coordinate, node);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestNode = node;
                }
            }

            return nearestNode;
        }

        static int FindClosestNode(List<double[]> nodeLatLons, double lat, double lon)
        {
            // Find the closest node using haversine node distance

            int closestNodeIndex = 0;
            double closestDistance = ScaledHaversineNodeDistance(lat, lon, nodeLatLons[0][0], nodeLatLons[0][1]);

            for (int i = 1; i < nodeLatLons.Count; i++)
            {
                double distance = ScaledHaversineNodeDistance(lat, lon, nodeLatLons[i][0], nodeLatLons[i][1]);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNodeIndex = i;
                }
            }

            return closestNodeIndex;
        }

        static double ScaledHaversineNodeDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLon = DegreesToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = c * 6371;

            return distance;
        }

        static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Haversine distance between two coordinates

            double R = 6371; // Radius of the Earth(KM)
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLon = DegreesToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = R * c;

            return distance;
        }

        static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        static string ConvertToGeoJsonLineString(List<Vertex> vertices)
        {
            var lineStringPositions = vertices.Select(vertex => new Position(vertex.Latitude, vertex.Longitude)).ToList();
            var lineString = new LineString(lineStringPositions);
            var feature = new Feature(lineString);
            var featureCollection = new FeatureCollection();
            featureCollection.Features.Add(feature);

            var point1 = new Feature(new Point(new Position(vertices.First().Latitude, vertices.First().Longitude)));
            featureCollection.Features.Add(point1);
            return JsonConvert.SerializeObject(featureCollection);
        }
        static async Task<List<Vertex>> ReverseGeocodeShortestPath(List<Road> shortestPath)
        {
            List<Vertex> geocodedPath = new List<Vertex>();
            string apiKey = "5b3ce3597851110001cf624800a0f6d78de048e280faef2746b611d3";

            foreach (var road in shortestPath)
            {
                Vertex vertex = road.Destination;
                string reverseGeocodeUrl = $"https://api.openrouteservice.org/geocode/reverse?api_key={apiKey}&point.lon={vertex.Longitude}&point.lat={vertex.Latitude}";

                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync(reverseGeocodeUrl);
                    response.EnsureSuccessStatusCode();
                    string responseJson = await response.Content.ReadAsStringAsync();
                    dynamic responseObject = JsonConvert.DeserializeObject<dynamic>(responseJson);

                    double lon = responseObject.features[0].geometry.coordinates[0];
                    double lat = responseObject.features[0].geometry.coordinates[1];

                    geocodedPath.Add(new Vertex(lat, lon));
                }
            }

            return geocodedPath;
        }

        static void OpenGeoJsonIO(string filePath)
        {
            // Open geojson.io with the given GeoJSON file path

            string geoJsonIOUrl = $"https://geojson.io/#data=data:application/json,{Uri.EscapeDataString(File.ReadAllText(filePath))}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(geoJsonIOUrl) { UseShellExecute = true });
        }
    }
    public struct Vertex
    {
        public double Latitude { get; }
        public double Longitude { get; }
        public int ID { get; set; } // Add ID property

        public Vertex(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            ID = 0; // Initialize ID to a default value, you can set it later
        }
    }
    public struct Road
    {
        public Vertex Destination { get; }
        public double SpeedLimit { get; }

        public Road(Vertex destination, double speedLimit)
        {
            Destination = destination;
            SpeedLimit = speedLimit;
        }
    }


    // Dijkstra's
    public class Graph
    {
        public Dictionary<Vertex, List<Road>> adjacencyList;
        public List<double[]> NodeLatLons;

        public Graph()
        {
            adjacencyList = new Dictionary<Vertex, List<Road>>();
            NodeLatLons = new List<double[]>();
        }

        public void AddNode(Vertex vertex)
        {
            if (!adjacencyList.ContainsKey(vertex))
            {
                adjacencyList[vertex] = new List<Road>();
                NodeLatLons.Add(new double[] { vertex.Latitude, vertex.Longitude });
                Console.WriteLine($"Added new node: {vertex.Latitude}, {vertex.Longitude}");
            }
            else
            {
                Console.WriteLine($"Node already exists: {vertex.Latitude}, {vertex.Longitude}");
            }
        }

        public void AddEdge(Road road1, Road road2)
        {
            if (!adjacencyList.ContainsKey(road1.Destination))
            {
                AddNode(road1.Destination);
            }

            if (!adjacencyList.ContainsKey(road2.Destination))
            {
                AddNode(road2.Destination);
            }

            adjacencyList[road1.Destination].Add(road2);
            adjacencyList[road2.Destination].Add(road1);

            Console.WriteLine($"Added edge between: {road1.Destination.Latitude}, {road1.Destination.Longitude} and {road2.Destination.Latitude}, {road2.Destination.Longitude}");
        }

        public List<Vertex> FindShortestPath(Vertex startVertex, Vertex endVertex)
        {
            Console.WriteLine($"Start vertex: {startVertex.Latitude}, {startVertex.Longitude}");
            Console.WriteLine($"End vertex: {endVertex.Latitude}, {endVertex.Longitude}");

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

                Console.WriteLine($"Current vertex: {currentVertex?.Latitude}, {currentVertex?.Longitude}");

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

        public double CalculateDistance(Vertex vertex1, Vertex vertex2)
        {
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
