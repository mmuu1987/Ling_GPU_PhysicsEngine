using UnityEngine;
using UnityEngine.Rendering;
using static MassEngine.MassGpuShaderPropertyIds;

namespace MassEngine
{
    /// <summary>
    /// Runtime-resolved render + animation data for one unit type. Built once at
    /// initialization from RenderConfig and its VAT profile; configuration assets are
    /// strictly read-only inputs and are NEVER written back. The per-LOD
    /// MaterialPropertyBlocks are prefilled with all VAT constants so the render path
    /// does zero reflection and zero per-frame VAT re-binding.
    /// </summary>
    public sealed class ResolvedUnitTypeRuntime
    {
        public Mesh nearMesh;
        public Mesh midMesh;
        public Mesh farMesh;
        public Material nearMaterial;
        public Material midMaterial;
        public Material farMaterial;
        public ShadowCastingMode nearShadowCasting = ShadowCastingMode.On;
        public bool nearReceiveShadows = true;
        public ShadowCastingMode midShadowCasting = ShadowCastingMode.Off;
        public bool midReceiveShadows;
        public ShadowCastingMode farShadowCasting = ShadowCastingMode.Off;
        public bool farReceiveShadows;

        public float idleClipDuration = 1f;
        public float moveClipDuration = 1f;
        public float attackClipDuration = 1f;
        public float deathClipDuration = 1.5f;

        public MaterialPropertyBlock nearBlock;
        public MaterialPropertyBlock midBlock;
        public MaterialPropertyBlock farBlock;

        /// <summary>
        /// LOD crossfade (2026-10-13): true when every LOD material's shader dithers by _MassLodState
        /// (SubShader tag MassLodCrossfade = 1). Only then does the classification draw an agent in
        /// both tiers while it changes tier; other shaders keep the instant switch (with hysteresis).
        /// </summary>
        public bool SupportsLodCrossfade
        {
            get { return LodCrossfadeTagged(nearMaterial) && LodCrossfadeTagged(midMaterial) && LodCrossfadeTagged(farMaterial); }
        }

        private static bool LodCrossfadeTagged(Material material)
        {
            return material == null || material.GetTag("MassLodCrossfade", false) == "1";
        }

        public Mesh GetMesh(int lodLevel)
        {
            if (lodLevel <= 0) return nearMesh;
            if (lodLevel == 1) return midMesh;
            return farMesh;
        }

        public Material GetMaterial(int lodLevel)
        {
            if (lodLevel <= 0) return nearMaterial;
            if (lodLevel == 1) return midMaterial;
            return farMaterial;
        }

        public MaterialPropertyBlock GetBlock(int lodLevel)
        {
            if (lodLevel <= 0) return nearBlock;
            if (lodLevel == 1) return midBlock;
            return farBlock;
        }

        public ShadowCastingMode GetShadowCasting(int lodLevel)
        {
            if (lodLevel <= 0) return nearShadowCasting;
            if (lodLevel == 1) return midShadowCasting;
            return farShadowCasting;
        }

        public bool GetReceiveShadows(int lodLevel)
        {
            if (lodLevel <= 0) return nearReceiveShadows;
            if (lodLevel == 1) return midReceiveShadows;
            return farReceiveShadows;
        }

