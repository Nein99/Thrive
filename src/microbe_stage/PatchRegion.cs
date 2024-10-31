using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   A region is something like a continent/ocean that contains multiple patches.
/// </summary>
[UseThriveSerializer]
[JsonObject(IsReference = true)]
public class PatchRegion
{
    public PatchRegion(int id, string name, RegionType regionType, Vector2 screenCoordinates)
    {
        ID = id;
        Patches = new List<Patch>();
        Name = name;
        Height = 0;
        Width = 0;
        Type = regionType;
        ScreenCoordinates = screenCoordinates;

        // Initialize the HotSpringAquiferPairs list
        HotSpringAquiferPairs = new List<(Patch HotSpring, Patch Aquifer)>();
    }

    [JsonConstructor]
    public PatchRegion(int id, string name, RegionType type, Vector2 screenCoordinates,
        float height, float width, List<(Patch HotSpring, Patch Aquifer)> hotSpringAquiferPairs)
    {
        ID = id;
        Name = name;
        Type = type;
        ScreenCoordinates = screenCoordinates;
        Height = height;
        Width = width;

        // Initialize the HotSpringAquiferPairs list from the deserialized data
        HotSpringAquiferPairs = hotSpringAquiferPairs ?? new List<(Patch HotSpring, Patch Aquifer)>();
    }

    public enum RegionType
    {
        Sea = 0,
        Ocean = 1,
        Continent = 2,
        Predefined = 3,
    }

    [JsonProperty]
    public RegionType Type { get; }

    [JsonProperty]
    public int ID { get; }

    /// <summary>
    ///   Regions this is adjacent to
    /// </summary>
    [JsonIgnore]
    public ISet<PatchRegion> Adjacent { get; } = new HashSet<PatchRegion>();

    [JsonIgnore]
    public Dictionary<int, HashSet<Patch>> PatchAdjacencies { get; } = new();

    [JsonProperty]
    public float Height { get; set; }

    [JsonProperty]
    public float Width { get; set; }

    [JsonIgnore]
    public Vector2 Size
    {
        get => new Vector2(Width, Height);
        set
        {
            Width = value.X;
            Height = value.Y;
        }
    }

    /// <summary>
    ///   The name of the region/continent
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     This is not translatable as this is just the output from the name generator, which isn't language-specific
    ///     currently. And even once it is, a different approach than <see cref="LocalizedString"/> will be needed to
    ///     allow randomly generated names to translate.
    ///   </para>
    /// </remarks>
    [JsonProperty]
    public string Name { get; private set; }

    /// <summary>
    ///   Coordinates where this region is to be displayed in the GUI
    /// </summary>
    [JsonProperty]
    public Vector2 ScreenCoordinates { get; set; }

    /// <summary>
    ///   The patches in this region. This is last because other constructor params need to be loaded from JSON first
    ///   and also this can't be a JSON constructor parameter because the patches refer to this so we couldn't
    ///   construct anything to begin with.
    /// </summary>
    [JsonProperty]
    public List<Patch> Patches { get; private set; } = null!;

    [JsonProperty]
    public MapElementVisibility Visibility { get; set; } = MapElementVisibility.Hidden;

    /// <summary>
    ///   List of hot spring and aquifer patch pairs in this region
    /// </summary>
    [JsonProperty]
    public List<(Patch HotSpring, Patch Aquifer)> HotSpringAquiferPairs { get; }

    /// <summary>
    ///   Adds a connection to another region
    /// </summary>
    /// <returns>True if this was a new connection, false if already connected</returns>
    public bool AddNeighbour(PatchRegion region)
    {
        return Adjacent.Add(region);
    }

    public void AddPatchAdjacency(PatchRegion region, Patch patch)
    {
        var id = region.ID;

        // Don't do this if the patch is in this region
        if (id == ID)
            return;

        if (!PatchAdjacencies.TryGetValue(id, out var set))
        {
            PatchAdjacencies[id] = new HashSet<Patch> { patch };
            return;
        }

        set.Add(patch);
    }
}
