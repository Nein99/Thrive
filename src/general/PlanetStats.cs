using Godot;
using System;

/// <summary>
/// Singleton to hold planet statistics globally.
/// </summary>
public partial class PlanetStats : Node
{
    // Static Instance property
    public static PlanetStats Instance { get; private set; }
    // Define properties with backing fields
    private string name = "Akahn";
    private int patchRegionsCount = 9;
    private double mass = 1.00; // Earth masses
    private double radius = 1.00; // Earth radii
    private double surfaceArea = 1.00; // Earth surface areas
    private double gravity = 1.00; // Earth gravities
    private double averageTemp = 15.00; // °C
	private double humidity = 75.00; // °C

    // Signal emitted when any planet statistic changes
    [Signal]
    public delegate void PlanetStatsChangedEventHandler();

    // Property for Planet Name
    public string Name
    {
        get => name;
        set
        {
            if (name != value)
            {
                name = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    // Property for Patch Regions Count
    public int PatchRegionsCount
    {
        get => patchRegionsCount;
        set
        {
            if (patchRegionsCount != value)
            {
                patchRegionsCount = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    // Property for Mass
    public double Mass
    {
        get => mass;
        set
        {
            if (mass != value)
            {
                mass = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    // Property for Radius
    public double Radius
    {
        get =>  radius;
        set
        {
            if ( radius != value)
            {
                radius = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    // Property for Surface Area
    public double SurfaceArea
    {
        get => surfaceArea;
        set
        {
            if (surfaceArea != value)
            {
                surfaceArea = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    // Property for Gravity
    public double Gravity
    {
        get =>  gravity;
        set
        {
            if ( gravity != value)
            {
                gravity = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    // Property for Average Temperature
    public double AverageTemp
    {
        get => averageTemp;
        set
        {
            if (averageTemp != value)
            {
                averageTemp = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

	public double Humidity
    {
        get => humidity;
        set
        {
            if (humidity != value)
            {
                humidity = value;
                EmitSignal(nameof(PlanetStatsChanged));
            }
        }
    }

    /// <summary>
    /// Called when the node enters the scene tree for the first time.
    /// Assigns the singleton instance.
    /// </summary>
    public override void _Ready()
    {
        base._Ready();

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            GD.PrintErr("Multiple instances of PlanetStats detected. There should only be one singleton instance.");
            QueueFree(); // Remove the duplicate instance
        }
    }
}