        /// <summary>
        /// Resolves render data for a unit type. VAT LOD meshes are forced to pair with
        /// their VAT textures (a mesh baked against a different vertex order samples
        /// garbage); if the author configured a conflicting mesh, the profile pairing
        /// wins and a warning names the conflict instead of silently rewriting assets.
        /// </summary>
        public static ResolvedUnitTypeRuntime Resolve(UnitTypeConfig config, float fallbackDeathClipDuration)
        {
            ResolvedUnitTypeRuntime resolved = new ResolvedUnitTypeRuntime();
            RenderConfig render = config != null ? config.renderConfig : null;
            resolved.deathClipDuration = Mathf.Max(0.01f, fallbackDeathClipDuration);

            if (render == null)
                return resolved;

            resolved.nearMesh = render.nearMesh;
            resolved.midMesh = render.midMesh;
            resolved.farMesh = render.farMesh;
            resolved.nearMaterial = render.nearMaterial;
            resolved.midMaterial = render.midMaterial;
            resolved.farMaterial = render.farMaterial;
            resolved.nearShadowCasting = render.nearShadowCasting;
            resolved.nearReceiveShadows = render.nearReceiveShadows;
            resolved.midShadowCasting = render.midShadowCasting;
            resolved.midReceiveShadows = render.midReceiveShadows;
            resolved.farShadowCasting = render.farShadowCasting;
            resolved.farReceiveShadows = render.farReceiveShadows;

            if (render.vatProfile == null)
                return resolved;

            if (!VatProfileReader.TryRead(render.vatProfile, out VatProfileData profile, out string error))
            {
                Debug.LogError("MassEngine VAT profile '" + render.vatProfile.name + "' is invalid: " + error, render.vatProfile);
                return resolved;
            }

            resolved.idleClipDuration = profile.idle.Duration;
            resolved.moveClipDuration = profile.move.Duration;
            resolved.attackClipDuration = profile.attack.Duration;
            resolved.deathClipDuration = profile.death.Duration;

            resolved.nearMesh = ResolveVatMesh("nearMesh", render.nearMesh, profile.cleanMesh, config);

            Mesh pairedMidMesh = profile.hasMidLod ? profile.midLodMesh : (profile.hasLowLod ? profile.lowLodMesh : profile.cleanMesh);
            Mesh pairedFarMesh = profile.hasLowLod ? profile.lowLodMesh : (profile.hasMidLod ? profile.midLodMesh : profile.cleanMesh);
            resolved.midMesh = ResolveVatMesh("midMesh", render.midMesh, pairedMidMesh, config);
            resolved.farMesh = ResolveVatMesh("farMesh", render.farMesh, pairedFarMesh, config);

            resolved.nearBlock = BuildBlock(profile, 0);
            resolved.midBlock = BuildBlock(profile, 1);
            resolved.farBlock = BuildBlock(profile, 2);

            // Optional vertex AO: the AO copy of each LOD mesh (same vertices, AO in uv3.x) plus its strength for the shader.
            // No library entry or switch off -> same mesh, strength 0 -> identical rendering.
            resolved.nearMesh = ApplyVertexAo(resolved.nearMesh, resolved.nearBlock);
            resolved.midMesh = ApplyVertexAo(resolved.midMesh, resolved.midBlock);
            resolved.farMesh = ApplyVertexAo(resolved.farMesh, resolved.farBlock);

            // Flight (2026-10-15): flyers are drawn at their altitude and fall to the ground during the death clip.
            float altitude = config != null && config.combatConfig != null ? Mathf.Max(0f, config.combatConfig.flightAltitude) : 0f;
            var flight = new Vector4(altitude, resolved.deathClipDuration, 0f, 0f);
            resolved.nearBlock?.SetVector(FlightId, flight);
            resolved.midBlock?.SetVector(FlightId, flight);
            resolved.farBlock?.SetVector(FlightId, flight);

            return resolved;
        }

        private static readonly int VertexAOId = Shader.PropertyToID("_VertexAO");
        /// <summary>Flight render lift (MPB): x = altitude metres (0 = ground), y = death clip seconds (fall time).</summary>
        public static readonly int FlightId = Shader.PropertyToID("_MassFlight");
        private static readonly int VertexAODirectId = Shader.PropertyToID("_VertexAODirect");

        private static Mesh ApplyVertexAo(Mesh mesh, MaterialPropertyBlock block)
        {
            Mesh result = VertexAoLibrary.Substitute(mesh, out float strength, out float direct);
            if (block != null) { block.SetFloat(VertexAOId, strength); block.SetFloat(VertexAODirectId, direct); }
            return result;
        }

