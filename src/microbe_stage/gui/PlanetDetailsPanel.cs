using System;
using Godot;
using System.Globalization;

/// <summary>
/// Displays planet statistics in the PlanetDetailsPanel.
/// </summary>
public partial class PlanetDetailsPanel : PanelContainer
{
#pragma warning disable CA2213
    [Export]
    private Label planetName = null!;

    [Export]
    private Label patchRegions = null!;

    [Export]
    private Label planetMass = null!;

    [Export]
    private Label planetRadius = null!;

    [Export]
    private Label planetSurfaceArea = null!;

    [Export]
    private Label planetGravity = null!;

    [Export]
    private Label planetAvgTemp = null!;

    [Export]
    private Label planetHumidity = null!;

    // Reference to PlanetStats singleton
    private PlanetStats planetStats = null!;

    public override void _Ready()
    {
        base._Ready();

        // Access the PlanetStats singleton
        planetStats = PlanetStats.Instance;

        if (planetStats == null)
        {
            GD.PrintErr("PlanetStats singleton not found. Ensure it is registered in Project Settings > Autoload.");
            return;
        }

        // Set the planet name
        planetName.Text = planetStats.Name;

        // Initialize labels with current stats
        UpdatePlanetDetails(
            patchRegionsCount: planetStats.PatchRegionsCount,
            mass: planetStats.Mass,
            radius: planetStats.Radius,
            surfaceArea: planetStats.SurfaceArea,
            gravity: planetStats.Gravity,
            averageTemp: planetStats.AverageTemp,
            humidity: planetStats.Humidity
        );

        // Subscribe to the PlanetStatsChanged signal using event subscription
        planetStats.PlanetStatsChanged += OnPlanetStatsChanged;
    }

    /// <summary>
    /// Called when the PlanetStatsChanged signal is emitted.
    /// Updates the planet details.
    /// </summary>
    private void OnPlanetStatsChanged()
    {
        GD.Print("PlanetStatsChanged signal received. Updating UI.");
        UpdatePlanetDetails(
            patchRegionsCount: planetStats.PatchRegionsCount,
            mass: planetStats.Mass,
            radius: planetStats.Radius,
            surfaceArea: planetStats.SurfaceArea,
            gravity: planetStats.Gravity,
            averageTemp: planetStats.AverageTemp,
            humidity: planetStats.Humidity
        );
    }

    /// <summary>
    /// Updates the planet statistics displayed in the panel.
    /// </summary>
    /// <param name="patchRegionsCount">Number of patch regions.</param>
    /// <param name="mass">Planet mass in Earth masses.</param>
    /// <param name="radius">Planet radius in Earth radii.</param>
    /// <param name="surfaceArea">Planet surface area in Earth surface areas.</param>
    /// <param name="gravity">Planet gravity in Earth gravities.</param>
    /// <param name="averageTemp">Planet average temperature in °C.</param>
    public void UpdatePlanetDetails(int patchRegionsCount, double mass, double radius, double surfaceArea, double gravity, double averageTemp,double humidity)
    {
        // Update the planet name (if it ever changes)
        planetName.Text = planetStats.Name;

        // Update the number of patch regions
        patchRegions.Text = $"Patch Regions: {patchRegionsCount:F2}";

        // Update planet mass
        planetMass.Text = $"Mass: {mass:F2} Earths";

        // Update planet radius
        planetRadius.Text = $"Radius: {radius:F2} Earths";

        // Update planet surface area
        planetSurfaceArea.Text = $"Surface Area: {surfaceArea:F2} Earths";

        // Update planet gravity
        planetGravity.Text = $"Gravity: {gravity:F2} Earths";

        // Update planet average temperature
        planetAvgTemp.Text = $"Avg. Temp.: {averageTemp:F2} °C";
        // Update planet humidity
        planetHumidity.Text = $"Humidity: {humidity:F2}%";
    }

    /// <summary>
    /// Called when the node exits the scene tree.
    /// Unsubscribes from the PlanetStatsChanged signal to prevent accessing disposed objects.
    /// </summary>
    public override void _ExitTree()
    {
        base._ExitTree();

        if (planetStats != null)
        {
            planetStats.PlanetStatsChanged -= OnPlanetStatsChanged;
        }
    }
}