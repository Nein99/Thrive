using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using System.Threading.Tasks;

/// <summary>
///   Draws a PatchMap inside a control
/// </summary>
public partial class PatchMapDrawer : Control
{
    private bool isMainInstance = true;
    [Export]
    public bool DrawDefaultMapIfEmpty;

    [Export(PropertyHint.ColorNoAlpha)]
    public Color DefaultConnectionColor = Colors.ForestGreen;

    [Export(PropertyHint.ColorNoAlpha)]
    public Color HighlightedConnectionColor = Colors.Cyan;

    [Export]
    public NodePath? PatchNodeContainerPath;

    [Export]
    public NodePath LineContainerPath = null!; // Ensure this is correctly assigned in the Godot Editor

    #pragma warning disable CA2213
    [Export]
    public ShaderMaterial MonochromeMaterial = null!; // Ensure this is correctly assigned in the Godot Editor
    #pragma warning restore CA2213

    [Export(PropertyHint.ColorNoAlpha)]
    public Color GlobalOceanColor = new Color(0.0f, 0.1f, 0.4f);

    [Export(PropertyHint.ColorNoAlpha)]
    public Color GlobalOceanColorTransparent = new Color(0.0f, 0.1f, 0.4f, 0.4f);

    [Export(PropertyHint.ColorNoAlpha)]
    public Color GlobalLandColor = new Color(0.545f, 0.271f, 0.075f); // Default Brown

    [Export(PropertyHint.ColorNoAlpha)]
    public Color GlobalLandColorTransparent = new Color(0.545f, 0.271f, 0.075f, 0.4f); // Semi-Transparent Brown

    [Export(PropertyHint.ColorNoAlpha)]
    public Color VentLineColor = new Color(1.0f, 1.0f, 1.0f, 0.1f);

    [Export]
    public float VentLineWidth = 10.0f; // Adjust as needed

    [Export(PropertyHint.ColorNoAlpha)]
    public Color OceanTrenchLineColor = new Color(0.0f, 0.0f, 0.0f, 0.1f);

    [Export]
    public float OceanTrenchLineWidth = 10.0f; // Adjust thickness as needed

    [Export(PropertyHint.Range, "1,20,1")]
    public int VentLineSegments = 3;          // Number of segments for vent lines

    [Export(PropertyHint.Range, "0,50,1")]
    public float VentLineJaggedness = 10.0f;  // Jaggedness for vent lines

    [Export(PropertyHint.Range, "1,20,1")]
    public int TrenchLineSegments = 5;        // Number of segments for trench lines

    [Export(PropertyHint.Range, "0,50,1")]
    public float TrenchLineJaggedness = 10.0f; // Jaggedness for trench lines

    [Export(PropertyHint.ColorNoAlpha)]
    public Color IceShelfPolygonColor = new Color(1.0f, 1.0f, 1.0f, 0.5f); // Transparent White

    [Export(PropertyHint.Range, "1,10,1")]
    public float IceShelfPolygonLineWidth = 2.0f; // Adjust thickness as needed

    [Export(PropertyHint.ColorNoAlpha)]
    public Color GlobalBorderColor = new Color(0.0f, 0.0f, 0.0f); // Default Brown

    [Export(PropertyHint.Range, "1,50,1")]
    public float BorderLineWidth = 100.0f; // Thickness of the border lines

    [Export(PropertyHint.Range, "1,100,1")]
    public float VentPolygonWidth = 20.0f; // Width of the vent polygon

    [Export(PropertyHint.Range, "1,100,1")]
    public float TrenchPolygonWidth = 30.0f; // Width of the trench polygon

    private List<Vector2[]> ventMainPolygons = new List<Vector2[]>();
    private List<Vector2[]> ventStepPolygons = new List<Vector2[]>();
    private List<Vector2[]> ventBufferPolygons = new List<Vector2[]>();

    private List<Vector2[]> trenchMainPolygons = new List<Vector2[]>();
    private List<Vector2[]> trenchStepPolygons = new List<Vector2[]>();
    private List<Vector2[]> trenchBufferPolygons = new List<Vector2[]>();

    private readonly Dictionary<Patch, PatchMapNode> nodes = new();

    /// <summary>
    ///   The representation of connections between regions, so we won't draw the same connection multiple times
    /// </summary>
    private readonly Dictionary<Vector2I, Vector2[]> connections = new();

    #pragma warning disable CA2213
    private PackedScene nodeScene = null!;
    private Control patchNodeContainer = null!;
    private Control lineContainer = null!;
    #pragma warning restore CA2213

    private PatchMap map = null!;

    private bool dirty = true;

    private bool alreadyDrawn;

    private Dictionary<Patch, bool>? patchEnableStatusesToBeApplied;

    private Patch? selectedPatch;

    private Patch? playerPatch;

    private Rect2 backgroundRect = new Rect2(Vector2.Zero, new Vector2(2048, 1024)); // Default size

    [Signal]
    public delegate void OnCurrentPatchCenteredEventHandler(Vector2 coordinates, bool smoothed);