        private static Mesh ResolveVatMesh(string slotName, Mesh authored, Mesh pairedWithVat, UnitTypeConfig context)
        {
            if (pairedWithVat == null)
                return authored;

            if (authored != null && authored != pairedWithVat)
            {
                Debug.LogWarning(
                    "MassEngine RenderConfig." + slotName + " (" + authored.name + ") does not match the VAT profile mesh (" +
                    pairedWithVat.name + ") its textures were baked against; using the profile mesh. " +
                    "Assign the profile mesh in the RenderConfig (or clear the slot) to silence this warning.",
                    context);
            }

            return pairedWithVat;
        }

        private static MaterialPropertyBlock BuildBlock(VatProfileData profile, int lodLevel)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            Texture positionTexture;
            Texture normalTexture;
            int textureWidth;
            int textureHeight;
            int rowsPerFrame;

            if (lodLevel == 1 && profile.hasMidLod)
            {
                positionTexture = profile.midLodPositionTexture;
                normalTexture = profile.midLodNormalTexture;
                textureWidth = profile.midLodTextureWidth;
                textureHeight = profile.midLodTextureHeight;
                rowsPerFrame = profile.midLodRowsPerFrame;
            }
            else if (lodLevel > 0 && profile.hasLowLod)
            {
                positionTexture = profile.lowLodPositionTexture;
                normalTexture = profile.lowLodNormalTexture;
                textureWidth = profile.lowLodTextureWidth;
                textureHeight = profile.lowLodTextureHeight;
                rowsPerFrame = profile.lowLodRowsPerFrame;
            }
            else if (lodLevel > 1 && profile.hasMidLod)
            {
                positionTexture = profile.midLodPositionTexture;
                normalTexture = profile.midLodNormalTexture;
                textureWidth = profile.midLodTextureWidth;
                textureHeight = profile.midLodTextureHeight;
                rowsPerFrame = profile.midLodRowsPerFrame;
            }
            else
            {
                positionTexture = profile.positionTexture;
                normalTexture = profile.normalTexture;
                textureWidth = profile.textureWidth;
                textureHeight = profile.textureHeight;
                rowsPerFrame = profile.rowsPerFrame;
            }

            if (positionTexture != null)
                block.SetTexture(VATPosTexId, positionTexture);
            if (normalTexture != null)
                block.SetTexture(VATNormTexId, normalTexture);
            block.SetFloat(VATTexWidthId, Mathf.Max(1, textureWidth));
            block.SetFloat(VATTexHeightId, Mathf.Max(1, textureHeight));
            block.SetFloat(VATRowsPerFrameId, Mathf.Max(1, rowsPerFrame));
            block.SetFloat(VATFrameCountId, Mathf.Max(1, profile.totalFrameCount));
            block.SetFloat(VATFrameRateId, Mathf.Max(1, profile.frameRate));
            SetClip(block, IdleClipStartFrameId, IdleClipFrameCountId, IdleClipFrameRateId, profile.idle);
            SetClip(block, MoveClipStartFrameId, MoveClipFrameCountId, MoveClipFrameRateId, profile.move);
            SetClip(block, AttackClipStartFrameId, AttackClipFrameCountId, AttackClipFrameRateId, profile.attack);
            SetClip(block, DeathClipStartFrameId, DeathClipFrameCountId, DeathClipFrameRateId, profile.death);

            return block;
        }

        private static void SetClip(MaterialPropertyBlock block, int startFrameId, int frameCountId, int frameRateId, VatClipData clip)
        {
            block.SetFloat(startFrameId, Mathf.Max(0, clip.startFrame));
            block.SetFloat(frameCountId, Mathf.Max(1, clip.frameCount));
            block.SetFloat(frameRateId, Mathf.Max(1, clip.frameRate));
        }
    }
}
