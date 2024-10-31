using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xoshiro.PRNG64;

/// <summary>
///   Contains logic for generating PatchMap objects
/// </summary>
public static class PatchMapGenerator
{
    /// <summary>
    ///   Generates a PatchMap based on the provided world generation settings, default species, and an optional random number generator.
    /// </summary>
    /// <param name="settings">World generation settings containing parameters like AverageTemp.</param>
    /// <param name="defaultSpecies">The default species to populate in the PatchMap.</param>
    /// <param name="random">Optional random number generator. If null, a new one is created using the seed from settings.</param>
    /// <returns>A fully generated PatchMap.</returns>
    public static PatchMap Generate(WorldGenerationSettings settings, Species defaultSpecies, Random? random = null)
    {
        // Initialize the random number generator with the provided seed or a new one if null
        random ??= new XoShiRo256starstar(settings.Seed);

        // Calculate ice shelf probability based on average surface temperature
        float averageTemperature = 8;
        float p = (20.0f - averageTemperature) / 70.0f;
        p = Mathf.Clamp(p, 0.0f, 1.0f); // Ensure p is within [0,1]

        // Debugging: Print the calculated probability
        GD.Print($"Average Surface Temperature: {averageTemperature}°C, Ice Shelf Probability: {p * 100}%");

        // **Updated Line:** Pass 'settings' to the PatchMap constructor
        var map = new PatchMap(settings);

        var nameGenerator = SimulationParameters.Instance.GetPatchMapNameGenerator();

        // Initialize the graph's random parameters
        var regionCoordinates = new List<Vector2>();
        int vertexCount = 9;
        int currentPatchId = 0;

        // Calculate grid size based on vertex count
        int gridSize = Math.Max((int)Math.Ceiling(Math.Sqrt(vertexCount)), 3);
        int maxGridSize = 15; // Define a reasonable maximum grid size to prevent infinite loops

        // Potential starting patches, which must be set by the end of the generating process
        Patch? vents = null;
        Patch? tidepool = null;

        // New data structures to hold the continent and ocean shapes
        var continentShapes = new List<List<Vector2>>();
        var oceanShapes = new List<List<Vector2>>();

        // Flag to ensure at least one HotSpring exists when needed
        bool hotSpringAddedForPond = false;

        // Create the graph's random regions
        for (int i = 0; i < vertexCount; ++i)
        {
            var (continentName, regionName) = nameGenerator.Next(random);
            var coordinates = new Vector2(0, 0);

            PatchRegion.RegionType regionType;

            float humidity = 60; // 0 to 100
            float oceanProbability = Mathf.Clamp(humidity / 100f, 0f, 1f); // Ensure it's between 0 and 1

            if (averageTemperature == -50)
            {
                // When temperature is -50°C, assign first region as Sea, second as Continent, others based on humidity
                if (i == 0)
                {
                    regionType = PatchRegion.RegionType.Sea;
                }
                else if (i == 1)
                {
                    regionType = PatchRegion.RegionType.Continent;
                }
                else
                {
                    // Assign Ocean or Continent based on humidity
                    regionType = (random.NextDouble() < oceanProbability) ? PatchRegion.RegionType.Ocean : PatchRegion.RegionType.Continent;
                }
            }
            else
            {
                {
                    // Assign Ocean or Continent based on humidity
                    regionType = (random.NextDouble() < oceanProbability) ? PatchRegion.RegionType.Ocean : PatchRegion.RegionType.Continent;
                }
            }

            var region = new PatchRegion(i, continentName, regionType, coordinates);
            int numberOfPatches;

            if (regionType == PatchRegion.RegionType.Continent)
            {
                // *** Conditional Patch Creation Based on Temperature and Humidity ***
                if (averageTemperature == -50)
                {
                    // Create IceShelf patch instead of Coastal
                    var iceShelfPatch = NewPredefinedPatch(BiomeType.IceShelf, ++currentPatchId, region, regionName);
                }
                else
                {
                    // Only create Coastal patch if humidity is greater than 0%
                    if (humidity > 0f)
                    {
                        // Create Coastal
                        var coastalPatch = NewPredefinedPatch(BiomeType.Coastal, ++currentPatchId, region, regionName);
                    }
                    else
                    {
                        GD.Print($"Skipping Coastal patch creation for region {i} due to 0% humidity.");
                        // Create Tidepool patch instead of Coastal
                        var tidepoolPatch = NewPredefinedPatch(BiomeType.Tidepool, ++currentPatchId, region, regionName);
                    }
                }   

                // Determine number of patches based on temperature
                numberOfPatches = averageTemperature == -50 ? 0 : random.Next(tidepool == null ? 1 : 0, 3);

                // *** Conditional Patch Assignment for Additional Patches ***
                if (averageTemperature == -50)
                {
                    // No additional IceShelf patches will be created since numberOfPatches is 0
                    // If HotSpring is needed for Pond, ensure it's added outside the loop
                    if (settings.Origin == WorldGenerationSettings.LifeOrigin.Pond && !hotSpringAddedForPond)
                    {
                        var hotSpringPatch = NewPredefinedPatch(BiomeType.HotSpring, ++currentPatchId, region, regionName);
                        var aquiferPatch = NewPredefinedPatch(BiomeType.Aquifer, ++currentPatchId, region, regionName);
                        region.HotSpringAquiferPairs.Add((hotSpringPatch, aquiferPatch));
                        hotSpringAddedForPond = true;
                    }
                }
                else
                {
                    using var availableBiomes =
                        new[] { BiomeType.Estuary, BiomeType.Tidepool, BiomeType.HotSpring }.OrderBy(_ => random.Next()).GetEnumerator();

                    while (numberOfPatches > 0)
                    {
                        // Add at least one tidepool to the map, otherwise choose randomly
                        availableBiomes.MoveNext();
                        var biomeType = availableBiomes.Current;
                        var patch = NewPredefinedPatch(biomeType, ++currentPatchId, region, regionName);
                        --numberOfPatches;

                        if (biomeType == BiomeType.Tidepool)
                            tidepool ??= patch;

                        if (biomeType == BiomeType.HotSpring)
                        {
                            // Add an aquifer patch underneath the hot spring
                            var aquiferPatch = NewPredefinedPatch(BiomeType.Aquifer, ++currentPatchId, region, regionName);
                            // Store the hot spring and aquifer pair in the region
                            region.HotSpringAquiferPairs.Add((patch, aquiferPatch));
                        }
                    }

                    // If there's no tidepool, add one
                    if (averageTemperature != -50)
                    {
                        tidepool ??= NewPredefinedPatch(BiomeType.Tidepool, ++currentPatchId, region, regionName);
                    }
                }

                // *** Ensure HotSpring Patch Exists When Temperature is -50°C and LifeOrigin is Pond ***
                if (averageTemperature == -50 && settings.Origin == WorldGenerationSettings.LifeOrigin.Pond && !hotSpringAddedForPond)
                {
                    // Add a HotSpring patch
                    var hotSpringPatch = NewPredefinedPatch(BiomeType.HotSpring, ++currentPatchId, region, regionName);
                    
                    // Add an Aquifer patch beneath the HotSpring
                    var aquiferPatch = NewPredefinedPatch(BiomeType.Aquifer, ++currentPatchId, region, regionName);
                    region.HotSpringAquiferPairs.Add((hotSpringPatch, aquiferPatch));
                    
                    hotSpringAddedForPond = true;

                    GD.Print("A HotSpring patch has been added to ensure availability for LifeOrigin.Pond with -50°C temperature.");
                }
            }
            else
            {
                // *** Modified Ocean/Sea Region Patch Assignment Start ***
                // Decide whether to add an IceShelf or Epipelagic patch based on probability p
                BiomeType initialBiome = (random.NextDouble() < p) ? BiomeType.IceShelf : BiomeType.Epipelagic;
                NewPredefinedPatch(initialBiome, ++currentPatchId, region, regionName);

                // Define mid-ocean biomes excluding Epipelagic and Seafloor
                var midOceanBiomes = new List<BiomeType>
                {
                    BiomeType.Mesopelagic,
                    BiomeType.Bathypelagic,
                    BiomeType.Abyssopelagic,
                };

                // Determine the number of additional patches
                numberOfPatches = random.Next(0, 4); // Adjusted max to prevent excessive patches

                // Add patches between surface and seafloor
                for (int j = 0; j < numberOfPatches && midOceanBiomes.Count > 0; ++j)
                {
                    var biomeType = midOceanBiomes[0];
                    NewPredefinedPatch(biomeType, ++currentPatchId, region, regionName);
                    midOceanBiomes.RemoveAt(0);
                }

                // Check if the region has any Abyssopelagic patches
                bool hasAbyssopelagic = region.Patches.Any(p => p.BiomeType == BiomeType.Abyssopelagic);

                if (hasAbyssopelagic)
                {
                    // 100% chance to add Hadopelagic instead of Seafloor
                    NewPredefinedPatch(BiomeType.Hadopelagic, ++currentPatchId, region, regionName);
                }
                else
                {
                    // Otherwise, add Seafloor as usual
                    NewPredefinedPatch(BiomeType.Seafloor, ++currentPatchId, region, regionName);
                }

                // Add vents with appropriate probability
                if (vents == null || random.Next(0, 2) == 1)
                {
                    var patch = NewPredefinedPatch(BiomeType.Vents, ++currentPatchId, region, regionName);
                    vents ??= patch;
                }

                // Add Seamount if Mesopelagic is present
                var mesopelagicPatch = region.Patches.FirstOrDefault(p => p.BiomeType == BiomeType.Mesopelagic);
                if (mesopelagicPatch != null && random.Next(0, 2) == 1) // 50% chance
                {
                    NewPredefinedPatch(BiomeType.Seamount, ++currentPatchId, region, regionName);
                }
                // *** Modified Ocean/Sea Region Patch Assignment End ***
            }

            // Random chance to create a cave
            if (random.Next(0, 2) == 1)
            {
                NewPredefinedPatch(BiomeType.Cave, ++currentPatchId, region, regionName);
            }

            BuildRegionSize(region);
            coordinates = GenerateCoordinates(region, map, random, ref gridSize);

            // We add the coordinates for the center of the region
            // since that's the point that will be connected
            regionCoordinates.Add(coordinates + region.Size / 2.0f);
            map.AddRegion(region);

            // Generate the shape for this region
            var regionShape = GenerateRegionShape(region, random);

            if (region.Type == PatchRegion.RegionType.Continent)
            {
                continentShapes.Add(regionShape);
            }
            else if (region.Type == PatchRegion.RegionType.Sea || region.Type == PatchRegion.RegionType.Ocean)
            {
                oceanShapes.Add(regionShape);
            }
        }

        // After building patches in regions
        BuildPatchesInRegions(map, random);

        // Validate that vents and tidepool (if applicable) have been created
        // if (vents == null)
            // throw new InvalidOperationException($"No vent patch created for seed {settings.Seed}");

        // if (settings.Origin != WorldGenerationSettings.LifeOrigin.Pond && tidepool == null)
            // throw new InvalidOperationException($"No tidepool patch created for seed {settings.Seed}");

        // Configure the starting patch based on origin
        ConfigureStartingPatch(map, settings, defaultSpecies, vents, tidepool, random);

        // *** New Logic Start: Set CurrentPatch to HotSpring if LifeOrigin is Pond and Temperature is -50°C ***
        if (averageTemperature == -50 && settings.Origin == WorldGenerationSettings.LifeOrigin.Pond)
        {
            // Find a HotSpring patch
            var hotSpringPatch = map.Patches.Values.FirstOrDefault(p => p.BiomeType == BiomeType.HotSpring);

            if (hotSpringPatch != null)
            {
                map.CurrentPatch = hotSpringPatch;
                GD.Print("map.CurrentPatch has been set to a HotSpring patch due to LifeOrigin.Pond and -50°C temperature.");
            }
            else
            {
                GD.PrintErr("No HotSpring patch found to set as CurrentPatch despite LifeOrigin.Pond and -50°C temperature.");
            }
        }
        // *** New Logic End ***

        // Calculate the maximum and minimum number of edges
        int maxEdges = vertexCount * (vertexCount - 1) / 2;
        int minEdges = vertexCount - 1;

        int edgeCount;

        // Handle small vertex counts
        if (maxEdges <= minEdges)
        {
            edgeCount = minEdges;
        }
        else
        {
            edgeCount = random.Next(minEdges, maxEdges + 1);
        }

        // We make the graph by subtracting edges from its Delaunay Triangulation
        // as long as the graph stays connected.
        var graph = new bool[vertexCount, vertexCount];

        if (vertexCount == 2)
        {
            // Directly connect the two regions
            graph[0, 1] = true;
            graph[1, 0] = true;
        }
        else
        {
            // Create Delaunay Triangulation
            DelaunayTriangulation(ref graph, regionCoordinates);
            // Subtract edges to reach desired edge count
            SubtractEdges(ref graph, vertexCount, edgeCount, random);
        }

        bool[] visited;

        // After SubtractEdges, check if the graph is connected
        if (!IsGraphConnected(graph, vertexCount, out visited))
        {
            // Implement logic to connect the disconnected components
            // Connect all unvisited vertices to a visited vertex (vertex 0 in this case)
            for (int i = 0; i < vertexCount; i++)
            {
                if (!visited[i])
                {
                    // Connect this vertex to a visited vertex (vertex 0 in this case)
                    graph[0, i] = true;
                    graph[i, 0] = true;
                }
            }
        }

        // Now, link regions according to the adjusted graph
        for (int i = 0; i < vertexCount; ++i)
        {
            for (int k = 0; k < vertexCount; ++k)
            {
                if (graph[k, i])
                    LinkRegions(map.Regions[i], map.Regions[k]);
            }
        }

        ConnectPatchesBetweenRegions(map, random);
        map.CreateAdjacenciesFromPatchData();

        // Store the shapes in the PatchMap
        map.ContinentShapes = continentShapes;
        map.OceanShapes = oceanShapes;

        // Fix up initial average sunlight values. Note that in the future if there are things that update the biome
        // available sunlight in a patch, this needs to be done again
        float daytimeMultiplier = DayNightCycle.CalculateDayTimeMultiplier(settings.DaytimeFraction);
        foreach (var entry in map.Patches)
        {
            entry.Value.UpdateAverageSunlight(DayNightCycle.CalculateAverageSunlight(daytimeMultiplier, settings));
        }

        return map;
    }

