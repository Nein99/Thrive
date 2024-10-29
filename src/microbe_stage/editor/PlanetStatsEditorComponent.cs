using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Editor patch map component
/// </summary>
/// <remarks>
///   <para>
///     TODO: this is a bit too microbe specific currently so this probably needs a bit more generalization in the
///     future with more logic being put in <see cref="MicrobeEditorPatchMap"/>
///   </para>
/// </remarks>
/// <typeparam name="TEditor">Type of editor this component is for</typeparam>
[GodotAbstract]
public partial class PlanetStatsEditorComponent<TEditor> : EditorComponentBase<TEditor>
    where TEditor : IEditorWithPatches
{
    [Export]
    [AssignOnlyChildItemsOnDeserialize]
    [JsonProperty]
    protected PlanetDetailsPanel planetDetailsPanel = null!;

    [Export]
    private Label seedLabel = null!;

    protected PlanetStatsEditorComponent()
    {
    }
    public override void Init(TEditor owningEditor, bool fresh)
    {
        base.Init(owningEditor, fresh);

        UpdateSeedLabel();
    }

    private void UpdateSeedLabel()
    {
        seedLabel.Text = Localization.Translate("SEED_LABEL")
            .FormatSafe(Editor.CurrentGame.GameWorld.WorldSettings.Seed);
    }

    public override void OnInsufficientMP(bool playSound = true)
    {
        // This component doesn't use actions
    }

    public override void OnActionBlockedWhileAnotherIsInProgress()
    {
    }

    public override void OnMutationPointsChanged(int mutationPoints)
    {
    }

    public override void UpdateUndoRedoButtons(bool canUndo, bool canRedo)
    {
    }

    public override void OnValidAction(IEnumerable<CombinableActionData> actions)
    {
    }
}