    public PatchMap? Map
    {
        get => map;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value), "setting to null not allowed");

            if (map == value)
                return;

            map = value;
            MarkDirty();

            playerPatch ??= map.CurrentPatch;
        }
    }

    /// <summary>
    ///   The current patch the player is in.
    /// </summary>
    public Patch? PlayerPatch
    {
        get => playerPatch;
        set
        {
            if (playerPatch == value)
                return;

            playerPatch = value;
            UpdateNodeSelections();
            NotifySelectionChanged();
        }
    }

    public Patch? SelectedPatch
    {
        get => selectedPatch;
        set
        {
            if (selectedPatch == value)
                return;

            // Only allow selecting the patch if it is selectable
            foreach (var (patch, node) in nodes)
            {
                if (patch == value)
                {
                    if (!node.Enabled)
                    {
                        GD.Print("Not selecting map node that is not enabled");
                        return;
                    }

                    break;
                }
            }

            selectedPatch = value;
            UpdateNodeSelections();
            NotifySelectionChanged();
        }
    }

    /// <summary>
    ///   Called when the currently shown patch properties should be looked up again
    /// </summary>
    public Action<PatchMapDrawer>? OnSelectedPatchChanged { get; set; }

    public override async void _Ready()
    {
        GD.Print("PatchMapDrawer _Ready() called");

        base._Ready();

        // Ensure that the node is properly initialized
        patchNodeContainer = GetNode<Control>(PatchNodeContainerPath);
        lineContainer = GetNode<Control>(LineContainerPath);

        nodeScene = GD.Load<PackedScene>("res://src/microbe_stage/editor/PatchMapNode.tscn");

        // Initialize Map immediately
        if (DrawDefaultMapIfEmpty && Map == null)
        {
            GD.Print("Generating and showing a new patch map for testing in PatchMapDrawer");
            Map = new GameWorld(new WorldGenerationSettings()).Map;
        }

        // Only the main instance should save the planet map texture
        if (isMainInstance)
        {
            // Call the method to save the planet map texture
            await SavePlanetMapToFileAsync();
        }

        // Create a new Image instance
        Image image = new Image();

        image = Image.Create(16, 16, false, Image.Format.Rgba8);

        // Fill the image with a color
        image.Fill(Colors.Red);

        // Define the file path
        string directoryPath = "user://test/";
        string filePath = $"{directoryPath}test_image.png";

        // Create the directory
        string absoluteDirectoryPath = ProjectSettings.GlobalizePath(directoryPath);
        Error dirErr = DirAccess.MakeDirRecursiveAbsolute(absoluteDirectoryPath);
        if (dirErr != Error.Ok && dirErr != Error.AlreadyExists)
        {
            GD.PrintErr($"Failed to create directory: {dirErr}");
            return;
        }

        // Save the image
        Error err = image.SavePng(filePath);
        if (err != Error.Ok)
        {
            GD.PrintErr($"Failed to save test image: {err}");
        }
        else
        {
            GD.Print($"Test image saved to: {filePath}");
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        CheckNodeSelectionUpdate();

        if (dirty)
        {
            RebuildMap();
            QueueRedraw();

            CustomMinimumSize = GetRightBottomCornerPointOnMap() + new Vector2(450, 450);

            dirty = false;
        }
    }

    /// <summary>
    ///   Custom drawing, draws the lines between map nodes and ocean polygons.
    /// </summary>
    public override async void _Draw()
    {
        base._Draw();

        if (Map == null)
            return;

        // **Randomize GlobalOceanColor based on the world seed on first draw to add visual diversity**
        if (!alreadyDrawn)
        {
            long seed = GetWorldSeed(); // Method to retrieve the world seed

            var rand = new Random((int)(seed % int.MaxValue)); // Seed Random with a deterministic value

            // Generate random values for red and green between 0.0 and 0.5, small chance of green or rust colored oceans
            float r = (float)rand.NextDouble() * 0.5f;
            float g = (float)rand.NextDouble() * 0.5f;
            // Generate blue between 0.4 and 0.8 for a strong blue presence
            float b = 0.4f + (float)rand.NextDouble() * 0.4f;

            // **Generate Random Land Color (Shades of Brown)**
            // Brown typically has higher red and green components with lower blue
            float landR = 0.0f + (float)rand.NextDouble() * 0.8f; // Red between 0 and 0.8
            float landG = 0.0f + (float)rand.NextDouble() * 0.4f; // Green between 0 and 0.6
            float landB = 0.0f + (float)rand.NextDouble() * 0.3f; // Blue between 0 and 0.3

            GlobalLandColor = new Color(landR, landG, landB);
            GlobalLandColorTransparent = new Color(landR, landG, landB, 0.4f);

            // Assign the new deterministic blue shade to GlobalOceanColor
            GlobalOceanColor = new Color(r, g, b);

            // Update the transparent version accordingly
            GlobalOceanColorTransparent = new Color(r, g, b, 0.4f);
        }

        // **Draw Global Background First**
        DrawGlobalBackground();

        // **Draw Brown Organic Polygons for Continent Groups**
        DrawConnectedContinents();

        // **Draw Ocean Polygons After to Create Visual Indentations**
        DrawOceanPolygons();

        // **Draw Connected Ocean Polygons in Front of Land Polygons**
        DrawConnectedOceans();

        // **Draw Vent Buffer Layers with GlobalOceanColor**
        foreach (var bufferPolygon in ventBufferPolygons)
        {
            DrawColoredPolygon(bufferPolygon, GlobalOceanColor);
        }

        // **Draw Trench Buffer Layers with GlobalOceanColor**
        foreach (var bufferPolygon in trenchBufferPolygons)
        {
            DrawColoredPolygon(bufferPolygon, GlobalOceanColor);
        }

        // **Draw Main Vent Layers with VentLineColor**
        foreach (var mainPolygon in ventMainPolygons)
        {
            DrawColoredPolygon(mainPolygon, VentLineColor);
        }

        // **Draw Trench Main Layers with OceanTrenchLineColor**
        foreach (var mainPolygon in trenchMainPolygons)
        {
            DrawColoredPolygon(mainPolygon, OceanTrenchLineColor);
        }

        // **Draw Step Vent Layers with Semi-Transparent VentLineColor**
        foreach (var stepPolygon in ventStepPolygons)
        {
            Color stepColor = new Color(VentLineColor.R, VentLineColor.G, VentLineColor.B, 0.1f);
            DrawColoredPolygon(stepPolygon, stepColor);
        }

        // **Draw Step Trench Layers with Semi-Transparent OceanTrenchLineColor**
        foreach (var stepPolygon in trenchStepPolygons)
        {
            Color stepColor = new Color(OceanTrenchLineColor.R, OceanTrenchLineColor.G, OceanTrenchLineColor.B, 0.1f);
            DrawColoredPolygon(stepPolygon, stepColor);
        }

        // **Draw Hydrothermal Vent Lines in Front of Everything**
        DrawHydrothermalVentLines();

        // **Draw Ocean Trench Lines in Front of Hydrothermal Vent Lines**
        DrawOceanTrenchLines();

        // **Conditional Drawing of Ice Coverage Based on Temperature**
        //if (map.Settings.AverageTemp != -50f)
        //{
            // **Draw Ice Shelf Polygons on Top of Everything**
            DrawIceShelfPolygons();
        //}

        // **Draw Global Ice Coverage Rectangle if Temperature is -50°C**
        //if (map.Settings.AverageTemp == -50f)
        //{
        //    DrawGlobalIceCoverage();
        //}

        // DrawGlobalBorder();

        if (isMainInstance)
            {
                // Create connections between regions if they don't exist.
                if (connections.Count == 0)
                {
                    CreateRegionLinks();
                    RebuildRegionConnections();
                }
            }

        // **Skip drawing patches and regions if not the main instance**
        if (isMainInstance)
        {
            // Draw Existing Features: Region Borders and Patch Links
            DrawRegionBorders();
            DrawPatchLinks();
        }

        // Only the main instance should save the planet map texture
        if (isMainInstance && !alreadyDrawn)
        {
            // Call the method to save the planet map texture
            await SavePlanetMapToFileAsync();

            // After the texture is saved, center the view to the player's patch
            CenterToCurrentPatch(false);

            // Set alreadyDrawn to true to prevent re-centering
            alreadyDrawn = true;
        }

        // For debugging: draw the content bounds
        if (!isMainInstance)
        {
            Rect2 contentBounds = GetPlanetMapBounds();
            DrawRect(contentBounds, Colors.Red, false);
        }
    }

    /// <summary>
    /// Retrieves the world seed from the PatchMap's settings.
    /// Adjust this method based on how your PatchMap or WorldSettings exposes the seed.
    /// </summary>
    /// <returns>The world seed as a long integer.</returns>
    private long GetWorldSeed()
    {
        // Assuming PatchMap.Settings.Seed exists
        return map.Settings.Seed;

        // If the seed is accessed differently, adjust accordingly.
        // For example:
        // return Editor.CurrentGame.GameWorld.WorldSettings.Seed;
    }

    /// <summary>
    /// Draws either the global ocean or land background based on the humidity level.
    /// If humidity is 20% or less, it uses GlobalLandColor; otherwise, it uses GlobalOceanColor.
    /// </summary>
    private void DrawGlobalBackground()
    {
        if (Map == null)
            return;

        // Determine the bounds of the map
        Vector2 maxPoint = GetRightBottomCornerPointOnMap();
        Vector2 minPoint = Vector2.Zero; // Assuming the map starts at (0,0)

        float currentWidth = maxPoint.X - minPoint.X;
        float currentHeight = maxPoint.Y - minPoint.Y;

        float desiredAspectRatio = 2.0f / 1.0f;

        float desiredWidth = currentWidth;
        float desiredHeight = currentHeight;

        if (currentWidth / currentHeight > desiredAspectRatio)
        {
            // Width is too much, adjust height
            desiredHeight = currentWidth / desiredAspectRatio;
        }
        else
        {
            // Height is too much, adjust width
            desiredWidth = currentHeight * desiredAspectRatio;
        }

        // Center the background rectangle based on existing map size
        Vector2 backgroundPosition = minPoint + new Vector2((currentWidth - desiredWidth) / 2, (desiredHeight - currentHeight) / 2);

        // **Store the background rectangle**
        backgroundRect = new Rect2(backgroundPosition, new Vector2(desiredWidth, desiredHeight));

        // Determine color based on humidity
        float humidity = 60; // Assuming humidity is a float between 0 and 100
        Color backgroundColor = humidity <= 20f ? GlobalLandColor : GlobalOceanColor;

        // Draw the filled background rectangle with the chosen color
        DrawRect(backgroundRect, backgroundColor, true);
    }

    /// <summary>
    /// Draws a 2:1 black border around the map using four thick lines to hide polygons spilling over the edge.
    /// The lines are very thick, do not obscure the map itself, and frame the map including the corners.
    /// </summary>
    private void DrawGlobalBorder()
    {
        if (Map == null)
            return;

        // Determine the bounds of the map
        Vector2 maxPoint = GetRightBottomCornerPointOnMap();
        Vector2 minPoint = Vector2.Zero; // Assuming the map starts at (0,0)

        float currentWidth = maxPoint.X - minPoint.X;
        float currentHeight = maxPoint.Y - minPoint.Y;

        float desiredAspectRatio = 2.0f / 1.0f;

        float desiredWidth = currentWidth;
        float desiredHeight = currentHeight;

        if (currentWidth / currentHeight > desiredAspectRatio)
        {
            // Width is too much, adjust height
            desiredHeight = currentWidth / desiredAspectRatio;
        }
        else
        {
            // Height is too much, adjust width
            desiredWidth = currentHeight * desiredAspectRatio;
        }

        // Center the border based on existing map size
        Vector2 borderPosition = minPoint + new Vector2((currentWidth - desiredWidth) / 2, (desiredHeight - currentHeight) / 2);
        Rect2 borderRect = new Rect2(borderPosition, new Vector2(desiredWidth, desiredHeight));

        // Define the line width
        float lineWidth = BorderLineWidth; // Use the exported property
        float halfLineWidth = lineWidth / 2.0f;

        // Extend the top and bottom lines by halfLineWidth on both ends to cover the corners
        Vector2 topStart = new Vector2(borderRect.Position.X - halfLineWidth, borderRect.Position.Y - halfLineWidth);
        Vector2 topEnd = new Vector2(borderRect.Position.X + borderRect.Size.X + halfLineWidth, borderRect.Position.Y - halfLineWidth);

        Vector2 bottomStart = new Vector2(borderRect.Position.X - halfLineWidth, borderRect.Position.Y + borderRect.Size.Y + halfLineWidth);
        Vector2 bottomEnd = new Vector2(borderRect.Position.X + borderRect.Size.X + halfLineWidth, borderRect.Position.Y + borderRect.Size.Y + halfLineWidth);

        // The left and right lines remain the same, extending from top to bottom with halfLineWidth offset
        Vector2 leftStart = new Vector2(borderRect.Position.X - halfLineWidth, borderRect.Position.Y - halfLineWidth);
        Vector2 leftEnd = new Vector2(borderRect.Position.X - halfLineWidth, borderRect.Position.Y + borderRect.Size.Y + halfLineWidth);

        Vector2 rightStart = new Vector2(borderRect.Position.X + borderRect.Size.X + halfLineWidth, borderRect.Position.Y - halfLineWidth);
        Vector2 rightEnd = new Vector2(borderRect.Position.X + borderRect.Size.X + halfLineWidth, borderRect.Position.Y + borderRect.Size.Y + halfLineWidth);

        // Draw the four lines
        DrawLine(topStart, topEnd, GlobalBorderColor, lineWidth, false);
        DrawLine(bottomStart, bottomEnd, GlobalBorderColor, lineWidth, false);
        DrawLine(leftStart, leftEnd, GlobalBorderColor, lineWidth, false);
        DrawLine(rightStart, rightEnd, GlobalBorderColor, lineWidth, false);
    }

    /// <summary>
    /// Draws brown polygons connecting connected continent regions.
    /// </summary>
    private void DrawConnectedContinents()
    {
        if (Map == null)
            return;

        // Group connected continent regions
        var connectedGroups = GroupConnectedContinents(Map);

        GD.Print($"Grouped {connectedGroups.Count} connected continent groups.");

        for (int i = 0; i < connectedGroups.Count; i++)
        {
            GD.Print($"Group {i + 1} has {connectedGroups[i].Count} regions.");
        }

        foreach (var group in connectedGroups)
        {
            if (group.Count < 1)
                continue;

            var organicPolygon = ComputeConvexHullForGroup(group);

            if (organicPolygon.Length >= 3) // Ensure a valid polygon
            {
                DrawColoredPolygon(organicPolygon.ToArray(), GlobalLandColor); // Use randomized brown color
            }
        }
    }

        /// <summary>
        /// Checks if a given region contains any ice shelf patches.
        /// </summary>
        /// <param name="region">The patch region to check.</param>
        /// <returns>True if the region contains at least one ice shelf patch; otherwise, false.</returns>
        private bool ContainsIceShelfPatch(PatchRegion region)
        {
            return region.Patches.Any(p => p.BiomeType == BiomeType.IceShelf);
        }

        /// <summary>
        /// Groups connected regions that contain ice shelf patches based on direct adjacency.
        /// Each group represents a cluster of directly adjacent regions with ice shelf patches.
        /// </summary>
        private List<List<PatchRegion>> GroupConnectedIceShelfRegions(PatchMap map)
        {
            var visited = new HashSet<int>();
            var groups = new List<List<PatchRegion>>();

            foreach (var region in map.Regions.Values.Where(r => ContainsIceShelfPatch(r)))
            {
                if (visited.Contains(region.ID))
                    continue;

                var group = new List<PatchRegion>();
                var queue = new Queue<PatchRegion>();
                queue.Enqueue(region);
                visited.Add(region.ID);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    group.Add(current);

                    foreach (var neighbor in current.Adjacent.Where(r => ContainsIceShelfPatch(r)))
                    {
                        if (!visited.Contains(neighbor.ID))
                        {
                            queue.Enqueue(neighbor);
                            visited.Add(neighbor.ID);
                        }
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        /// <summary>
        /// Draws organic polygons connecting connected ocean regions.
        /// These polygons are rendered in front of land polygons to ensure visibility.
        /// </summary>
        private void DrawConnectedOceans()
        {
            if (Map == null)
                return;

            // Group connected ocean regions
            var connectedOceanGroups = GroupConnectedOceans(Map);

            GD.Print($"Grouped {connectedOceanGroups.Count} connected ocean groups.");

            for (int i = 0; i < connectedOceanGroups.Count; i++)
            {
                GD.Print($"Ocean Group {i + 1} has {connectedOceanGroups[i].Count} regions.");
            }

            foreach (var group in connectedOceanGroups)
            {
                if (group.Count < 1)
                    continue;

                var organicOceanPolygon = ComputeConvexHullForGroup(group);

                if (organicOceanPolygon.Length >= 3) // Ensure a valid polygon
                {
                    DrawColoredPolygon(organicOceanPolygon.ToArray(), GlobalOceanColor); // Semi-transparent Blue
                }
            }
        }

        /// <summary>
        /// Generates a jagged line between two points by adding random perpendicular offsets.
        /// </summary>
        /// <param name="start">Starting point of the line.</param>
        /// <param name="end">Ending point of the line.</param>
        /// <param name="segments">Number of segments to divide the line into.</param>
        /// <param name="jaggedness">Maximum offset magnitude for each segment.</param>
        /// <param name="rand">Random number generator instance for deterministic randomness.</param>
        /// <returns>List of points forming the jagged line.</returns>
        private List<Vector2> GenerateJaggedLine(Vector2 start, Vector2 end, int segments, float jaggedness, Random rand)
        {
            List<Vector2> points = new List<Vector2> { start };

            Vector2 direction = (end - start).Normalized();
            Vector2 perpendicular = new Vector2(-direction.Y, direction.X);

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)(segments + 1);
                Vector2 point = start.Lerp(end, t);

                // Generate a random offset within the specified jaggedness range
                float offsetMagnitude = (float)(rand.NextDouble() - 0.5) * 2 * jaggedness;
                Vector2 offset = perpendicular * offsetMagnitude;

                points.Add(point + offset);
            }

            points.Add(end);

            return points;
        }

        /// <summary>
        /// Generates polygon points around a jagged line by creating left and right offsets with specified width.
        /// </summary>
        /// <param name="jaggedPoints">List of points forming the main jagged line.</param>
        /// <param name="width">Half the desired width of the polygon.</param>
        /// <param name="rand">Random number generator for slight variations.</param>
        /// <returns>Array of points forming the polygon.</returns>
        private Vector2[] GeneratePolygonAroundLine(List<Vector2> jaggedPoints, float width, Random rand)
        {
            if (jaggedPoints.Count < 2)
                return Array.Empty<Vector2>(); // Not enough points to form a polygon

            List<Vector2> leftOffsets = new List<Vector2>();
            List<Vector2> rightOffsets = new List<Vector2>();

            for (int i = 0; i < jaggedPoints.Count; i++)
            {
                Vector2 current = jaggedPoints[i];
                Vector2 direction;

                if (i < jaggedPoints.Count - 1)
                    direction = (jaggedPoints[i + 1] - current).Normalized();
                else
                    direction = (current - jaggedPoints[i - 1]).Normalized();

                Vector2 perpendicular = new Vector2(-direction.Y, direction.X);

                // Introduce slight randomness to the width for more organic shapes
                float randomFactor = 0.8f + (float)rand.NextDouble() * 0.4f; // Between 0.8 and 1.2
                float adjustedWidth = width * randomFactor;

                leftOffsets.Add(current + perpendicular * adjustedWidth);
                rightOffsets.Add(current - perpendicular * adjustedWidth);
            }

            // Combine left and reversed right offsets to form a closed polygon
            List<Vector2> polygonPoints = new List<Vector2>();
            polygonPoints.AddRange(leftOffsets);
            polygonPoints.AddRange(rightOffsets.AsEnumerable().Reverse());

            return polygonPoints.ToArray();
        }

        /// <summary>
        /// Generates layered polygons (main, step, buffer) around a jagged line.
        /// </summary>
        /// <param name="jaggedPoints">List of points forming the main jagged line.</param>
        /// <param name="mainWidth">Half the desired width of the main polygon.</param>
        /// <param name="stepOffset">Offset distance for the step layer.</param>
        /// <param name="bufferWidth">Half the desired width of the buffer polygon.</param>
        /// <param name="rand">Random number generator for slight variations.</param>
        /// <returns>A tuple containing arrays of points for main, step, and buffer polygons.</returns>
        private (Vector2[] Main, Vector2[] Step, Vector2[] Buffer) GenerateLayeredPolygons(
            List<Vector2> jaggedPoints,
            float mainWidth,
            float stepOffset,
            float bufferWidth,
            Random rand)
        {
            // Main Polygon
            Vector2[] mainPolygon = GeneratePolygonAroundLine(jaggedPoints, mainWidth, rand);

            // Step Layer (slightly offset from the main polygon)
            List<Vector2> stepJaggedPoints = jaggedPoints.Select(p => p + new Vector2(stepOffset, stepOffset)).ToList();
            Vector2[] stepPolygon = GeneratePolygonAroundLine(stepJaggedPoints, mainWidth, rand);

            // Buffer Layer (wider than the main polygon)
            Vector2[] bufferPolygon = GeneratePolygonAroundLine(jaggedPoints, bufferWidth, rand);

            return (mainPolygon, stepPolygon, bufferPolygon);
        }

            /// <summary>
        /// Draws Hydrothermal Vent Lines on top of the cached polygons for emphasis.
        /// </summary>
        private void DrawHydrothermalVentLines()
        {
            if (Map == null)
                return;

            // Collect all vent patches
            var ventPatches = Map.Patches.Values
                .Where(p => p.BiomeType == BiomeType.Vents)
                .OrderBy(p => p.ID) // Ensure deterministic ordering
                .ToList();

            // Define a maximum distance to consider patches as adjacent
            float maxDistance = 1000f; // Adjust as necessary

            // Keep track of which vents have been connected
            var connectedVents = new HashSet<int>();

            for (int i = 0; i < ventPatches.Count; i++)
            {
                var patchA = ventPatches[i];
                if (connectedVents.Contains(patchA.ID))
                    continue; // Already connected

                // Find the closest vent patch to patchA that is not yet connected
                Patch? closestPatch = null;
                float closestDistance = float.MaxValue;

                for (int j = 0; j < ventPatches.Count; j++)
                {
                    if (i == j)
                        continue;

                    var patchB = ventPatches[j];
                    if (connectedVents.Contains(patchB.ID))
                        continue; // Already connected

                    float distance = patchA.ScreenCoordinates.DistanceTo(patchB.ScreenCoordinates);
                    if (distance < closestDistance && distance <= maxDistance)
                    {
                        closestDistance = distance;
                        closestPatch = patchB;
                    }
                }

                if (closestPatch != null)
                {
                    // Calculate the center positions of both patches
                    Vector2 start = PatchCenter(patchA.ScreenCoordinates);
                    Vector2 end = PatchCenter(closestPatch.ScreenCoordinates);

                    // Draw the jagged vent line on top of the polygon for emphasis
                    // Generate a jagged line with desired segments and jaggedness
                    int ventSeed = patchA.ID ^ closestPatch.ID ^ (int)(Map.Settings.Seed % int.MaxValue);
                    Random rand = new Random(ventSeed);

                    List<Vector2> jaggedPoints = GenerateJaggedLine(
                        start,
                        end,
                        segments: VentLineSegments,         // Number of segments from exported property
                        jaggedness: VentLineJaggedness,     // Jaggedness from exported property
                        rand
                    );

                    // Draw the jagged vent line
                    for (int k = 0; k < jaggedPoints.Count - 1; k++)
                    {
                        DrawLine(
                            jaggedPoints[k],
                            jaggedPoints[k + 1],
                            VentLineColor,
                            VentLineWidth,
                            antialiased: true
                        );
                    }

                    // Mark only the first patch as connected, the second one can connect to another and form a chain
                    connectedVents.Add(patchA.ID);
                }
            }
        }

        /// <summary>
        /// Draws Ocean Trench Lines on top of the cached polygons for emphasis.
        /// </summary>
        private void DrawOceanTrenchLines()
        {
            if (Map == null)
                return;

            // Collect all ocean trench patches
            var trenchPatches = Map.Patches.Values
                .Where(p => p.BiomeType == BiomeType.Hadopelagic) // Ensure BiomeType.Hadopelagic is correct
                .OrderBy(p => p.ID) // Ensure deterministic ordering
                .ToList();

            // Define a maximum distance to consider patches as adjacent
            float maxDistance = 1500f; // Adjust as necessary

            // Keep track of which trenches have been connected
            var connectedTrenches = new HashSet<int>();

            for (int i = 0; i < trenchPatches.Count; i++)
            {
                var patchA = trenchPatches[i];
                if (connectedTrenches.Contains(patchA.ID))
                    continue; // Already connected

                // Find the closest trench patch to patchA that is not yet connected
                Patch? closestPatch = null;
                float closestDistance = float.MaxValue;

                for (int j = 0; j < trenchPatches.Count; j++)
                {
                    if (i == j)
                        continue;

                    var patchB = trenchPatches[j];
                    if (connectedTrenches.Contains(patchB.ID))
                        continue; // Already connected

                    float distance = patchA.ScreenCoordinates.DistanceTo(patchB.ScreenCoordinates);
                    if (distance < closestDistance && distance <= maxDistance)
                    {
                        closestDistance = distance;
                        closestPatch = patchB;
                    }
                }

                if (closestPatch != null)
                {
                    // Calculate the center positions of both patches
                    Vector2 start = PatchCenter(patchA.ScreenCoordinates);
                    Vector2 end = PatchCenter(closestPatch.ScreenCoordinates);

                    // Generate a jagged line with desired segments and jaggedness
                    int trenchSeed = patchA.ID ^ closestPatch.ID ^ (int)(Map.Settings.Seed % int.MaxValue);
                    Random rand = new Random(trenchSeed);

                    List<Vector2> jaggedPoints = GenerateJaggedLine(
                        start,
                        end,
                        segments: TrenchLineSegments,         // Number of segments from exported property
                        jaggedness: TrenchLineJaggedness,     // Jaggedness from exported property
                        rand
                    );

                    // Draw the jagged trench line
                    for (int k = 0; k < jaggedPoints.Count - 1; k++)
                    {
                        DrawLine(
                            jaggedPoints[k],
                            jaggedPoints[k + 1],
                            OceanTrenchLineColor,
                            OceanTrenchLineWidth,
                            antialiased: true
                        );
                    }

                    // Mark only the first patch as connected, the second one can connect to another and form a chain
                    connectedTrenches.Add(patchA.ID);
                }
            }
        }

        /// <summary>
        /// Draws a 2:1 white rectangle representing global ice coverage over the entire planet.
        /// This rectangle is drawn in front of all other map elements.
        /// </summary>
        private void DrawGlobalIceCoverage()
        {
            // Determine the bounds of the map
            Vector2 maxPoint = GetRightBottomCornerPointOnMap();
            Vector2 minPoint = Vector2.Zero; // Assuming the map starts at (0,0)

            float currentWidth = maxPoint.X - minPoint.X;
            float currentHeight = maxPoint.Y - minPoint.Y;

            // Desired aspect ratio 2:1 (width:height)
            float desiredAspectRatio = 2.0f / 1.0f;

            float desiredWidth = currentWidth;
            float desiredHeight = currentHeight;

            if (currentWidth / currentHeight > desiredAspectRatio)
            {
                // Width is too much, adjust height
                desiredHeight = currentWidth / desiredAspectRatio;
            }
            else
            {
                // Height is too much, adjust width
                desiredWidth = currentHeight * desiredAspectRatio;
            }

            // Center the rectangle based on map size
            Vector2 oceanPosition = minPoint + new Vector2((currentWidth - desiredWidth) / 2, (currentHeight - desiredHeight) / 2);
            Rect2 iceRect = new Rect2(oceanPosition, new Vector2(desiredWidth, desiredHeight));

            // Draw the filled ice rectangle with white color, semi-transparent
            Color iceColor = new Color(1.0f, 1.0f, 1.0f, 0.8f); // Adjust alpha as needed (0.0f to 1.0f)
            DrawRect(iceRect, iceColor, true);
        }

        /// <summary>
        /// Groups connected continent regions based on direct adjacency.
        /// Each group represents a cluster of directly adjacent continent regions.
        /// </summary>
        private List<List<PatchRegion>> GroupConnectedContinents(PatchMap map)
        {
            var visited = new HashSet<int>();
            var groups = new List<List<PatchRegion>>();

            foreach (var region in map.Regions.Values.Where(r => r.Type == PatchRegion.RegionType.Continent))
            {
                if (visited.Contains(region.ID))
                    continue;

                var group = new List<PatchRegion>();
                var queue = new Queue<PatchRegion>();
                queue.Enqueue(region);
                visited.Add(region.ID);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    group.Add(current);

                    foreach (var neighbor in current.Adjacent.Where(r => r.Type == PatchRegion.RegionType.Continent))
                    {
                        if (!visited.Contains(neighbor.ID))
                        {
                            queue.Enqueue(neighbor);
                            visited.Add(neighbor.ID);
                        }
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        /// <summary>
        /// Groups connected ocean regions based on direct adjacency.
        /// Each group represents a cluster of directly adjacent ocean regions.
        /// </summary>
        private List<List<PatchRegion>> GroupConnectedOceans(PatchMap map)
        {
            var visited = new HashSet<int>();
            var groups = new List<List<PatchRegion>>();

            foreach (var region in map.Regions.Values.Where(r => r.Type == PatchRegion.RegionType.Ocean))
            {
                if (visited.Contains(region.ID))
                    continue;

                var group = new List<PatchRegion>();
                var queue = new Queue<PatchRegion>();
                queue.Enqueue(region);
                visited.Add(region.ID);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    group.Add(current);

                    foreach (var neighbor in current.Adjacent.Where(r => r.Type == PatchRegion.RegionType.Ocean))
                    {
                        if (!visited.Contains(neighbor.ID))
                        {
                            queue.Enqueue(neighbor);
                            visited.Add(neighbor.ID);
                        }
                    }
                }

                groups.Add(group);
            }

                return groups;
        }

        /// <summary>
        /// Draws transparent white polygons connecting connected regions with ice shelf patches.
        /// These polygons are rendered on top of all other map elements.
        /// </summary>
        private void DrawIceShelfPolygons()
        {
            if (Map == null)
                return;

            // Group connected regions containing ice shelf patches
            var connectedIceShelfGroups = GroupConnectedIceShelfRegions(Map);

            GD.Print($"Grouped {connectedIceShelfGroups.Count} connected ice shelf groups.");

            for (int i = 0; i < connectedIceShelfGroups.Count; i++)
            {
                GD.Print($"Ice Shelf Group {i + 1} has {connectedIceShelfGroups[i].Count} regions.");
            }

            foreach (var group in connectedIceShelfGroups)
            {
                if (group.Count < 1)
                    continue;

                var iceShelfPolygon = ComputeConvexHullForGroup(group);

                if (iceShelfPolygon.Length >= 3) // Ensure a valid polygon
                {
                    DrawColoredPolygon(iceShelfPolygon.ToArray(), IceShelfPolygonColor); // Transparent White
                }
            }
        }

        private const float ConvexHullPadding = 20f; // Adjust as necessary

        /// <summary>
        /// Computes the convex hull for a group of regions.
        /// Handles singleton groups by generating a larger, organically perturbed square around the patch center.
        /// </summary>
        /// <param name="group">List of connected PatchRegions.</param>
        /// <returns>Array of Vector2 points representing the organic polygon.</returns>
        private Vector2[] ComputeConvexHullForGroup(List<PatchRegion> group)
        {
            var allPoints = new List<Vector2>();

            foreach (var region in group)
            {
                foreach (var patch in region.Patches)
                {
                    // Use the center of each patch for better distribution
                    allPoints.Add(PatchCenter(patch.ScreenCoordinates));
                }
            }

            if (allPoints.Count == 0)
                return Array.Empty<Vector2>();

            Vector2[] initialPolygon;

            if (allPoints.Count == 1)
            {
                // Singleton group: Create a larger, organically perturbed square around the single patch center
                float size = Constants.PATCH_NODE_RECT_LENGTH * 2.0f; // Double the size for better visibility
                Vector2 center = allPoints[0];
                initialPolygon = new Vector2[]
                {
                    center + new Vector2(-size / 2, -size / 2),
                    center + new Vector2(size / 2, -size / 2),
                    center + new Vector2(size / 2, size / 2),
                    center + new Vector2(-size / 2, size / 2)
                };
            }
            else if (allPoints.Count == 2)
            {
                // Two-point group: Create a rectangle with a defined width and apply perturbation
                float width = 30f; // Increased width for better visibility
                Vector2 p1 = allPoints[0];
                Vector2 p2 = allPoints[1];
                Vector2 direction = (p2 - p1).Normalized();
                Vector2 perpendicular = new Vector2(-direction.Y, direction.X);

                initialPolygon = new Vector2[]
                {
                    p1 + perpendicular * width / 2,
                    p2 + perpendicular * width / 2,
                    p2 - perpendicular * width / 2,
                    p1 - perpendicular * width / 2
                };
            }
            else
            {
                // For groups with 3 or more points, compute the convex hull normally
                initialPolygon = Geometry2D.ConvexHull(allPoints.ToArray());

                // Remove the last point if it's a duplicate of the first point
                if (initialPolygon.Length > 1 && initialPolygon[0] == initialPolygon[initialPolygon.Length - 1])
                {
                    initialPolygon = initialPolygon.Take(initialPolygon.Length - 1).ToArray();
                }
            }

            // Subdivide the polygon edges to add more vertices
            int subdivisionsPerEdge = 3; // Adjust this value for more or fewer subdivisions
            var subdividedPolygon = SubdividePolygon(initialPolygon, subdivisionsPerEdge);

            // Apply organic perturbation using the fallback method
            var organicPolygon = MakePolygonOrganic(subdividedPolygon);

            return organicPolygon;
        }

        /// <summary>
        /// Applies organic perturbations to polygon vertices using the fallback noise method.
        /// Enhances smoothness by scaling perturbations based on distance from centroid.
        /// </summary>
        /// <param name="points">Original vertices of the polygon.</param>
        /// <returns>New vertices with organic perturbations.</returns>
        private Vector2[] MakePolygonOrganic(Vector2[] points)
        {
            // Use the fallback method directly without OpenSimplexNoise
            return MakePolygonOrganicFallback(points);
        }

        /// <summary>
        /// Fallback organic perturbation using simple random offsets.
        /// Enhances smoothness by scaling perturbations based on distance from centroid.
        /// </summary>
        /// <param name="points">Original vertices of the polygon.</param>
        /// <returns>New vertices with simple random perturbations.</returns>
        private Vector2[] MakePolygonOrganicFallback(Vector2[] points)
        {
            var random = new Random(42); // Fixed seed for consistency

            // Calculate centroid of the polygon
            Vector2 centroid = CalculateCentroid(points);

            var organicPoints = new List<Vector2>();

            foreach (var point in points)
            {
                // Calculate distance from centroid to point
                float distance = point.DistanceTo(centroid);

                // Dynamic perturbation magnitude based on distance (optional)
                // For example, larger perturbations for points farther from centroid
                float perturbationMagnitude = 5.0f + (distance / 100.0f); // Adjust scaling as needed

                // Apply small random offsets
                float offsetX = ((float)random.NextDouble() - 0.5f) * perturbationMagnitude * 2.0f; // Adjust magnitude as needed
                float offsetY = ((float)random.NextDouble() - 0.5f) * perturbationMagnitude * 2.0f; // Adjust magnitude as needed

                organicPoints.Add(point + new Vector2(offsetX, offsetY));
            }

            // Apply simple smoothing
            var smoothedPoints = SimpleSmoothPolygon(organicPoints.ToArray());

            return smoothedPoints;
        }

        /// <summary>
        /// Calculates the centroid of a polygon.
        /// </summary>
        /// <param name="points">Vertices of the polygon.</param>
        /// <returns>Centroid as a Vector2.</returns>
        private Vector2 CalculateCentroid(Vector2[] points)
        {
            float signedArea = 0;
            float cx = 0;
            float cy = 0;
            int count = points.Length;

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                float a = points[i].X * points[next].Y - points[next].X * points[i].Y;
                signedArea += a;
                cx += (points[i].X + points[next].X) * a;
                cy += (points[i].Y + points[next].Y) * a;
            }

            signedArea *= 0.5f;
            if (Math.Abs(signedArea) < MathUtils.EPSILON)
                return Vector2.Zero; // Avoid division by zero

            cx /= (6.0f * signedArea);
            cy /= (6.0f * signedArea);

            return new Vector2(cx, cy);
        }

        /// <summary>
        /// Applies simple smoothing to a polygon by averaging each point with its immediate neighbors.
        /// </summary>
        /// <param name="points">Vertices of the polygon.</param>
        /// <returns>Smoothed polygon vertices.</returns>
        private Vector2[] SimpleSmoothPolygon(Vector2[] points)
        {
            int count = points.Length;
            var smoothedPoints = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                Vector2 prev = points[(i - 1 + count) % count];
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % count];

                // Average the current point with its neighbors
                smoothedPoints[i] = (prev + current + next) / 3.0f;
            }

            return smoothedPoints;
        }

        /// <summary>
        /// Subdivides a polygon by adding new points between each pair of consecutive vertices.
        /// </summary>
        /// <param name="polygon">The original polygon vertices.</param>
        /// <param name="subdivisions">Number of subdivisions per edge.</param>
        /// <returns>New polygon vertices with additional subdivided points.</returns>
        private Vector2[] SubdividePolygon(Vector2[] polygon, int subdivisions)
        {
            var newPolygon = new List<Vector2>();

            for (int i = 0; i < polygon.Length; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % polygon.Length];
                newPolygon.Add(current);

                for (int j = 1; j <= subdivisions; j++)
                {
                    float t = (float)j / (subdivisions + 1);
                    Vector2 interpolated = current.Lerp(next, t);
                    newPolygon.Add(interpolated);
                }
            }

            return newPolygon.ToArray();
        }

        /// <summary>
        /// Draws a filled polygon with the specified color.
        /// </summary>
        /// <param name="points">The vertices of the polygon.</param>
        /// <param name="color">The fill color of the polygon.</param>
        public void DrawColoredPolygon(Vector2[] points, Color color)
        {
            // Validate the polygon
            if (points == null || points.Length < 3)
            {
                GD.PrintErr("Attempted to draw an invalid polygon with less than 3 points.");
                return; // Skip drawing this polygon
            }

            // Check for degenerate polygons (all points colinear)
            if (ArePointsColinear(points))
            {
                GD.PrintErr("Attempted to draw a degenerate polygon with colinear points.");
                return; // Skip drawing this polygon
            }

            // Optionally, you can add more validations here (e.g., no overlapping points)

            // Proceed to draw the polygon
            DrawPolygon(points, new Color[] { color }, null);
        }

        /// <summary>
        /// Checks if all points in the polygon are colinear.
        /// </summary>
        private bool ArePointsColinear(Vector2[] points)
        {
            if (points.Length < 3)
                return true;

            // Calculate the slope between the first two points
            float dx = points[1].X - points[0].X;
            float dy = points[1].Y - points[0].Y;

            if (dx == 0 && dy == 0)
                return true; // First two points are identical

            // Compare the slope of each subsequent pair with the initial slope
            for (int i = 2; i < points.Length; i++)
            {
                float currentDx = points[i].X - points[0].X;
                float currentDy = points[i].Y - points[0].Y;

                // To avoid division, compare cross multiplication
                if (dy * currentDx != dx * currentDy)
                    return false; // Found a point that breaks colinearity
            }

            return true; // All points are colinear
        }

        /// <summary>
        /// Draws ocean polygons on top of land polygons to create visual indentations.
        /// Each ocean polygon is scaled by 50% to make them larger.
        /// </summary>
        private void DrawOceanPolygons()
        {
            if (Map == null)
                return;

            // Define the scaling factor (1.5 for 50% increase)
            const float scalingFactor = 1.5f;

            foreach (var oceanShape in Map.OceanShapes)
            {
                if (oceanShape.Count < 3)
                    continue; // Invalid polygon

                // Scale the ocean polygon
                Vector2[] scaledOceanShape = ScalePolygon(oceanShape.ToArray(), scalingFactor);

                // Draw the scaled ocean polygon with the same semi-transparent blue color as the global ocean
                DrawColoredPolygon(scaledOceanShape, GlobalOceanColor); // Semi-transparent Blue
            }
        }

        /// <summary>
        /// Scales a polygon around its centroid by a given scaling factor.
        /// </summary>
        /// <param name="polygon">The original polygon vertices.</param>
        /// <param name="scaleFactor">The factor by which to scale the polygon.</param>
        /// <returns>A new array of Vector2 points representing the scaled polygon.</returns>
        private Vector2[] ScalePolygon(Vector2[] polygon, float scaleFactor)
        {
            if (polygon.Length == 0)
                return Array.Empty<Vector2>();
            
            // Calculate the centroid of the polygon
            Vector2 centroid = CalculateCentroid(polygon);
            
            var scaledPolygon = new Vector2[polygon.Length];
            
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 direction = polygon[i] - centroid;
                scaledPolygon[i] = centroid + direction * scaleFactor;
            }
            
            return scaledPolygon;
        }

        /// <summary>
        ///   Centers the map to the coordinates of current patch.
        /// </summary>
        /// <param name="smoothed">If true, smoothly pans the view to the destination, otherwise just snaps.</param>
        public void CenterToCurrentPatch(bool smoothed = true)
        {
            if (!isMainInstance)
                return;

            EmitSignal(SignalName.OnCurrentPatchCentered, PlayerPatch!.ScreenCoordinates, smoothed);
        }

        public void MarkDirty()
        {
            dirty = true;
        }

        /// <summary>
        ///   Stores patch node status values that will be applied when creating the patch nodes
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Note that this only works *before* the patch nodes are created, this doesn't apply retroactively
        ///   </para>
        /// </remarks>
        /// <param name="statuses">The enabled status values to store</param>
        public void SetPatchEnabledStatuses(Dictionary<Patch, bool> statuses)
        {
            patchEnableStatusesToBeApplied = statuses;
        }

        public void SetPatchEnabledStatuses(IEnumerable<Patch> patches, Func<Patch, bool> predicate)
        {
            SetPatchEnabledStatuses(patches.ToDictionary(x => x, predicate));
        }

        /// <summary>
        ///   Runs a function to determine what to set as the enabled status for all patch nodes
        /// </summary>
        /// <param name="predicate">
        ///   Predicate to run on all nodes and set the result to <see cref="PatchMapNode.Enabled"/>
        /// </param>
        public void ApplyPatchNodeEnabledStatus(Func<Patch, bool> predicate)
        {
            foreach (var (patch, node) in nodes)
            {
                node.Enabled = predicate(patch);
            }
        }

        /// <summary>
        ///   Sets patch node enabled status for all nodes
        /// </summary>
        /// <param name="enabled">Value to set to <see cref="PatchMapNode.Enabled"/></param>
        public void ApplyPatchNodeEnabledStatus(bool enabled)
        {
            foreach (var (_, node) in nodes)
            {
                node.Enabled = enabled;
            }
        }

        /// <summary>
        ///   Update the patch event visuals on all created patch map nodes. Call if events change after initial graphics
        ///   init for this drawer.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     TODO: the auto-evo exploring tool needs to call this to show things properly
        ///   </para>
        /// </remarks>
        public void UpdatePatchEvents()
        {
            foreach (var (patch, node) in nodes)
            {
                patch.ApplyPatchEventVisuals(node);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (PatchNodeContainerPath != null)
                {
                    PatchNodeContainerPath.Dispose();
                    LineContainerPath.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private static Vector2 ClosestPoint(Vector2 comparisonPoint, Vector2 point1, Vector2 point2)
        {
            return point1.DistanceSquaredTo(comparisonPoint) > point2.DistanceSquaredTo(comparisonPoint) ? point2 : point1;
        }

        /// <summary>
        ///   If two segments parallel to axis intersect each other.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     True if intersect at endpoint. And true if the two segments are collinear and has common points.
        ///   </para>
        ///   <para>
        ///     Doesn't use Geometry.SegmentIntersectsSegment2d() because it isn't handling intersection at endpoint well.
        ///   </para>
        /// </remarks>
        /// <returns>True if intersect</returns>
        private static bool SegmentSegmentIntersects(Vector2 segment1Start, Vector2 segment1End,
            Vector2 segment2Start, Vector2 segment2End)
        {
            if (Math.Abs(segment1Start.X - segment1End.X) < MathUtils.EPSILON)
            {
                var segment1Greater = Math.Max(segment1Start.Y, segment1End.Y);
                var segment1Smaller = Math.Min(segment1Start.Y, segment1End.Y);

                if (Math.Abs(segment2Start.X - segment2End.X) < MathUtils.EPSILON)
                {
                    var segment2Greater = Math.Max(segment2Start.Y, segment2End.Y);
                    var segment2Smaller = Math.Min(segment2Start.Y, segment2End.Y);

                    return Math.Abs(segment1Start.X - segment2Start.X) < MathUtils.EPSILON &&
                        !(Math.Max(segment1Smaller, segment2Smaller) - Math.Min(segment1Greater, segment2Greater) >
                            MathUtils.EPSILON);
                }
                else
                {
                    if (!(Math.Abs(segment2Start.Y - segment2End.Y) < MathUtils.EPSILON))
                        throw new InvalidOperationException("Segment2 isn't parallel to axis!");

                    var segment2Greater = Math.Max(segment2Start.X, segment2End.X);
                    var segment2Smaller = Math.Min(segment2Start.X, segment2End.X);

                    return segment1Greater - segment2Start.Y > -MathUtils.EPSILON &&
                        segment2Start.Y - segment1Smaller > -MathUtils.EPSILON &&
                        segment2Greater - segment1Start.X > -MathUtils.EPSILON &&
                        segment1Start.X - segment2Smaller > -MathUtils.EPSILON;
                }
            }
            else
            {
                if (!(Math.Abs(segment1Start.Y - segment1End.Y) < MathUtils.EPSILON))
                    throw new InvalidOperationException("Segment1 isn't parallel to axis!");

                var segment1Greater = Math.Max(segment1Start.X, segment1End.X);
                var segment1Smaller = Math.Min(segment1Start.X, segment1End.X);

                if (Math.Abs(segment2Start.Y - segment2End.Y) < MathUtils.EPSILON)
                {
                    var segment2Greater = Math.Max(segment2Start.X, segment2End.X);
                    var segment2Smaller = Math.Min(segment2Start.X, segment2End.X);

                    return Math.Abs(segment1Start.Y - segment2Start.Y) < MathUtils.EPSILON &&
                        !(Math.Max(segment1Smaller, segment2Smaller) - Math.Min(segment1Greater, segment2Greater) >
                            MathUtils.EPSILON);
                }
                else
                {
                    if (!(Math.Abs(segment2Start.X - segment2End.X) < MathUtils.EPSILON))
                        throw new InvalidOperationException("Segment2 isn't parallel to axis!");

                    var segment2Greater = Math.Max(segment2Start.Y, segment2End.Y);
                    var segment2Smaller = Math.Min(segment2Start.Y, segment2End.Y);

                    return segment1Greater - segment2Start.X > -MathUtils.EPSILON &&
                        segment2Start.X - segment1Smaller > -MathUtils.EPSILON &&
                        segment2Greater - segment1Start.Y > -MathUtils.EPSILON &&
                        segment1Start.Y - segment2Smaller > -MathUtils.EPSILON;
                }
            }
        }

        private static bool SegmentRectangleIntersects(Vector2 start, Vector2 end, Rect2 rect)
        {
            var p0 = rect.Position;
            var p1 = rect.Position + new Vector2(0, rect.Size.Y);
            var p2 = rect.Position + new Vector2(rect.Size.X, 0);
            var p3 = rect.End;

            return SegmentSegmentIntersects(p0, p1, start, end) ||
                SegmentSegmentIntersects(p0, p2, start, end) ||
                SegmentSegmentIntersects(p1, p3, start, end) ||
                SegmentSegmentIntersects(p2, p3, start, end);
        }

        private static Vector2 RegionCenter(PatchRegion region)
        {
            return new Vector2(region.ScreenCoordinates.X + region.Width * 0.5f,
                region.ScreenCoordinates.Y + region.Height * 0.5f);
        }

        private static Vector2 PatchCenter(Vector2 pos)
        {
            return new Vector2(pos.X + Constants.PATCH_NODE_RECT_LENGTH * 0.5f,
                pos.Y + Constants.PATCH_NODE_RECT_LENGTH * 0.5f);
        }

        private Line2D CreateConnectionLine(Vector2[] points, Color connectionColor)
        {
            var link = new Line2D
            {
                DefaultColor = connectionColor,
                Points = points,
                Width = Constants.PATCH_REGION_CONNECTION_LINE_WIDTH,
            };

            lineContainer.AddChild(link);

            return link;
        }

        private void ApplyFadeToLine(Line2D line, bool reversed)
        {
            // TODO: it seems just a few gradients are used, so these should be able to be cached
            var gradient = new Gradient();
            var color = line.DefaultColor;
            Color transparent = new(color, 0);

            gradient.AddPoint(reversed ? 0.3f : 0.7f, transparent);

            gradient.SetColor(reversed ? 2 : 0, color);
            gradient.SetColor(reversed ? 0 : 2, transparent);
            line.Gradient = gradient;
        }

        private void DrawNodeLink(Vector2 center1, Vector2 center2, Color connectionColor)
        {
            DrawLine(center1, center2, connectionColor, Constants.PATCH_REGION_CONNECTION_LINE_WIDTH, true);
        }

        private PatchMapNode? GetPatchNode(Patch patch)
        {
            nodes.TryGetValue(patch, out var node);
            return node;
        }

        private bool ContainsSelectedPatch(PatchRegion region)
        {
            return region.Patches.Any(p => GetPatchNode(p)?.Selected == true);
        }

        private bool ContainsAdjacentToSelectedPatch(PatchRegion region)
        {
            return region.Patches.Any(p => GetPatchNode(p)?.AdjacentToSelectedPatch == true);
        }

        private bool CheckHighlightedAdjacency(PatchRegion region1, PatchRegion region2)
        {
            return (ContainsSelectedPatch(region1) && ContainsAdjacentToSelectedPatch(region2)) ||
                (ContainsSelectedPatch(region2) && ContainsAdjacentToSelectedPatch(region1));
        }

        private Vector2 GetRightBottomCornerPointOnMap()
        {
            if (map == null || map.Regions == null)
            {
                GD.PrintErr("Map or Map.Regions is null in GetRightBottomCornerPointOnMap");
                return Vector2.Zero;
            }

            var point = Vector2.Zero;

            foreach (var region in map.Regions)
            {
                if (region.Value == null)
                    continue;

                var regionEnd = region.Value.ScreenCoordinates + region.Value.Size;

                point.X = Math.Max(point.X, regionEnd.X);
                point.Y = Math.Max(point.Y, regionEnd.Y);
            }

            return point;
        }

        public Rect2 GetPlanetMapBounds()
    {
        // Return the background rectangle
        return backgroundRect;
    }

        /// <summary>
        ///   This function creates least intersected links to adjoining regions.
        /// </summary>
        private void CreateRegionLinks()
        {
            var mapCenter = map.Center;

            // When ordered by distance to center, central regions will be linked first, which reduces intersections.
            foreach (var region in map.Regions.Values.OrderBy(r => mapCenter.DistanceSquaredTo(r.ScreenCoordinates)))
            {
                foreach (var adjacent in region.Adjacent)
                {
                    var connectionKey = new Vector2I(region.ID, adjacent.ID);
                    var reverseConnectionKey = new Vector2I(adjacent.ID, region.ID);

                    if (connections.ContainsKey(connectionKey) || connections.ContainsKey(reverseConnectionKey))
                        continue;

                    var pathToAdjacent = GetLeastIntersectingPath(region, adjacent);

                    connections.Add(connectionKey, pathToAdjacent);
                }
            }

            AdjustPathEndpoints();
        }

        /// <summary>
        ///   Get the least intersecting path from start region to end region. This is achieved by first calculating all
        ///   possible paths, then figuring out which one intersects the least. If several paths are equally good, return
        ///   the one with highest priority.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Priority: Direct path > L-shape path > Z-shape path > U-shape path
        ///   </para>
        /// </remarks>
        /// <returns>Path represented in a Vector2 array</returns>
        private Vector2[] GetLeastIntersectingPath(PatchRegion start, PatchRegion end)
        {
            var startCenter = RegionCenter(start);
            var startRect = new Rect2(start.ScreenCoordinates, start.Size);
            var endCenter = RegionCenter(end);
            var endRect = new Rect2(end.ScreenCoordinates, end.Size);

            // TODO: it would be pretty nice to be able to use a buffer pool for the path points here as a ton of memory
            // is re-allocated here each time the map needs drawing
            var probablePaths = new List<(Vector2[] Path, int Priority)>();

            // Direct line, I shape, highest priority
            if (Math.Abs(startCenter.X - endCenter.X) < MathUtils.EPSILON ||
                Math.Abs(startCenter.Y - endCenter.Y) < MathUtils.EPSILON)
            {
                probablePaths.Add((new[] { startCenter, endCenter }, 3));
            }

            // 2-segment line, L shape
            var intermediate = new Vector2(startCenter.X, endCenter.Y);
            if (!startRect.HasPoint(intermediate) && !endRect.HasPoint(intermediate))
                probablePaths.Add((new[] { startCenter, intermediate, endCenter }, 2));

            intermediate = new Vector2(endCenter.X, startCenter.Y);
            if (!startRect.HasPoint(intermediate) && !endRect.HasPoint(intermediate))
                probablePaths.Add((new[] { startCenter, intermediate, endCenter }, 2));

            // 3-segment lines consider relative position
            var upper = startRect.Position.Y < endRect.Position.Y ? startRect : endRect;
            var lower = startRect.End.Y > endRect.End.Y ? startRect : endRect;
            var left = startRect.Position.X < endRect.Position.X ? startRect : endRect;
            var right = startRect.End.X > endRect.End.X ? startRect : endRect;

            // 3-segment line, Z shape
            var middlePoint = new Vector2(left.End.X + right.Position.X, upper.End.Y + lower.Position.Y) / 2.0f;

            var intermediate1 = new Vector2(startCenter.X, middlePoint.Y);
            var intermediate2 = new Vector2(endCenter.X, middlePoint.Y);
            if (!startRect.HasPoint(intermediate1) && !endRect.HasPoint(intermediate2))
                probablePaths.Add((new[] { startCenter, intermediate1, intermediate2, endCenter }, 1));

            intermediate1 = new Vector2(middlePoint.X, startCenter.Y);
            intermediate2 = new Vector2(middlePoint.X, endCenter.Y);
            if (!startRect.HasPoint(intermediate1) && !endRect.HasPoint(intermediate2))
                probablePaths.Add((new[] { startCenter, intermediate1, intermediate2, endCenter }, 1));

            // 3-segment line, U shape
            for (int i = 1; i <= 3; ++i)
            {
                intermediate1 = new Vector2(startCenter.X, lower.End.Y + i * 50);
                intermediate2 = new Vector2(endCenter.X, lower.End.Y + i * 50);
                probablePaths.Add((new[] { startCenter, intermediate1, intermediate2, endCenter }, -i));

                intermediate1 = new Vector2(startCenter.X, upper.Position.Y - i * 50);
                intermediate2 = new Vector2(endCenter.X, upper.Position.Y - i * 50);
                probablePaths.Add((new[] { startCenter, intermediate1, intermediate2, endCenter }, -i));

                intermediate1 = new Vector2(right.End.X + i * 50, startCenter.Y);
                intermediate2 = new Vector2(right.End.X + i * 50, endCenter.Y);
                probablePaths.Add((new[] { startCenter, intermediate1, intermediate2, endCenter }, -i));

                intermediate1 = new Vector2(left.Position.X - i * 50, startCenter.Y);
                intermediate2 = new Vector2(left.Position.X - i * 50, endCenter.Y);
                probablePaths.Add((new[] { startCenter, intermediate1, intermediate2, endCenter }, -i));
            }

            // Choose a best path
            return probablePaths.Select(p => (p.Path, CalculatePathPriorityTuple(p)))
                .OrderBy(p => p.Item2.RegionIntersectionCount)
                .ThenBy(p => p.Item2.PathIntersectionCount)
                .ThenBy(p => p.Item2.StartPointOverlapCount)
                .ThenByDescending(p => p.Item2.Priority)
                .First().Path;
        }

        /// <summary>
        ///   Add a separation between each overlapped line, and adjust connection endpoint
        /// </summary>
        private void AdjustPathEndpoints()
        {
            foreach (var region in Map!.Regions)
            {
                int regionId = region.Key;
                var connectionStartHere = connections.Where(p => p.Key.X == regionId);
                var connectionEndHere = connections.Where(p => p.Key.Y == regionId);

                var connectionTupleList = connectionStartHere.Select(c => (c.Value, 0, 1)).ToList();
                connectionTupleList.AddRange(
                    connectionEndHere.Select(c => (c.Value, c.Value.Length - 1, c.Value.Length - 2)));

                // Separate connection by directions: 0 -> Left, 1 -> Up, 2 -> Right, 3 -> Down
                // TODO: refactor this to use an enum
                var connectionsToDirections = new List<(Vector2[] Path, int Endpoint, int Intermediate, float Distance)>[4];

                for (int i = 0; i < 4; ++i)
                {
                    connectionsToDirections[i] =
                        new List<(Vector2[] Path, int Endpoint, int Intermediate, float Distance)>();
                }

                foreach (var (path, endpoint, intermediate) in connectionTupleList)
                {
                    if (Math.Abs(path[endpoint].X - path[intermediate].X) < MathUtils.EPSILON)
                    {
                        connectionsToDirections[path[endpoint].Y > path[intermediate].Y ? 1 : 3].Add((
                            path, endpoint, intermediate,
                            Math.Abs(path[endpoint].Y - path[intermediate].Y)));
                    }
                    else
                    {
                        connectionsToDirections[path[endpoint].X > path[intermediate].X ? 0 : 2].Add((
                            path, endpoint, intermediate,
                            Math.Abs(path[endpoint].X - path[intermediate].X)));
                    }
                }

                var halfBorderWidth = Constants.PATCH_REGION_BORDER_WIDTH / 2;

                // Endpoint position
                foreach (var (path, endpoint, _, _) in connectionsToDirections[0])
                {
                    path[endpoint].X -= region.Value.Width / 2 + halfBorderWidth;
                }

                foreach (var (path, endpoint, _, _) in connectionsToDirections[1])
                {
                    path[endpoint].Y -= region.Value.Height / 2 + halfBorderWidth;
                }

                foreach (var (path, endpoint, _, _) in connectionsToDirections[2])
                {
                    path[endpoint].X += region.Value.Width / 2 + halfBorderWidth;
                }

                foreach (var (path, endpoint, _, _) in connectionsToDirections[3])
                {
                    path[endpoint].Y += region.Value.Height / 2 + halfBorderWidth;
                }

                // Separation
                const float lineSeparation = 4 * Constants.PATCH_REGION_CONNECTION_LINE_WIDTH;

                for (int direction = 0; direction < 4; ++direction)
                {
                    var connectionsToDirection = connectionsToDirections[direction];

                    // Only when we have more than 1 connections do we need to offset them
                    if (connectionsToDirection.Count <= 1)
                        continue;

                    if (direction is 1 or 3)
                    {
                        float right = (connectionsToDirection.Count - 1) / 2.0f;
                        float left = -right;

                        foreach (var (path, endpoint, intermediate, _) in
                                connectionsToDirection.OrderBy(t => t.Distance))
                        {
                            if (path.Length == 2 || path[2 * intermediate - endpoint].X > path[intermediate].X)
                            {
                                path[endpoint].X += lineSeparation * right;
                                path[intermediate].X += lineSeparation * right;
                                right -= 1;
                            }
                            else
                            {
                                path[endpoint].X += lineSeparation * left;
                                path[intermediate].X += lineSeparation * left;
                                left += 1;
                            }
                        }
                    }
                    else
                    {
                        float down = (connectionsToDirection.Count - 1) / 2.0f;
                        float up = -down;

                        foreach (var (path, endpoint, intermediate, _) in
                                connectionsToDirection.OrderBy(t => t.Distance))
                        {
                            if (path.Length == 2 || path[2 * intermediate - endpoint].Y > path[intermediate].Y)
                            {
                                path[endpoint].Y += lineSeparation * down;
                                path[intermediate].Y += lineSeparation * down;
                                down -= 1;
                            }
                            else
                            {
                                path[endpoint].Y += lineSeparation * up;
                                path[intermediate].Y += lineSeparation * up;
                                up += 1;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///   Calculate priority of a path for sorting.
        /// </summary>
        private (int RegionIntersectionCount, int PathIntersectionCount, int StartPointOverlapCount, int Priority)
            CalculatePathPriorityTuple((Vector2[] Path, int Priority) pathPriorityTuple)
        {
            var (path, priority) = pathPriorityTuple;

            // Intersections with regions are considered worse than that with lines.
            // So an intersect with region adds count by 10.
            int regionIntersectionCount = 0;
            int pathIntersectionCount = 0;
            int startPointOverlapCount = 0;

            for (int i = 1; i < path.Length; ++i)
            {
                var startPoint = path[i - 1];
                var endPoint = path[i];

                foreach (var region in map.Regions.Values)
                {
                    var regionRect = new Rect2(region.ScreenCoordinates, region.Size);
                    if (SegmentRectangleIntersects(startPoint, endPoint, regionRect))
                    {
                        ++regionIntersectionCount;
                    }
                }
            }

            // Calculate line-to-line intersections
            foreach (var target in connections.Values)
            {
                for (int i = 1; i < path.Length; ++i)
                {
                    var startPoint = path[i - 1];
                    var endPoint = path[i];

                    for (int j = 1; j < target.Length; ++j)
                    {
                        if (SegmentSegmentIntersects(startPoint, endPoint, target[j - 1], target[j]))
                            ++pathIntersectionCount;
                    }
                }

                // If the endpoint is the same, it is regarded as the two lines intersects but it actually isn't.
                if (path[0] == target[0])
                {
                    --pathIntersectionCount;

                    // And if they goes the same direction, the second segment intersects but it actually isn't either.
                    if (Math.Abs((path[1] - path[0]).AngleTo(target[1] - target[0])) < MathUtils.EPSILON)
                    {
                        --pathIntersectionCount;
                        ++startPointOverlapCount;
                    }
                }
                else if (path[0] == target[target.Length - 1])
                {
                    --pathIntersectionCount;

                    if (Math.Abs((path[1] - path[0]).AngleTo(target[target.Length - 2] - target[target.Length - 1]))
                        < MathUtils.EPSILON)
                    {
                        --pathIntersectionCount;
                        ++startPointOverlapCount;
                    }
                }
                else if (path[path.Length - 1] == target[0])
                {
                    --pathIntersectionCount;

                    if (Math.Abs((path[path.Length - 2] - path[path.Length - 1]).AngleTo(target[1] - target[0]))
                        < MathUtils.EPSILON)
                    {
                        --pathIntersectionCount;
                        ++startPointOverlapCount;
                    }
                }
                else if (path[path.Length - 1] == target[target.Length - 1])
                {
                    --pathIntersectionCount;

                    if (Math.Abs((path[path.Length - 2] - path[path.Length - 1]).AngleTo(target[target.Length - 2] -
                            target[target.Length - 1])) < MathUtils.EPSILON)
                    {
                        --pathIntersectionCount;
                        ++startPointOverlapCount;
                    }
                }
            }

            // The highest priority has the lowest value.
            return (regionIntersectionCount, pathIntersectionCount, startPointOverlapCount, priority);
        }

        private void DrawRegionBorders()
        {
            // Don't draw a border if there's only one region
            if (map.Regions.Count == 1)
                return;

            foreach (var region in map.Regions.Values)
            {
                // Don't draw borders for hidden regions
                if (region.Visibility != MapElementVisibility.Shown)
                    continue;

                DrawRect(new Rect2(region.ScreenCoordinates, region.Size),
                    Colors.DarkCyan, false, Constants.PATCH_REGION_BORDER_WIDTH);
            }
        }

        private void DrawPatchLinks()
        {
            // This ends up drawing duplicates but that doesn't seem problematic ATM
            foreach (var patch in Map!.Patches.Values)
            {
                foreach (var adjacent in patch.Adjacent)
                {
                    // Do not draw connections to/from hidden patches
                    if (patch.Visibility == MapElementVisibility.Hidden ||
                        adjacent.Visibility == MapElementVisibility.Hidden)
                    {
                        continue;
                    }

                    // Only draw connections if patches belong to the same region
                    if (patch.Region.ID == adjacent.Region.ID)
                    {
                        var start = PatchCenter(patch.ScreenCoordinates);
                        var end = PatchCenter(adjacent.ScreenCoordinates);

                        DrawNodeLink(start, end, DefaultConnectionColor);
                    }
                }
            }
        }

        /// <summary>
        ///   Clears the map and rebuilds all nodes
        /// </summary>
        private void RebuildMap()
        {
            // Clear existing polygons
            ventMainPolygons.Clear();
            ventStepPolygons.Clear();
            ventBufferPolygons.Clear();

            trenchMainPolygons.Clear();
            trenchStepPolygons.Clear();
            trenchBufferPolygons.Clear();

            // Generate vent and trench polygons
            GenerateVentPolygons();
            GenerateTrenchPolygons();

            // Clear existing nodes and connections
            patchNodeContainer.FreeChildren();
            nodes.Clear();
            connections.Clear();

            if (Map == null)
            {
                SelectedPatch = null;
                return;
            }

            // **Only add patch nodes if this is the main instance**
            if (isMainInstance)
            {
                foreach (var entry in Map.Patches)
                {
                    AddPatchNode(entry.Value, entry.Value.ScreenCoordinates);
                }
            }

            // **Only rebuild region connections if this is the main instance**
            if (isMainInstance)
            {
                RebuildRegionConnections();
            }

            bool runNodeSelectionsUpdate = true;

            if (SelectedPatch != null)
            {
                // Unset the selected patch if it was removed from the map
                bool found = false;
                foreach (var node in nodes.Values)
                {
                    if (node.Patch == SelectedPatch)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    SelectedPatch = null;
                    // Changing the selected patch already updates the node selections so we skip a duplicate call with this flag
                    runNodeSelectionsUpdate = false;
                }
            }

            if (runNodeSelectionsUpdate && isMainInstance)
                UpdateNodeSelections();
        }

        private void GenerateVentPolygons()
        {
            if (Map == null)
                return;

            // Collect all vent patches
            var ventPatches = Map.Patches.Values
                .Where(p => p.BiomeType == BiomeType.Vents)
                .OrderBy(p => p.ID) // Ensure deterministic ordering
                .ToList();

            // Define a maximum distance to consider patches as adjacent
            float maxDistance = 1000f; // Adjust as necessary

            // Keep track of which vents have been connected
            var connectedVents = new HashSet<int>();

            for (int i = 0; i < ventPatches.Count; i++)
            {
                var patchA = ventPatches[i];
                if (connectedVents.Contains(patchA.ID))
                    continue; // Already connected

                // Find the closest vent patch to patchA that is not yet connected
                Patch? closestPatch = null;
                float closestDistance = float.MaxValue;

                for (int j = 0; j < ventPatches.Count; j++)
                {
                    if (i == j)
                        continue;

                    var patchB = ventPatches[j];
                    if (connectedVents.Contains(patchB.ID))
                        continue; // Already connected

                    float distance = patchA.ScreenCoordinates.DistanceTo(patchB.ScreenCoordinates);
                    if (distance < closestDistance && distance <= maxDistance)
                    {
                        closestDistance = distance;
                        closestPatch = patchB;
                    }
                }

                if (closestPatch != null)
                {
                    // Calculate the center positions of both patches
                    Vector2 start = PatchCenter(patchA.ScreenCoordinates);
                    Vector2 end = PatchCenter(closestPatch.ScreenCoordinates);

                    // Generate a jagged line with desired segments and jaggedness
                    int ventSeed = patchA.ID ^ closestPatch.ID ^ (int)(Map.Settings.Seed % int.MaxValue);
                    Random rand = new Random(ventSeed);

                    List<Vector2> jaggedPoints = GenerateJaggedLine(
                        start,
                        end,
                        segments: VentLineSegments,         // Number of segments from exported property
                        jaggedness: VentLineJaggedness,    // Jaggedness from exported property
                        rand
                    );

                    // Generate layered polygons around the jagged line
                    var layeredPolygons = GenerateLayeredPolygons(
                        jaggedPoints,
                        mainWidth: VentPolygonWidth,             // Main polygon width
                        stepOffset: 5.0f,                        // Step layer offset (adjust as needed)
                        bufferWidth: VentPolygonWidth + 50.0f,   // Buffer polygon wider than main
                        rand
                    );

                    // Add polygons to their respective lists
                    if (layeredPolygons.Main.Length > 0)
                        ventMainPolygons.Add(layeredPolygons.Main);

                    if (layeredPolygons.Step.Length > 0)
                        ventStepPolygons.Add(layeredPolygons.Step);

                    if (layeredPolygons.Buffer.Length > 0)
                        ventBufferPolygons.Add(layeredPolygons.Buffer);

                    // Mark only the first patch as connected, the second one can connect to another and form a chain
                    connectedVents.Add(patchA.ID);
                }
            }
        }

        private void GenerateTrenchPolygons()
        {
            if (Map == null)
                return;

            // Collect all ocean trench patches
            var trenchPatches = Map.Patches.Values
                .Where(p => p.BiomeType == BiomeType.Hadopelagic) // Ensure BiomeType.Hadopelagic is correct
                .OrderBy(p => p.ID) // Ensure deterministic ordering
                .ToList();

            // Define a maximum distance to consider patches as adjacent
            float maxDistance = 1500f; // Adjust as necessary

            // Keep track of which trenches have been connected
            var connectedTrenches = new HashSet<int>();

            for (int i = 0; i < trenchPatches.Count; i++)
            {
                var patchA = trenchPatches[i];
                if (connectedTrenches.Contains(patchA.ID))
                    continue; // Already connected

                // Find the closest trench patch to patchA that is not yet connected
                Patch? closestPatch = null;
                float closestDistance = float.MaxValue;

                for (int j = 0; j < trenchPatches.Count; j++)
                {
                    if (i == j)
                        continue;

                    var patchB = trenchPatches[j];
                    if (connectedTrenches.Contains(patchB.ID))
                        continue; // Already connected

                    float distance = patchA.ScreenCoordinates.DistanceTo(patchB.ScreenCoordinates);
                    if (distance < closestDistance && distance <= maxDistance)
                    {
                        closestDistance = distance;
                        closestPatch = patchB;
                    }
                }

                if (closestPatch != null)
                {
                    // Calculate the center positions of both patches
                    Vector2 start = PatchCenter(patchA.ScreenCoordinates);
                    Vector2 end = PatchCenter(closestPatch.ScreenCoordinates);

                    // Generate a jagged line with desired segments and jaggedness
                    int trenchSeed = patchA.ID ^ closestPatch.ID ^ (int)(Map.Settings.Seed % int.MaxValue);
                    Random rand = new Random(trenchSeed);

                    List<Vector2> jaggedPoints = GenerateJaggedLine(
                        start,
                        end,
                        segments: TrenchLineSegments,         // Number of segments from exported property
                        jaggedness: TrenchLineJaggedness,    // Jaggedness from exported property
                        rand
                    );

                    // Generate layered polygons around the jagged line
                    var layeredPolygons = GenerateLayeredPolygons(
                        jaggedPoints,
                        mainWidth: TrenchPolygonWidth,             // Main polygon width
                        stepOffset: 5.0f,                           // Step layer offset (adjust as needed)
                        bufferWidth: TrenchPolygonWidth + 10.0f,    // Buffer polygon wider than main
                        rand
                    );

                    // Add polygons to their respective lists
                    if (layeredPolygons.Main.Length > 0)
                        trenchMainPolygons.Add(layeredPolygons.Main);

                    if (layeredPolygons.Step.Length > 0)
                        trenchStepPolygons.Add(layeredPolygons.Step);

                    if (layeredPolygons.Buffer.Length > 0)
                        trenchBufferPolygons.Add(layeredPolygons.Buffer);

                    // Mark only the first patch as connected, the second one can connect to another and form a chain
                    connectedTrenches.Add(patchA.ID);
                }
            }
        }

        private void AddPatchNode(Patch patch, Vector2 position)
        {
            var node = nodeScene.Instantiate<PatchMapNode>();
            node.OffsetLeft = position.X;
            node.OffsetTop = position.Y;
            node.Size = new Vector2(Constants.PATCH_NODE_RECT_LENGTH, Constants.PATCH_NODE_RECT_LENGTH);

            node.Patch = patch;
            node.PatchIcon = patch.BiomeTemplate.LoadedIcon;

            node.MonochromeMaterial = MonochromeMaterial;

            node.SelectCallback = clicked => { SelectedPatch = clicked.Patch; };

            node.Enabled = patchEnableStatusesToBeApplied?[patch] ?? true;

            patch.ApplyPatchEventVisuals(node);

            patchNodeContainer.AddChild(node);
            nodes.Add(node.Patch, node);
        }

        private void RebuildRegionConnections()
        {
            // Clear existing connection lines
            lineContainer.FreeChildren();

            foreach (var entry in connections)
            {
                var region1 = map.Regions[entry.Key.X];
                var region2 = map.Regions[entry.Key.Y];

                var visibility1 = region1.Visibility;
                var visibility2 = region2.Visibility;

                // Do not draw connections between hidden or unknown regions
                if (visibility1 != MapElementVisibility.Shown && visibility2 != MapElementVisibility.Shown)
                    continue;

                // Check if connections should be highlighted
                // (Unknown patches should not highlight connections)
                var highlight = CheckHighlightedAdjacency(region1, region2) &&
                    SelectedPatch?.Visibility == MapElementVisibility.Shown;

                var color = highlight ? HighlightedConnectionColor : DefaultConnectionColor;

                // Create the main connection line
                var points = entry.Value;
                var line = CreateConnectionLine(points, color);

                // Fade the connection line if need be
                ApplyFadeIfNeeded(region1, region2, line, false);
                ApplyFadeIfNeeded(region2, region1, line, true);

                // Create additional lines to connect "floating" patches in unknown regions
                if (visibility1 == MapElementVisibility.Unknown)
                    BuildUnknownRegionConnections(line, region1, region2, color, false);

                if (visibility2 == MapElementVisibility.Unknown)
                    BuildUnknownRegionConnections(line, region2, region1, color, true);
            }
        }

        private void BuildUnknownRegionConnections(Line2D startingConnection, PatchRegion targetRegion,
            PatchRegion startRegion, Color color, bool reversed)
        {
            var startingPoint = reversed ?
                startingConnection.Points[startingConnection.Points.Length - 1] :
                startingConnection.Points[0];

            var adjacencies = startRegion.PatchAdjacencies[targetRegion.ID];

            // Generate a list of patches to connect to
            var patches = targetRegion.Patches
                .Where(p => p.Visibility == MapElementVisibility.Unknown)
                .Where(p => adjacencies.Contains(p));

            foreach (var targetPatch in patches)
            {
                var patchSize = Vector2.One * Constants.PATCH_NODE_RECT_LENGTH;
                var endingPoint = targetPatch.ScreenCoordinates + patchSize / 2;

                // Draw a straight line if possible
                if (endingPoint.X == startingPoint.X || endingPoint.Y == startingPoint.Y)
                {
                    var straightPoints = new[]
                    {
                        startingPoint,
                        endingPoint,
                    };

                    CreateConnectionLine(straightPoints, color);
                    continue;
                }

                var intermediate = new Vector2(endingPoint.X, startingPoint.Y);

                // Make sure the new point is not covered by the patch itself
                var targetPatchRect = new Rect2(targetPatch.ScreenCoordinates, patchSize);
                if (targetPatchRect.HasPoint(intermediate))
                    intermediate = new Vector2(startingPoint.X, endingPoint.Y);

                var points = new[]
                {
                    startingPoint,
                    intermediate,
                    endingPoint,
                };

                CreateConnectionLine(points, color);
            }
        }

        private void ApplyFadeIfNeeded(PatchRegion startingRegion, PatchRegion endingRegion,
            Line2D line, bool reversed)
        {
            // Do not apply fade from hidden or unknown region
            if (startingRegion.Visibility != MapElementVisibility.Shown)
                return;

            // Do not apply fade if target region is visible
            if (endingRegion.Visibility == MapElementVisibility.Shown)
                return;

            // Apply fade if target region is hidden
            if (endingRegion.Visibility == MapElementVisibility.Hidden)
            {
                ApplyFadeToLine(line, reversed);
                return;
            }

            // Apply fade only if no connecting patches are visible in the target region
            var adjacencies = startingRegion.PatchAdjacencies[endingRegion.ID];

            if (!endingRegion.Patches
                    .Where(p => p.Visibility == MapElementVisibility.Unknown)
                    .Any(p => adjacencies.Contains(p)))
            {
                ApplyFadeToLine(line, reversed);
            }
        }

        private void UpdateNodeSelections()
        {
            foreach (var node in nodes.Values)
            {
                node.Selected = node.Patch == SelectedPatch;
                node.Marked = node.Patch == playerPatch;

                if (SelectedPatch != null)
                    node.AdjacentToSelectedPatch = SelectedPatch.Adjacent.Contains(node.Patch);
            }
        }

        private void NotifySelectionChanged()
        {
            if (isMainInstance)
            {
                OnSelectedPatchChanged?.Invoke(this);
            }
        }

        private void CheckNodeSelectionUpdate()
        {
            bool needsUpdate = false;

            foreach (var node in nodes.Values)
            {
                if (node.SelectionDirty)
                {
                    needsUpdate = true;
                    break;
                }
            }

            if (needsUpdate)
            {
                UpdateNodeSelections();

                foreach (var node in nodes.Values)
                {
                    if (SelectedPatch == null)
                        node.AdjacentToSelectedPatch = false;

                    node.UpdateSelectionState();
                }

                // Also needs to update the lines connecting patches for those to display properly
                // TODO: would be really nice to be able to just update the line objects without redoing them all
                RebuildRegionConnections();
            }
        }

        public Vector2 GetContentSize()
        {
            // Return the size of the background rectangle
            return backgroundRect.Size;
        }

        public async Task<Texture2D> GetPlanetMapTextureAsync()
        {
            GD.Print("GetPlanetMapTextureAsync called");

            // Load the PackedScene for PatchMapDrawer
            var patchMapDrawerScene = GD.Load<PackedScene>("res://src/microbe_stage/editor/PatchMapDrawer.tscn");

            // Instance the PatchMapDrawer
            var patchMapDrawerInstance = patchMapDrawerScene.Instantiate<PatchMapDrawer>();

            // Set isMainInstance to false to prevent recursion and adjust rendering
            patchMapDrawerInstance.isMainInstance = false;

            // Assign the Map property to the new instance
            patchMapDrawerInstance.Map = this.Map;

            // Set the position of the PatchMapDrawer to (0, 0)
            patchMapDrawerInstance.Position = Vector2.Zero;

            // Force a redraw to ensure the latest map is rendered
            patchMapDrawerInstance.QueueRedraw();

            // Wait for the patchMapDrawerInstance to process and update
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // Calculate the planet map bounds
            Rect2 planetMapBounds = patchMapDrawerInstance.GetPlanetMapBounds();

            // Get the content size
            Vector2 contentSize = patchMapDrawerInstance.GetContentSize();

            // Set the size of the PatchMapDrawer to the content size
            patchMapDrawerInstance.Size = contentSize;

            // Desired output size (e.g., 2048x1024 for a 2:1 aspect ratio)
            Vector2 outputSize = new Vector2(2048, 1024); // Adjust as needed

            // Calculate the scaling factor to fit the content within the output size
            float scaleX = outputSize.X / contentSize.X;
            float scaleY = outputSize.Y / contentSize.Y;
            float scaleFactor = Math.Min(scaleX, scaleY);

            // Apply scaling and translation to the CanvasTransform
            Transform2D canvasTransform = Transform2D.Identity;

            // Apply scaling
            canvasTransform = canvasTransform.Scaled(new Vector2(scaleFactor, scaleFactor));

            // Adjust translation to center the content
            Vector2 scaledContentSize = contentSize * scaleFactor;
            Vector2 offset = (outputSize - scaledContentSize) / 2;

            // Adjust the translation to account for the planet map bounds
            canvasTransform = canvasTransform.Translated(offset - planetMapBounds.Position * scaleFactor);

            // Create a new SubViewport
            var subViewport = new SubViewport();

            // Set its size to the desired output size
            subViewport.Size = new Vector2I((int)outputSize.X, (int)outputSize.Y);

            // Configure the SubViewport
            subViewport.TransparentBg = true;
            subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once; // Use 'Once' to force a single update

            // Set the CanvasTransform
            subViewport.CanvasTransform = canvasTransform;

            // Add the new PatchMapDrawer instance to the SubViewport
            subViewport.AddChild(patchMapDrawerInstance);

            // Add the SubViewport to the scene tree temporarily
            GetTree().Root.AddChild(subViewport);

            // Wait for a few frames to ensure the SubViewport has rendered
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // Get the texture from the SubViewport
            var texture = subViewport.GetTexture();

            // Remove the SubViewport from the scene tree
            subViewport.GetParent()?.RemoveChild(subViewport);
            subViewport.QueueFree();

            return texture;
        }

        public async Task SavePlanetMapToFileAsync()
        {
            try
            {
                // Get the texture
                var texture = await GetPlanetMapTextureAsync();

                if (texture == null)
                {
                    GD.PrintErr("Texture is null. Cannot save planet map texture.");
                    return;
                }

                // Get the image data from the texture
                var image = texture.GetImage();

                if (image == null)
                {
                    GD.PrintErr("Image is null. Cannot save planet map texture.");
                    return;
                }

                // Get the world seed
                long seed = Map.Settings.Seed;

                // Get the current timestamp (optional)
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Define the file path with the seed and timestamp
                string directoryPath = "user://assets/generated/planet/";
                string fileName = $"planet_map_{seed}_{timestamp}.png";
                string filePath = $"{directoryPath}{fileName}";

                // Create the directory if it doesn't exist
                string absoluteDirectoryPath = ProjectSettings.GlobalizePath(directoryPath);
                Error dirErr = DirAccess.MakeDirRecursiveAbsolute(absoluteDirectoryPath);
                if (dirErr != Error.Ok && dirErr != Error.AlreadyExists)
                {
                    GD.PrintErr($"Failed to create directory: {dirErr}");
                    return;
                }

                // Save the image as a PNG file
                Error err = image.SavePng(filePath);

                if (err != Error.Ok)
                {
                    GD.PrintErr($"Failed to save planet map texture to file: {err}");
                }
                else
                {
                    GD.Print($"Planet map texture saved to: {filePath}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Exception occurred while saving image: {ex.Message}");
            }
        }
    }