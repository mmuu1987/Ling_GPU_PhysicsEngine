using System;
using NUnit.Framework;
namespace MassEngine.Tests {
 public sealed class BurstDependencyRepairPolicyTests {
  [Test] public void WaitingRoutesManagedWithoutProbingAndThenBecomesReady(){
   object owner=new object();int probes=0,releases=0,managed=0,native=0;
   using(var gate=new P3PreparationGate(owner,()=>++probes==2,()=>releases++,0)){
    if(gate.CanUseBurst(owner,true,false,0))native++;else managed++;
    Assert.AreEqual(0,probes);Assert.AreEqual(1,managed);Assert.AreEqual(0,native);
    Assert.IsFalse(gate.Advance(owner,true,false,0));Assert.IsFalse(gate.Advance(owner,true,false,.05));Assert.AreEqual(1,probes);
    Assert.IsTrue(gate.Advance(owner,true,false,.1));
    if(gate.CanUseBurst(owner,true,false,.1))native++;else managed++;
    Assert.AreEqual(1,native);Assert.AreEqual(1,managed);Assert.IsTrue(gate.Advance(owner,true,false,.2));Assert.AreEqual(2,probes);
   }Assert.AreEqual(1,releases);
  }
  [TestCase(false,false)][TestCase(true,true)][TestCase(false,true)]
  public void ForbiddenPolicyNeverProbes(bool enabled,bool forceSync){object o=new object();int probes=0,releases=0;using(var g=new P3PreparationGate(o,()=>{probes++;return true;},()=>releases++,0)){Assert.IsFalse(g.Advance(o,enabled,forceSync,0));Assert.IsFalse(g.Advance(o,true,false,1));Assert.IsFalse(g.CanUseBurst(o,true,false,2));Assert.AreEqual(P3PreparationGate.Phase.Unavailable,g.State);}Assert.AreEqual(0,probes);Assert.AreEqual(1,releases);}
  [TestCase(false,false)][TestCase(true,true)]
  public void LostPermissionInvalidatesEvenReady(bool enabled,bool forced){object o=new object();int p=0,d=0;using(var g=new P3PreparationGate(o,()=>{p++;return true;},()=>d++,0)){Assert.IsTrue(g.Advance(o,true,false,0));Assert.IsFalse(g.CanUseBurst(o,enabled,forced,.1));Assert.IsFalse(g.Advance(o,true,false,.2));}Assert.AreEqual(1,p);Assert.AreEqual(1,d);}
  [Test] public void DeadlineRejectsBeforeAnotherProbeAndDoesNotRetry(){object o=new object();int p=0,d=0;using(var g=new P3PreparationGate(o,()=>{p++;return false;},()=>d++,0,.1)){Assert.IsFalse(g.Advance(o,true,false,0));Assert.IsFalse(g.Advance(o,true,false,.1));Assert.AreEqual("timeout",g.Reason);Assert.IsFalse(g.Advance(o,true,false,99));}Assert.AreEqual(1,p);Assert.AreEqual(1,d);}
  [Test] public void NewOwnerCannotReuseOldReady(){object a=new object(),b=new object();int da=0,db=0;using(var old=new P3PreparationGate(a,()=>true,()=>da++,0)){Assert.IsTrue(old.Advance(a,true,false,0));Assert.IsFalse(old.CanUseBurst(b,true,false,1));Assert.AreEqual("owner-replaced",old.Reason);using(var fresh=new P3PreparationGate(b,()=>true,()=>db++,1)){Assert.IsFalse(fresh.CanUseBurst(b,true,false,1));Assert.IsTrue(fresh.Advance(b,true,false,1));Assert.IsFalse(old.CanUseBurst(a,true,false,2));}}Assert.AreEqual(1,da);Assert.AreEqual(1,db);}
  [Test] public void DisposePendingIsIdempotentAndNeverProbesAgain(){object o=new object();int p=0,d=0;var g=new P3PreparationGate(o,()=>{p++;return false;},()=>d++,0);g.Advance(o,true,false,0);g.Dispose();g.Dispose();Assert.IsFalse(g.Advance(o,true,false,1));Assert.IsFalse(g.CanUseBurst(o,true,false,1));Assert.AreEqual(P3PreparationGate.Phase.Disposed,g.State);Assert.AreEqual(1,p);Assert.AreEqual(1,d);}
  [Test] public void ExplicitUnavailableReturnsManagedAndReleases(){object o=new object();int p=0,d=0;using(var g=new P3PreparationGate(o,()=>{p++;throw new P3PreparationUnavailableException("simulated unavailable backend");},()=>d++,0)){Assert.IsFalse(g.Advance(o,true,false,0));Assert.AreEqual("backend-unavailable",g.Reason);Assert.IsFalse(g.Advance(o,true,false,1));}Assert.AreEqual(1,p);Assert.AreEqual(1,d);}
  [Test] public void UnexpectedErrorIsNotSilentlyDowngraded(){object o=new object();int d=0;var error=new InvalidOperationException("data contract error");using(var g=new P3PreparationGate(o,()=>{throw error;},()=>d++,0)){Assert.AreSame(error,Assert.Throws<InvalidOperationException>(()=>g.Advance(o,true,false,0)));Assert.AreEqual(P3PreparationGate.Phase.Faulted,g.State);Assert.IsFalse(g.CanUseBurst(o,true,false,1));}Assert.AreEqual(1,d);}
  [Test] public void InvalidOrBackwardClockCannotLaunchProbe(){object o=new object();int p=0;using(var g=new P3PreparationGate(o,()=>{p++;return true;},()=>{},1)){Assert.Throws<ArgumentOutOfRangeException>(()=>g.Advance(o,true,false,double.NaN));Assert.Throws<ArgumentOutOfRangeException>(()=>g.Advance(o,true,false,.9));Assert.AreEqual(0,p);}}
  [Test] public void PreparationDeadlineDoesNotExpireAnAlreadyReadyInstance(){object o=new object();int p=0;using(var g=new P3PreparationGate(o,()=>{p++;return true;},()=>{},0,.1)){Assert.IsTrue(g.Advance(o,true,false,0));Assert.IsTrue(g.CanUseBurst(o,true,false,100));Assert.IsTrue(g.Advance(o,true,false,100));Assert.AreEqual(1,p);}}
 }
}
