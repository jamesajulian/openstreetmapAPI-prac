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
using System.Runtime.CompilerServices;

namespace API
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("Enter the first coordinate (latitude,longitude):");
                string firstCoordinateInput = "50.919092926039724, -1.4286166713867294";
                string[] firstCoordinate = firstCoordinateInput.Split(',');

                Console.WriteLine("Enter the second coordinate (latitude,longitude):");
                string secondCoordinateInput = "50.93515348952913, -1.4327822225192743";
                string[] secondCoordinate = secondCoordinateInput.Split(',');

                double lat1 = double.Parse(firstCoordinate[0]);
                double lon1 = double.Parse(firstCoordinate[1]);
                double lat2 = double.Parse(secondCoordinate[0]);
                double lon2 = double.Parse(secondCoordinate[1]);

                string overpassQuery = $"[out:json];(node(around:5000,{lat1},{lon1},{lat2},{lon2}); way(around:5000,{lat1},{lon1},{lat2},{lon2})[highway~\"^(primary|secondary|tertiary|residential|motorway)$\"];);out;";
                //string overpassQuery = $"[out:json];(node(around:1000,{lat1},{lon1},{lat2},{lon2}); way(around:1000,{lat1},{lon1},{lat2},{lon2}););out;";
                string overpassUrl = $"https://lz4.overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";

                // Download OSM data and store it in a JSON file
                string directory = Directory.GetCurrentDirectory();
                string filePath = Path.Combine(directory, "data.geojson");
                await DownloadAndSaveGeoJson(overpassUrl, filePath);

                // Read the OSM data from the file
                string geoJson = File.ReadAllText(filePath);

                // Deserialize OSM into a dynamic object
                dynamic geoJsonObject = JsonConvert.DeserializeObject<dynamic>(geoJson);


                // Check if the 'type' property is missing and add it if necessary
                if (geoJsonObject.type == null)
                {
                    geoJsonObject.type = "FeatureCollection";
                }

                // Process OSM and create a new graph
                Graph graph = ProcessOSM(geoJsonObject);

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
                    long closestNodeIndex1 = graph.FindClosestNode(lat1, lon1);
                    Console.WriteLine($"Closest node index: {closestNodeIndex1}");

                    // Find the closest road node for the second coordinate
                    Console.WriteLine("Finding the closest road node for the second coordinate...");
                    long closestNodeIndex2 = graph.FindClosestNode(lat2, lon2);
                    Console.WriteLine($"Closest node index: {closestNodeIndex2}");

                    // Find the shortest path using Dijkstra's algorithm
                    List<Vertex> shortestPath = graph.FindShortestPath(graph.GetVertexByID(closestNodeIndex1), graph.GetVertexByID(closestNodeIndex2));

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
                            double[] coords = { startVertex.Longitude, startVertex.Latitude };
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

        static Graph ProcessOSM(dynamic geoJsonObject)
        {
            Graph graph = new Graph();

            foreach (var feature in geoJsonObject.elements)
            {
                if (feature.type == "node")
                {
                    Vertex vertex = new Vertex((double)feature.lat, (double)feature.lon);
                    vertex.ID = feature.id;
                    graph.AddNode(vertex);
                }
                if (feature.type == "way")
                {
                    for (int i = 0; i < feature.nodes.Count; i++)
                    {
                        for (int j = i + 1; j < feature.nodes.Count; j++)
                        {
                            if (i == j)
                            {
                                continue;
                            }

                            double speedLimit = 60.0;
                            Vertex node1 = graph.GetVertexByID((long)feature.nodes[i]);
                            Vertex node2 = graph.GetVertexByID((long)feature.nodes[j]);
                            if (node1 != null && node2 != null)
                            {
                                graph.AddEdge(node1, node2, speedLimit); // Add edge between consecutive nodes in the way
                            }
                        }
                    }
                }
            }
            graph.RemoveDisconnectedNodes();
            return graph;
        }


        static Vertex FindNearestRoadNode(Graph graph, Vertex coordinate)
        {
            Vertex nearestNode = graph.GetVertexByID(graph.adjacencyList.Keys.FirstOrDefault()); // Assign an initial value

            double minDistance = double.MaxValue;

            foreach (var node in graph.adjacencyList.Keys)
            {
                Vertex currentVertex = graph.GetVertexByID(node);
                double distance = graph.CalculateDistance(coordinate, currentVertex);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestNode = currentVertex;
                }
            }

            return nearestNode;
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
        static async Task<List<Vertex>> ReverseGeocodeShortestPath(List<Vertex> shortestPath)
        {
            List<Vertex> geocodedPath = new List<Vertex>();
            string apiKey = "5b3ce3597851110001cf624800a0f6d78de048e280faef2746b611d3";

            foreach (var vertex in shortestPath)
            {
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
            string geoJsonIOUrl = $"https://geojson.io/#data=data:application/json,{Uri.EscapeDataString(File.ReadAllText(filePath))}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(geoJsonIOUrl) { UseShellExecute = true });
        }
    }

    public class Vertex
    {
        public double Latitude { get; }
        public double Longitude { get; }
        public long ID { get; set; } // Add ID property

        public Vertex(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            ID = 0; // Initialise ID to a default value, you can set it later
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
        public Dictionary<long, List<Road>> adjacencyList;
        public List<double[]> NodeLatLons;
        public Dictionary<long, Vertex> Vertices;

        public Graph()
        {
            adjacencyList = new Dictionary<long, List<Road>>();
            NodeLatLons = new List<double[]>();
            Vertices = new Dictionary<long, Vertex>();
        }

        public Vertex GetVertexByID(long id)
        {
            if (Vertices.ContainsKey(id)) return Vertices[id];
            else return null;
        }

        public void AddNode(Vertex vertex)
        {
            if (!adjacencyList.ContainsKey(vertex.ID))
            {
                adjacencyList[vertex.ID] = new List<Road>();
                NodeLatLons.Add(new double[] { vertex.Latitude, vertex.Longitude });
                Vertices.Add(vertex.ID, vertex);
            }
            else
            {
                Console.WriteLine($"Node already exists: {vertex.Latitude}, {vertex.Longitude}");
            }
        }

        public void AddEdge(Vertex source, Vertex destination, double speedLimit)
        {
            if (!adjacencyList.ContainsKey(source.ID))
            {
                AddNode(source);
            }

            if (!adjacencyList.ContainsKey(destination.ID))
            {
                AddNode(destination);
            }

            Road roadFromSourceToDestination = new Road(destination, speedLimit);
            Road roadFromDestinationToSource = new Road(source, speedLimit);

            adjacencyList[source.ID].Add(roadFromSourceToDestination);
            adjacencyList[destination.ID].Add(roadFromDestinationToSource);

          
        }

        public void RemoveDisconnectedNodes()
        {
            var connectedNodes = new HashSet<Vertex>();

            foreach (var vertex in adjacencyList.Keys)
            {
                if (adjacencyList[vertex].Count != 0)
                {
                    connectedNodes.Add(Vertices[vertex]); // Storing actual Vertex objects in the set
                }
            }

            List<long> nodesToRemove = new List<long>();
            foreach (var vertex in adjacencyList.Keys)
            {
                if (!connectedNodes.Contains(Vertices[vertex]))
                {
                    nodesToRemove.Add(vertex);
                }
            }

            foreach (var node in nodesToRemove)
            {
                adjacencyList.Remove(node);
                Vertices.Remove(node);
            }
        }


        public List<Vertex> FindShortestPath(Vertex startVertex, Vertex endVertex)
        {
            Dictionary<long, double> distances = new Dictionary<long, double>();
            Dictionary<long, long> previous = new Dictionary<long, long>();
            HashSet<long> unvisited = new HashSet<long>(); 

            foreach (var id in adjacencyList.Keys)
            {
                distances[id] = id == startVertex.ID ? 0 : double.MaxValue;
                previous[id] = -1; // Use -1 to indicate no previous vertex
                unvisited.Add(id);
            }

            while (unvisited.Count > 0)
            {
                long currentVertexID = -1;
                double minDistance = double.MaxValue;

                foreach (var id in unvisited)
                {
                    if (distances[id] < minDistance)
                    {
                        minDistance = distances[id];
                        currentVertexID = id;
                    }
                }

                if (currentVertexID == -1)
                {
                    break;
                }

                unvisited.Remove(currentVertexID);

                if (currentVertexID == endVertex.ID)
                {
                    break;
                }

                foreach (var road in adjacencyList[currentVertexID])
                {
                    double distance = distances[currentVertexID] + CalculateDistance(GetVertexByID(currentVertexID), GetVertexByID(road.Destination.ID));

                    if (distance < distances[road.Destination.ID])
                    {
                        distances[road.Destination.ID] = distance;
                        previous[road.Destination.ID] = currentVertexID;
                    }
                }

            }

           // if (previous[endVertex.ID] == -1)
            //{
              //  return new List<Vertex>();
            //}

            List<Vertex> shortestPath = new List<Vertex>();
            long pathVertexID = endVertex.ID;

            while (pathVertexID != -1)
            {
                shortestPath.Add(GetVertexByID(pathVertexID));
                pathVertexID = previous[pathVertexID];
            }

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

        public double ScaledHaversineNodeDistance(double lat1, double lon1, double lat2, double lon2)
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

        public double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
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
        public long FindClosestNode(double lat, double lon)
        {
            // Find the closest node using haversine node distance

            long closestNodeIndex = 0;
            double closestDistance = double.MaxValue;

            foreach (long VertexID in Vertices.Keys)
            {
                double distance = ScaledHaversineNodeDistance(lat, lon, Vertices[VertexID].Latitude, Vertices[VertexID].Longitude);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNodeIndex = VertexID;
                }
            }

            return closestNodeIndex;
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