    private static Biome GetBiomeTemplate(string name)
    {
        return SimulationParameters.Instance.GetBiome(name);
    }

    private static void LinkPatches(Patch patch1, Patch patch2)
    {
        patch1.AddNeighbour(patch2);
        patch2.AddNeighbour(patch1);

        var region1 = patch1.Region;
        var region2 = patch2.Region;

        region1.AddPatchAdjacency(region2, patch2);
        region2.AddPatchAdjacency(region1, patch1);
    }

    private static void LinkRegions(PatchRegion region1, PatchRegion region2)
    {
        region1.AddNeighbour(region2);
        region2.AddNeighbour(region1);
    }

    /// <summary>
    ///   Creates a triangulation for a certain graph given some vertex coordinates
    /// </summary>
    private static void DelaunayTriangulation(ref bool[,] graph, List<Vector2> vertexCoordinates)
    {
        var triangles = Geometry2D.TriangulateDelaunay(vertexCoordinates.ToArray());
        for (var i = 0; i < triangles.Length; i += 3)
        {
            graph[triangles[i], triangles[i + 1]] = graph[triangles[i + 1], triangles[i]] = true;
            graph[triangles[i + 1], triangles[i + 2]] = graph[triangles[i + 2], triangles[i + 1]] = true;
            graph[triangles[i], triangles[i + 2]] = graph[triangles[i + 2], triangles[i]] = true;
        }
    }

