﻿using System;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   A concrete, equipable piece of equipment
/// </summary>
public class Equipment : RigidBody, IInteractableEntity
{
    public Equipment(EquipmentDefinition definition)
    {
        Definition = definition;

        AddChild(definition.WorldRepresentation.Instance());

        // TODO: physics customization
        var owner = CreateShapeOwner(this);
        ShapeOwnerAddShape(owner, new BoxShape
        {
            Extents = new Vector3(1.0f, 1.0f, 1.0f),
        });
    }

    [JsonProperty]
    public EquipmentDefinition Definition { get; private set; }

    [JsonIgnore]
    public AliveMarker AliveMarker { get; } = new();

    [JsonIgnore]
    public Spatial EntityNode => this;

    [JsonIgnore]
    public string ReadableName => Definition.Name;

    [JsonIgnore]
    public Texture Icon => Definition.Icon;

    [JsonIgnore]
    public WeakReference<InventorySlot>? ShownAsGhostIn { get; set; }

    [JsonIgnore]
    public float InteractDistanceOffset => 0;

    [JsonIgnore]
    public Vector3? ExtraInteractOverlayOffset => null;

    [JsonIgnore]
    public bool InteractionDisabled { get; set; }

    [JsonIgnore]
    public bool CanBeCarried => true;

    public void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }
}