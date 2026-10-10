using System;
using UnityEngine;
namespace MassEngine
{
    public sealed partial class MassEngineManager
    {
        private static int nextLocalEpoch;
        private LocalOrderChannel localOrders;
        public LocalOrderChannel LocalOrders=>localOrders;
        /// <summary>Developer-only P6 admission. No serialized toggle and no player/UI caller.</summary>
        public bool TryEnableLocalOrderPrototype(out string error)
        {
            error=null;
            if(localOrders!=null&&!localOrders.IsDisposed)return true;
            if(bufferManager==null||!bufferManager.IsAllocated||terrainRuntime==null||terrainNavigation==null||pipelineOrchestrator==null)
            {error="P6 prototype requires initialized authored terrain; legacy plane remains unchanged.";return false;}
            if(bufferManager.FlowCellCount>LocalOrderChannel.MaxCells){error="P6 flow-cell memory budget exceeded.";return false;}
            try
            {
                int n=bufferManager.AgentCount;var teams=new int[n];var radii=new float[n];
                for(int i=0;i<n;i++)
                {
                    int type=agentUnitTypeIndices[i];teams[i]=unitTypeRegistry.RegisteredTypes[type].Config.teamId;
                    var scale=agentSpawnScales[i];radii[i]=gpuSettingsCache[type].agentRadius*Mathf.Max(1f,Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z)));
                }
                int hash=LocalPhysicsSignature();var surface=terrainSurface;var obstacles=CurrentTerrainObstacles();float padding=activeStaticObstaclePadding;
                var origin=Flow.flowFieldOrigin;float cell=Flow.flowFieldCellSize;int resolution=Flow.flowFieldResolution;float boundary=Simulation.boundaryPadding;
                // Reuse only this exact, current terrain context. A different effective
                // radius must keep the original radius-specific construction path.
                var sharedNavigation = terrainContextChecked &&
                    terrainContextHash == unchecked(TerrainConfigurationHash() * 31 + lastStaticObstacleHash)
                    ? terrainNavigation : null;
                localOrders=new LocalOrderChannel(bufferManager,System.Threading.Interlocked.Increment(ref nextLocalEpoch),teams,radii,
                    r=>sharedNavigation!=null && ReferenceEquals(sharedNavigation.Surface,surface) &&
                        sharedNavigation.Clearance==r && sharedNavigation.Origin.Equals(origin) &&
                        sharedNavigation.CellSize==cell && sharedNavigation.ResolutionX==resolution &&
                        sharedNavigation.ResolutionZ==resolution
                        ? sharedNavigation.CreateIndependentSolver()
                        : new TerrainNavigationGrid(surface,origin,cell,resolution,resolution,r,boundary,obstacles,padding),
                    ()=>terrainSurface==surface&&LocalPhysicsSignature()==hash,()=>battleStarted);
                pipelineOrchestrator.LocalCommands=localOrders;return true;
            }
            catch(Exception ex){localOrders?.Dispose();localOrders=null;error=ex.Message;return false;}
        }
        private int LocalPhysicsSignature()
        {
            unchecked{int h=terrainContextHash*31+lastStaticObstacleHash;
                if(gpuSettingsCache!=null)foreach(var s in gpuSettingsCache)h=h*31+s.agentRadius.GetHashCode();return h;}
        }
        public void DisableLocalOrderPrototype()
        {localOrders?.Dispose();localOrders=null;if(pipelineOrchestrator!=null)pipelineOrchestrator.LocalCommands=null;}
    }
}