    private static void SubtractEdges(ref bool[,] graph, int vertexCount, int edgeCount, Random random)
    {
        var currentEdgeCount = CurrentEdgeNumber(ref graph, vertexCount);

        // Subtract edges until we reach the desired edge count.
        while (currentEdgeCount > edgeCount)
        {
            int edgeToDelete = random.Next(1, currentEdgeCount + 1);
            int i;
            int k;

            for (i = 0, k = 0; i < vertexCount && edgeToDelete != 0; ++i)
            {
                for (k = 0; edgeToDelete != 0 && k < i; ++k)
                {
                    if (graph[i, k])
                        --edgeToDelete;
                }
            }

            // Compensate for the ++i, ++k at the end of the loop
            --i;
            --k;

            // Check if the graph stays connected after subtracting the edge
            // otherwise, leave the edge as is.
            graph[i, k] = graph[k, i] = false;

            if (!CheckConnectivity(ref graph, vertexCount))
            {
                graph[i, k] = graph[k, i] = true;
            }
            else
            {
                currentEdgeCount -= 1;
            }
        }
    }

    /// <summary>
    ///   DFS graph traversal to get all connected nodes
    /// </summary>
    private static void DepthFirstGraphTraversal(ref bool[,] graph, int vertexCount, int point, ref bool[] visited)
    {
        visited[point] = true;

        for (int i = 0; i < vertexCount; ++i)
        {
            if (graph[point, i] && !visited[i])
                DepthFirstGraphTraversal(ref graph, vertexCount, i, ref visited);
        }
    }

