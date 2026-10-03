using System;
using UnityEngine;
using UnityEditor;

namespace MassEngine.Game.Editor
{
    public enum CharacterLowLodPolicy { FullOnly, StandardClustering, PaletteGrid }

    [CreateAssetMenu(menuName = "MassEngine/Character pipeline/Recipe", fileName = "CharacterRecipe")]
    public sealed class CharacterRecipe : ScriptableObject
    {
        [Header("Prepared Generic model — animation paths must already match")]
        public GameObject model;
        [Tooltip("Optional direct child containing ALL renderers. Only this unanimated node may have positive uniform scale.")]
        public string sizeRootPath = "";
        [Tooltip("Exact transform paths of every enabled rigid MeshRenderer attachment. Missing or unlisted renderers are rejected.")]
        public string[] attachmentPaths = Array.Empty<string>();
        [Tooltip("Explicit paths of unskinned, single-shape, single-100%-frame morph attachments. Other blendshapes remain rejected.")]
        public string[] blendshapeAttachmentPaths = Array.Empty<string>();
        public AnimationClip idle, move, attack, death;
        [Range(1, 60)] public int frameRate = 30;
        [Header("Geometry, NOT a combat balance preset")]
        [Min(.05f)] public float targetBodyHeight = 1.8f;
        public bool groundBindFeet = true;
        [Tooltip("Independent gameplay radius copied into the NEW unit's private FlockingConfig. Not inferred from weapons/cape.")]
        [Min(.01f)] public float agentRadius = .45f;
        [Header("Budget and quality gates")]
        public CharacterLowLodPolicy lowPolicy = CharacterLowLodPolicy.PaletteGrid;
        [Range(8, 20000)] public int lowVertexBudget = 1400;
        [Range(1, 32)] public int paletteColumns = 4, paletteRows = 4;
        [Min(8)] public int fullVertexBudget = 20000;
        [Range(1, 128)] public int maxTextureMiB = 32;
        [Min(.00001f)] public float maximumPositionError = .0025f;
        [Header("Private output — existing output is never overwritten")]
        public string outputName = "NewCharacter01";
        public UnitTypeConfig unitTemplate;
        [Header("Optional isolated preview; existing scenes are templates, never modified")]
        public bool createTrial;
        public SceneAsset trialMenu, trialBattlefield;
        public WarSandboxBattlefieldCatalog trialCatalog;
        public string battlefieldId = "character-pipeline-trial";
        public string templateId = "character-pipeline-unit";
        public AnimationClip[] Clips => new[] { idle, move, attack, death };
    }
}