    /// <summary>
    ///   Checks the graph's connectivity
    /// </summary>
    private static bool CheckConnectivity(ref bool[,] graph, int vertexCount)
    {
        bool[] visited = new bool[vertexCount];
        DepthFirstGraphTraversal(ref graph, vertexCount, 0, ref visited);
        return visited.Count(v => v) == vertexCount;
    }

    /// <summary>
    ///   Counts the current number of edges in a given graph
    /// </summary>
    private static int CurrentEdgeNumber(ref bool[,] graph, int vertexCount)
    {
        int edgeNumber = 0;
        for (int i = 0; i < vertexCount; ++i)
        {
            for (int k = 0; k < i; ++k)
            {
                if (graph[i, k])
                    ++edgeNumber;
            }
        }

        return edgeNumber;
    }

    /// <summary>
    ///   Checks distance between regions
    /// </summary>
    /// <returns>Returns true if the regions are far away enough (don't overlap)</returns>
    private static bool CheckRegionDistance(PatchRegion region, PatchMap map, int minDistance)
    {
        foreach (var otherRegion in map.Regions.Values)
        {
            if (ReferenceEquals(region, otherRegion))
                continue;

            if (CheckIfRegionsIntersect(region, otherRegion))
                return false;
        }

        return true;
    }

    private static bool CheckIfRegionsIntersect(PatchRegion region1, PatchRegion region2)
    {
        var region1Rect = new Rect2(region1.ScreenCoordinates, region1.Size);
        var region2Rect = new Rect2(region2.ScreenCoordinates, region2.Size);
        return region1Rect.Intersects(region2Rect, true);
    }

    private static Vector2 GenerateCoordinates(PatchRegion region, PatchMap map, Random random, ref int gridSize)
    {
        int spacing = 300; // Adjust spacing between regions
        int offset = 100;  // Offset to position regions away from the edges
        int maxGridSize = 15; // Define a reasonable maximum grid size to prevent infinite loops

        while (gridSize <= maxGridSize)
        {
            var positions = new List<Vector2>();

            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    positions.Add(new Vector2(2 * x * spacing + offset, y * spacing + offset));
                }
            }

            // Shuffle the positions
            positions = positions.OrderBy(_ => random.Next()).ToList();

            // Try each position
            foreach (var coordinate in positions)
            {
                region.ScreenCoordinates = coordinate;
                if (CheckRegionDistance(region, map, 0)) // No minimum distance needed
                {
                    return coordinate;
                }
            }

            // If no suitable position found, increase gridSize and retry
            gridSize++;
            GD.Print($"Grid size increased to {gridSize} to accommodate region placement.");
        }

        // If gridSize exceeds maxGridSize, throw an exception
        throw new InvalidOperationException("Could not place region in grid after increasing grid size.");
    }

    private static List<Vector2> GenerateRegionShape(PatchRegion region, Random random)
    {
        // Generate a random polygon around the region's center
        var shape = new List<Vector2>();
        var center = region.ScreenCoordinates + region.Size / 2.0f;
        int points = random.Next(5, 9); // Number of vertices in the polygon
        float radius = Math.Min(region.Width, region.Height) / 2.0f;

        // Ensure radius is sufficient
        const float minRadius = 50f; // Adjust as needed
        radius = Math.Max(radius, minRadius);

        for (int i = 0; i < points; i++)
        {
            float angle = i * 2 * Mathf.Pi / points + (float)random.NextDouble() * 0.5f;
            float distance = radius * (0.7f + (float)random.NextDouble() * 0.6f);
            var point = new Vector2(
                center.X + distance * Mathf.Cos(angle),
                center.Y + distance * Mathf.Sin(angle)
            );
            shape.Add(point);
        }

        return shape;
    }

    private static bool IsWaterPatch(Patch patch)
    {
        return patch.BiomeType != BiomeType.Cave &&
               patch.BiomeType != BiomeType.Vents &&
               patch.BiomeType != BiomeType.Seamount &&
               patch.BiomeType != BiomeType.Aquifer;
    }

    private static void BuildPatchesInRegion(PatchRegion region, Random random)
    {
        // For now minimum sunlight is always 0
        foreach (var regionPatch in region.Patches)
        {
            regionPatch.Biome.MinimumCompounds[Compound.Sunlight] = new BiomeCompoundProperties
            {
                Ambient = 0,
            };
        }

        // Base vectors to simplify later calculation
        var topLeftPatchPosition = region.ScreenCoordinates + new Vector2(
            Constants.PATCH_AND_REGION_MARGIN + 0.5f * Constants.PATCH_REGION_BORDER_WIDTH,
            Constants.PATCH_AND_REGION_MARGIN + 0.5f * Constants.PATCH_REGION_BORDER_WIDTH);
        var offsetHorizontal = new Vector2(Constants.PATCH_NODE_RECT_LENGTH + Constants.PATCH_AND_REGION_MARGIN, 0);
        var offsetVertical = new Vector2(0, Constants.PATCH_NODE_RECT_LENGTH + Constants.PATCH_AND_REGION_MARGIN);

        // Patch linking first
        switch (region.Type)
        {
            case PatchRegion.RegionType.Sea or PatchRegion.RegionType.Ocean:
            {
                var cave = region.Patches.FirstOrDefault(p => p.BiomeType == BiomeType.Cave);
                int caveLinkedTo = -1;
                var vents = region.Patches.FirstOrDefault(p => p.BiomeType == BiomeType.Vents);
                var seamount = region.Patches.FirstOrDefault(p => p.BiomeType == BiomeType.Seamount);
                int seamountLinkedTo = -1;

                var waterPatches = region.Patches.Where(p => IsWaterPatch(p)).ToList();

                int waterPatchCount = waterPatches.Count;

                // Ensure there are enough water patches to proceed
                if (waterPatchCount < 2)
                {
                    GD.PrintErr($"Not enough water patches to establish seafloor and deepest sea patch.");
                    // Optionally, you can decide to skip further processing for this region
                    break;
                }

                // Link water patches
                for (int i = 0; i < waterPatchCount - 1; ++i)
                {
                    LinkPatches(waterPatches[i], waterPatches[i + 1]);
                }

                if (cave != null)
                {
                    // Ensure there's at least one water patch to link to
                    if (waterPatchCount - 1 > 0)
                    {
                        caveLinkedTo = random.Next(0, waterPatchCount - 1);
                        LinkPatches(cave, waterPatches[caveLinkedTo]);
                    }
                    else
                    {
                        GD.PrintErr($"Not enough water patches to link Cave patch.");
                    }
                }

                if (seamount != null)
                {
                    var mesopelagicIndex = waterPatches.FindIndex(p => p.BiomeType == BiomeType.Mesopelagic);
                    if (mesopelagicIndex != -1)
                    {
                        seamountLinkedTo = mesopelagicIndex;
                        LinkPatches(seamount, waterPatches[mesopelagicIndex]);

                        // Set depth of Seamount same as Mesopelagic
                        seamount.Depth[0] = waterPatches[mesopelagicIndex].Depth[0];
                        seamount.Depth[1] = waterPatches[mesopelagicIndex].Depth[1];
                    }
                    else
                    {
                        // Mesopelagic not found, cannot link Seamount
                        // Remove Seamount from region patches
                        region.Patches.Remove(seamount);
                        seamount = null;
                        GD.Print($"Mesopelagic patch not found. Seamount removed.");
                    }
                }

                // Position water patches
                for (int i = 0; i < waterPatchCount; ++i)
                {
                    waterPatches[i].ScreenCoordinates = topLeftPatchPosition + i * offsetVertical;
                }

                // Collect special patches
                var specialPatches = new List<(Patch patch, int linkedToIndex)>();

                if (vents != null)
                {
                    // Find the index of the seafloor patch
                    var seafloorIndex = waterPatches.FindIndex(p => p.BiomeType == BiomeType.Seafloor);
                    if (seafloorIndex != -1)
                    {
                        specialPatches.Add((vents, seafloorIndex));

                        // Link vents to the seafloor patch
                        LinkPatches(vents, waterPatches[seafloorIndex]);
                    }
                    else
                    {
                        // No seafloor found, cannot link vents
                        // Remove vents from region patches
                        region.Patches.Remove(vents);
                        vents = null;
                        GD.Print($"Seafloor patch not found. Vents removed.");
                    }
                }

                if (cave != null)
                {
                    specialPatches.Add((cave, caveLinkedTo));
                }

                if (seamount != null)
                {
                    specialPatches.Add((seamount, seamountLinkedTo));
                }

                if (specialPatches.Count > 0)
                {
                    bool specialPatchesToTheRight = random.Next(2) == 1;

                    // If the special patches are on the left we need to adjust the water patches' position
                    if (!specialPatchesToTheRight)
                    {
                        for (int i = 0; i < waterPatchCount; ++i)
                        {
                            waterPatches[i].ScreenCoordinates += offsetHorizontal;
                        }
                    }

                    // Group special patches by their linked water patch index
                    var specialPatchesByLinkedIndex = specialPatches.GroupBy(sp => sp.linkedToIndex);

                    foreach (var group in specialPatchesByLinkedIndex)
                    {
                        int i = 0;
                        foreach (var (patch, linkedToIndex) in group)
                        {
                            // Adjust the vertical position to prevent overlap
                            var additionalVerticalOffset = i * offsetVertical;

                            // Position the special patch
                            patch.ScreenCoordinates = waterPatches[linkedToIndex].ScreenCoordinates
                                + (specialPatchesToTheRight ? 1 : -1) * offsetHorizontal
                                + additionalVerticalOffset;

                            // Set depth same as linked patch
                            patch.Depth[0] = waterPatches[linkedToIndex].Depth[0];
                            patch.Depth[1] = waterPatches[linkedToIndex].Depth[1];

                            i++;
                        }
                    }
                }

                // Ensure there are enough patches to set depths
                if (waterPatchCount < 2)
                {
                    GD.PrintErr($"Not enough water patches to set depths.");
                    break;
                }

                // Random depth for water regions
                var deepestSeaPatch = waterPatches[waterPatchCount - 2];
                var seafloor = waterPatches[waterPatchCount - 1];
                var depth = deepestSeaPatch.Depth;

                // Ensure that depth[0] + 1 <= depth[1] - 10 to avoid invalid ranges
                if (depth[0] + 1 >= depth[1] - 10)
                {
                    GD.PrintErr($"Invalid depth range for deepest sea patch.");
                    // Assign default values or adjust accordingly
                    deepestSeaPatch.Depth[1] = depth[0] + 10;
                    seafloor.Depth[0] = deepestSeaPatch.Depth[1];
                    seafloor.Depth[1] = deepestSeaPatch.Depth[1] + 10;
                }
                else
                {
                    deepestSeaPatch.Depth[1] = random.Next(depth[0] + 1, depth[1] - 10);
                    seafloor.Depth[0] = deepestSeaPatch.Depth[1];
                    seafloor.Depth[1] = deepestSeaPatch.Depth[1] + 10;
                }

                // Build seafloor light, using 0m -> 1, 200m -> 0.01, floor to 0.01
                if (seafloor.Biome.ChangeableCompounds.TryGetValue(Compound.Sunlight, out var sunlightProperty))
                {
                    var sunlightAmount = MathF.Pow(0.977237220956f, seafloor.Depth[1]);
                    sunlightAmount = MathF.Round(sunlightAmount * 100) / 100.0f; // Round to 2 decimal places
                    sunlightProperty.Ambient = sunlightAmount;

                    seafloor.Biome.ChangeableCompounds[Compound.Sunlight] = sunlightProperty;
                }
                else
                {
                    GD.PrintErr($"Seafloor patch missing Sunlight compound.");
                }

                // Average sunlight is updated later

                break;
            }

            case PatchRegion.RegionType.Continent:
            {
                var cave = region.Patches.FirstOrDefault(p => p.BiomeType == BiomeType.Cave);
                int caveLinkedTo = -1;

                var waterPatches = region.Patches.Where(p => IsWaterPatch(p)).ToList();
                int waterPatchCount = waterPatches.Count;

                // Ensure there are enough water patches to proceed
                if (waterPatchCount < 1)
                {
                    GD.PrintErr($"Not enough water patches to establish links.");
                    break;
                }

                // Link water patches together
                for (int i = 0; i < waterPatchCount - 1; ++i)
                {
                    LinkPatches(waterPatches[i], waterPatches[i + 1]);
                }

                if (cave != null)
                {
                    // Ensure there's at least one water patch to link to
                    if (waterPatchCount > 0)
                    {
                        caveLinkedTo = random.Next(0, waterPatchCount);
                        LinkPatches(cave, waterPatches[caveLinkedTo]);

                        cave.Depth[0] = waterPatches[caveLinkedTo].Depth[0];
                        cave.Depth[1] = waterPatches[caveLinkedTo].Depth[1];
                    }
                    else
                    {
                        GD.PrintErr($"Not enough water patches to link Cave patch.");
                    }
                }

                // Link hot springs and aquifers
                foreach (var pair in region.HotSpringAquiferPairs)
                {
                    // Link hot spring to all water patches
                    foreach (var waterPatch in waterPatches)
                    {
                        LinkPatches(pair.HotSpring, waterPatch);
                    }

                    // Link aquifer to hot spring
                    LinkPatches(pair.HotSpring, pair.Aquifer);
                }

                // Position water patches
                int waterRowColumns = waterPatchCount > 2 ? (int)Math.Ceiling(waterPatchCount / 2.0) : waterPatchCount;
                int waterRowRows = waterPatchCount > 2 ? 2 : 1;

                for (int i = 0; i < waterPatchCount; ++i)
                {
                    int row = waterPatchCount > 2 ? i / waterRowColumns : 0;
                    int column = waterPatchCount > 2 ? i % waterRowColumns : i;

                    waterPatches[i].ScreenCoordinates = topLeftPatchPosition
                        + new Vector2(column * offsetHorizontal.X, row * offsetVertical.Y);
                }

                // Position hot springs and aquifers
                int hotSpringRowStart = waterRowRows;
                for (int i = 0; i < region.HotSpringAquiferPairs.Count; ++i)
                {
                    var hotSpring = region.HotSpringAquiferPairs[i].HotSpring;
                    var aquifer = region.HotSpringAquiferPairs[i].Aquifer;

                    // Position hot spring
                    hotSpring.ScreenCoordinates = topLeftPatchPosition
                        + new Vector2(i * offsetHorizontal.X, hotSpringRowStart * offsetVertical.Y);

                    // Position aquifer below the hot spring
                    aquifer.ScreenCoordinates = topLeftPatchPosition
                        + new Vector2(i * offsetHorizontal.X, (hotSpringRowStart + 1) * offsetVertical.Y);
                }

                // Position cave if present
                if (cave != null)
                {
                    // Position the cave to the right of the water patches
                    cave.ScreenCoordinates = topLeftPatchPosition + offsetHorizontal;
                }

                break;
            }
        }
    }

    private static void BuildRegionSize(PatchRegion region)
    {
        // Initial size with no patch in it
        region.Width = region.Height = Constants.PATCH_REGION_BORDER_WIDTH + Constants.PATCH_AND_REGION_MARGIN;

        // Per patch offset
        const float offset = Constants.PATCH_NODE_RECT_LENGTH + Constants.PATCH_AND_REGION_MARGIN;

        // Region size configuration
        switch (region.Type)
        {
            case PatchRegion.RegionType.Continent:
            {
                // Initialize width and height with border and margin
                region.Width += offset;
                region.Height += offset;

                var cave = region.Patches.FirstOrDefault(p => p.BiomeType == BiomeType.Cave);

                int waterPatchCount = region.Patches.Count(p => IsWaterPatch(p));

                float caveWidth = 0f;
                if (cave != null)
                {
                    caveWidth = offset; // Cave adds potential width

                    // Adjust region width to accommodate the cave on the left
                    region.Width += caveWidth;
                }

                int hotSpringAquiferCount = region.HotSpringAquiferPairs.Count * 2;

                int totalRows = 1; // Minimum one row for water patches

                if (waterPatchCount > 2)
                {
                    totalRows = 2;
                }

                if (hotSpringAquiferCount > 0)
                {
                    totalRows += 2; // Add two more rows for hot springs and aquifers
                }

                region.Height += (totalRows - 1) * offset;

                // Calculate the number of columns in each row
                int waterRowColumns = waterPatchCount > 2 ? (int)Math.Ceiling(waterPatchCount / 2.0) : waterPatchCount;
                int hotSpringAquiferColumns = region.HotSpringAquiferPairs.Count;

                // The maximum number of columns needed in any row
                int maxColumnsInAnyRow = Math.Max(waterRowColumns, hotSpringAquiferColumns);

                // Adjust width based on the maximum columns needed
                if (maxColumnsInAnyRow > 1)
                {
                    region.Width += (maxColumnsInAnyRow - 1) * offset;
                }

                break;
            }

            case PatchRegion.RegionType.Ocean or PatchRegion.RegionType.Sea:
            {
                int verticalPatchCount = region.Patches.Count(p => IsWaterPatch(p));

                region.Width += offset;

                // If a cave, vent, or seamount is present
                if (verticalPatchCount != region.Patches.Count)
                    region.Width += offset;

                region.Height += verticalPatchCount * offset;

                break;
            }
        }

        // Ensure minimum size to prevent degenerate polygons
        const float minSize = 100f; // Adjust as needed
        region.Width = Math.Max(region.Width, minSize);
        region.Height = Math.Max(region.Height, minSize);
    }

    private static void BuildPatchesInRegions(PatchMap map, Random random)
    {
        foreach (var region in map.Regions)
        {
            BuildPatchesInRegion(region.Value, random);
            foreach (var patch in region.Value.Patches)
            {
                map.AddPatch(patch);
            }
        }
    }

    private static void ConnectPatchesBetweenRegions(PatchMap map, Random random)
    {
        foreach (var region in map.Regions)
        {
            ConnectPatchesBetweenRegions(region.Value, random);
        }
    }

    private static void ConnectPatchesBetweenRegions(PatchRegion region, Random random)
    {
        switch (region.Type)
        {
            case PatchRegion.RegionType.Ocean or PatchRegion.RegionType.Sea:
            {
                foreach (var adjacent in region.Adjacent)
                {
                    switch (adjacent.Type)
                    {
                        case PatchRegion.RegionType.Sea or PatchRegion.RegionType.Ocean:
                        {
                            int maxIndex =
                                Math.Min(region.Patches.Count(IsWaterPatch), adjacent.Patches.Count(IsWaterPatch)) - 1;

                            int lowestConnectedLevel = random.Next(0, maxIndex);

                            for (int i = 0; i <= lowestConnectedLevel; ++i)
                            {
                                LinkPatches(region.Patches[i], adjacent.Patches[i]);
                            }

                            break;
                        }

                        case PatchRegion.RegionType.Continent:
                        {
                            LinkPatches(region.Patches[0], adjacent.Patches.OrderBy(_ => random.Next())
                                .First(p => IsWaterPatch(p) && p.BiomeType != BiomeType.Tidepool));
                            break;
                        }
                    }
                }

                break;
            }

            case PatchRegion.RegionType.Continent:
            {
                foreach (var adjacent in region.Adjacent)
                {
                    if (adjacent.Type == PatchRegion.RegionType.Continent)
                    {
                        int maxIndex =
                            Math.Min(region.Patches.Count(IsWaterPatch), adjacent.Patches.Count(IsWaterPatch));

                        int patchIndex = random.Next(0, maxIndex);
                        LinkPatches(region.Patches[patchIndex], adjacent.Patches[patchIndex]);
                    }

                    // Connect aquifer patches to cave patches in adjacent regions
                    var adjacentCavePatches = adjacent.Patches.Where(p => p.BiomeType == BiomeType.Cave).ToList();

                    foreach (var aquiferPair in region.HotSpringAquiferPairs)
                    {
                        var aquifer = aquiferPair.Aquifer;

                        foreach (var cavePatch in adjacentCavePatches)
                        {
                            LinkPatches(aquifer, cavePatch);
                        }
                    }
                }

                break;
            }
        }
    }

    /// <summary>
    ///   Returns a predefined patch with default values.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     Note that after calling this, regions and patches have already been linked together.
    ///   </para>
    /// </remarks>
    /// <param name="biome">The requested biome</param>
    /// <param name="id">ID of this patch</param>
    /// <param name="region">Region this patch belongs to</param>
    /// <param name="regionName">Name of the region</param>
    /// <returns>Predefined patch</returns>
    /// <exception cref="InvalidOperationException">Thrown if biome is not a valid value</exception>
    private static Patch NewPredefinedPatch(BiomeType biome, int id, PatchRegion region, string regionName)
    {
        var newPatch = biome switch
        {
            BiomeType.Abyssopelagic => new Patch(GetPatchLocalizedName(regionName, "ABYSSOPELAGIC"),
                id, GetBiomeTemplate("abyssopelagic"), BiomeType.Abyssopelagic, region)
            {
                Depth =
                {
                    [0] = 4000,
                    [1] = 6000,
                },
                ScreenCoordinates = new Vector2(300, 400),
            },

            BiomeType.Bathypelagic => new Patch(GetPatchLocalizedName(regionName, "BATHYPELAGIC"),
                id, GetBiomeTemplate("bathypelagic"), BiomeType.Bathypelagic, region)
            {
                Depth =
                {
                    [0] = 1000,
                    [1] = 4000,
                },
                ScreenCoordinates = new Vector2(200, 300),
            },

            BiomeType.Cave => new Patch(GetPatchLocalizedName(regionName, "UNDERWATERCAVE"),
                id, GetBiomeTemplate("underwater_cave"), BiomeType.Cave, region)
            {
                Depth =
                {
                    [0] = 200,
                    [1] = 1000,
                },
                ScreenCoordinates = new Vector2(300, 200),
            },

            BiomeType.Coastal => new Patch(GetPatchLocalizedName(regionName, "COASTAL"),
                id, GetBiomeTemplate("coastal"), BiomeType.Coastal, region)
            {
                Depth =
                {
                    [0] = 0,
                    [1] = 200,
                },
                ScreenCoordinates = new Vector2(100, 100),
            },

            BiomeType.Epipelagic => new Patch(GetPatchLocalizedName(regionName, "EPIPELAGIC"),
                id, GetBiomeTemplate("default"), BiomeType.Epipelagic, region)
            {
                Depth =
                {
                    [0] = 0,
                    [1] = 200,
                },
                ScreenCoordinates = new Vector2(200, 100),
            },

            BiomeType.Estuary => new Patch(GetPatchLocalizedName(regionName, "ESTUARY"),
                id, GetBiomeTemplate("estuary"), BiomeType.Estuary, region)
            {
                Depth =
                {
                    [0] = 0,
                    [1] = 200,
                },
                ScreenCoordinates = new Vector2(70, 160),
            },

            BiomeType.HotSpring => new Patch(GetPatchLocalizedName(regionName, "HOTSPRING"),
                id, GetBiomeTemplate("hotspring"), BiomeType.HotSpring, region)
            {
                Depth =
                {
                    [0] = 0,
                    [1] = 200,
                },
                ScreenCoordinates = new Vector2(70, 160),
            },

            BiomeType.Aquifer => new Patch(GetPatchLocalizedName(regionName, "AQUIFER"),
                id, GetBiomeTemplate("aquifer"), BiomeType.Aquifer, region)
            {
                Depth =
                {
                    [0] = 200,
                    [1] = 1000,
                },
                ScreenCoordinates = new Vector2(70, 260),
            },

            BiomeType.IceShelf => new Patch(GetPatchLocalizedName(regionName, "ICESHELF"),
                id, GetBiomeTemplate("ice_shelf"), BiomeType.IceShelf, region)
            {
                Depth =
                {
                    [0] = 0,
                    [1] = 200,
                },
                ScreenCoordinates = new Vector2(200, 30),
            },

            BiomeType.Mesopelagic => new Patch(GetPatchLocalizedName(regionName, "MESOPELAGIC"),
                id, GetBiomeTemplate("mesopelagic"), BiomeType.Mesopelagic, region)
            {
                Depth =
                {
                    [0] = 200,
                    [1] = 1000,
                },
                ScreenCoordinates = new Vector2(200, 200),
            },

            BiomeType.Hadopelagic => new Patch(GetPatchLocalizedName(regionName, "HADOPELAGIC"),
                id, GetBiomeTemplate("hadopelagic"), BiomeType.Hadopelagic, region)
            {
                Depth =
                {
                    [0] = 6000,
                    [1] = 8000,
                },
                ScreenCoordinates = new Vector2(300, 500),
            },

            BiomeType.Seafloor => new Patch(GetPatchLocalizedName(regionName, "SEA_FLOOR"),
                id, GetBiomeTemplate("seafloor"), BiomeType.Seafloor, region)
            {
                Depth =
                {
                    [0] = 4000,
                    [1] = 6000,
                },
                ScreenCoordinates = new Vector2(200, 400),
            },

            BiomeType.Tidepool => new Patch(GetPatchLocalizedName(regionName, "TIDEPOOL"),
                id, GetBiomeTemplate("tidepool"), BiomeType.Tidepool, region)
            {
                Depth =
                {
                    [0] = 0,
                    [1] = 10,
                },
                ScreenCoordinates = new Vector2(300, 100),
            },

            BiomeType.Seamount => new Patch(GetPatchLocalizedName(regionName, "SEAMOUNT"),
                id, GetBiomeTemplate("seamount"), BiomeType.Seamount, region)
            {
                Depth =
                {
                    [0] = 200,
                    [1] = 1000,
                },
                ScreenCoordinates = new Vector2(200, 200),
            },

            // ReSharper disable once StringLiteralTypo
            BiomeType.Vents => new Patch(GetPatchLocalizedName(regionName, "VOLCANIC_VENT"),
                id, GetBiomeTemplate("aavolcanic_vent"), BiomeType.Vents, region)
            {
                Depth =
                {
                    [0] = 2500,
                    [1] = 3000,
                },
                ScreenCoordinates = new Vector2(100, 400),
            },
            _ => throw new InvalidOperationException($"{nameof(biome)} is not a valid biome enum value."),
        };

        // Add this patch to the region
        region.Patches.Add(newPatch);
        return newPatch;
    }

    private static void ConfigureStartingPatch(PatchMap map, WorldGenerationSettings settings, Species defaultSpecies,
    Patch vents, Patch? tidepool, Random random)
    {
        // Choose this here to ensure the same seed creates the same world regardless of starting location type
        var randomPatch = map.Patches.Random(random) ?? throw new Exception("No patches to pick from");

        Patch selectedPatch = null; // Initialize variable for selected patch

        switch (settings.Origin)
        {
            case WorldGenerationSettings.LifeOrigin.Vent:
                selectedPatch = vents;
                break;
            case WorldGenerationSettings.LifeOrigin.Pond:
                selectedPatch = tidepool ?? throw new InvalidOperationException($"No tidepool patch created for seed {settings.Seed}");
                break;
            case WorldGenerationSettings.LifeOrigin.Panspermia:
                selectedPatch = randomPatch;
                break;
            default:
                GD.PrintErr($"Selected origin {settings.Origin} doesn't match a known origin type");
                selectedPatch = randomPatch;
                break;
        }

        // Check if the selected patch is part of the map before setting it as CurrentPatch
        if (selectedPatch != null && map.ContainsPatch(selectedPatch))
        {
            map.CurrentPatch = selectedPatch;
            map.CurrentPatch.AddSpecies(defaultSpecies, Constants.INITIAL_SPECIES_POPULATION);
        }
        else
        {
            GD.PrintErr($"Cannot set CurrentPatch to {selectedPatch?.Name} as it's not in the map.");
            map.CurrentPatch = randomPatch; // Fallback to a valid patch
            map.CurrentPatch.AddSpecies(defaultSpecies, Constants.INITIAL_SPECIES_POPULATION);
        }
    }

    private static LocalizedString GetPatchLocalizedName(string regionName, string biomeType)
    {
        return new LocalizedString("PATCH_NAME", regionName, new LocalizedString(biomeType));
    }

    /// <summary>
    ///   Checks if the graph is connected
    /// </summary>
    private static bool IsGraphConnected(bool[,] graph, int vertexCount, out bool[] visited)
    {
        visited = new bool[vertexCount];
        var stack = new Stack<int>();
        stack.Push(0); // Start from the first vertex
        visited[0] = true;

        while (stack.Count > 0)
        {
            int current = stack.Pop();

            for (int i = 0; i < vertexCount; i++)
            {
                if (graph[current, i] && !visited[i])
                {
                    visited[i] = true;
                    stack.Push(i);
                }
            }
        }

        // Check if all vertices were visited
        return visited.All(v => v);
    }
}
