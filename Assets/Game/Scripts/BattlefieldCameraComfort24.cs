using UnityEngine;
namespace MassEngine.Game {
 [DefaultExecutionOrder(1200),DisallowMultipleComponent]
 public sealed class BattlefieldCameraComfort24:MonoBehaviour {
  public MyCameraManager owner;
  public CameraGround24 ground;
  public TerrainScaleBounds envelope;
  [Min(3)]public float clearance=6;
  public float panSensitivity=.8f,lookDegreesPerPixel=.14f,wheelFraction=.06f;
  public float horizontalLimit=480,maximumHeight=600;
  public enum DragMode {None,Pan,Look,Orbit,Dolly}
  public struct Frame {
   public Vector2 mouse;
   public Vector3 keys;
   public float wheel,dt,screenHeight;
   public bool allowed,focused,alt,shift,slow,middleDown,middle,rightDown,right,leftDown,left,home;
  }
  static readonly Vector2[] Footprint={Vector2.right,Vector2.left,Vector2.up,Vector2.down};
  Camera cam;DragMode mode;Vector2 previousMouse;Vector3 anchor,initialPosition,lastSafe;
  Quaternion initialRotation;float zoomRemaining;bool initialized,returning;
  public bool SyntheticInput {get;set;}
  public bool SuppressFollow {get;private set;}
  public void NotifyFocus(){SuppressFollow=false;CancelMotion();}
  public DragMode CurrentMode=>mode;
  public float PendingZoom=>zoomRemaining;
  public Vector3 HomePosition=>initialPosition;
  public float LastMoveBudget {get;private set;}
  public int RejectedPointerSpikes {get;private set;}
  public int GroundStops {get;private set;}
  bool Ready(){if(initialized)return cam!=null;if(owner==null)owner=GetComponent<MyCameraManager>();cam=owner!=null?owner.ControlledCamera:null;if(cam==null||ground==null)return false;initialPosition=cam.transform.position;initialRotation=cam.transform.rotation;lastSafe=initialPosition;initialized=true;Enforce();return true;}
  public void Tick(bool locked){if(!Ready())return;if(SyntheticInput)return;var p=Input.mousePosition;var area=owner.NormalizedInputArea;bool inside=!owner.RequireMouseInsideScreen||new Rect(area.x*Screen.width,area.y*Screen.height,area.width*Screen.width,area.height*Screen.height).Contains(p);
   var f=new Frame{mouse=p,dt=Time.unscaledDeltaTime,screenHeight=Screen.height,focused=Application.isFocused,allowed=!locked&&inside&&!WarSandboxUGUI.PointerOverUI()&&!WarSandboxUGUI.IsTyping,alt=Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt)||Input.GetKey(KeyCode.AltGr),shift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),slow=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl),middleDown=Input.GetMouseButtonDown(2),middle=Input.GetMouseButton(2),rightDown=Input.GetMouseButtonDown(1),right=Input.GetMouseButton(1),leftDown=Input.GetMouseButtonDown(0),left=Input.GetMouseButton(0),wheel=Input.mouseScrollDelta.y,home=Input.GetKeyDown(KeyCode.Home)};
   f.keys=new Vector3((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),(Input.GetKey(KeyCode.E)?1:0)-(Input.GetKey(KeyCode.Q)?1:0),(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));Step(f);
  }
  public void CancelMotion(){mode=DragMode.None;zoomRemaining=0;returning=false;}
  void OnApplicationFocus(bool focused){if(!focused)CancelMotion();}
  void OnApplicationPause(bool paused){if(paused)CancelMotion();}
  void OnDisable(){CancelMotion();}
  void LateUpdate(){if(Ready())Enforce();}
  public float Floor(Vector3 p){float footprint=Mathf.Max(.5f,cam!=null?cam.nearClipPlane*1.5f:.5f);float y=ground.Height(p.x,p.z);foreach(var d in Footprint)y=Mathf.Max(y,ground.Height(p.x+d.x*footprint,p.z+d.y*footprint));return y;}
  public float RequiredClearance=>Mathf.Max(clearance,cam!=null?cam.nearClipPlane*2+1:clearance);
  Vector3 Bounded(Vector3 p){if(envelope!=null)p=envelope.center+Vector3.ClampMagnitude(p-envelope.center,envelope.maximumDistance);var xz=Vector2.ClampMagnitude(new Vector2(p.x,p.z),horizontalLimit);p.x=xz.x;p.z=xz.y;p.y=Mathf.Clamp(p.y,Mathf.Max(8,Floor(p)+RequiredClearance),maximumHeight);return p;}
  public void Enforce(){if(!initialized)return;var p=cam.transform.position;if(!CameraMotionSafety.IsFinite(p)){p=lastSafe;CancelMotion();}p=Bounded(p);owner.ConstrainWorldPosition(p);lastSafe=p;}
  void Move(Vector3 requested,float budget){if(!CameraMotionSafety.IsFinite(requested))return;var start=cam.transform.position;var step=Vector3.ClampMagnitude(requested,Mathf.Max(0,budget));int steps=Mathf.Max(1,Mathf.CeilToInt(step.magnitude/.5f));var safe=start;
   for(int i=1;i<=steps;i++){var candidate=Bounded(start+step*(i/(float)steps));if(Vector3.Distance(candidate,start)>budget+.0005f){GroundStops++;break;}safe=candidate;}
   owner.ConstrainWorldPosition(safe);lastSafe=safe;
  }
  Vector3 FlatForward(){var v=Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up);if(v.sqrMagnitude<.001f)v=Vector3.ProjectOnPlane(cam.transform.up,Vector3.up);return v.normalized;}
  bool FindAnchor(Vector2 mouse){var ray=cam.ScreenPointToRay(mouse);float prior=0;for(float t=2;t<=1200;t+=4){var p=ray.GetPoint(t);if(p.y<=ground.Height(p.x,p.z)){float lo=prior,hi=t;for(int k=0;k<12;k++){float mid=(lo+hi)*.5f;var q=ray.GetPoint(mid);if(q.y>ground.Height(q.x,q.z))lo=mid;else hi=mid;}anchor=ray.GetPoint((lo+hi)*.5f);return true;}prior=t;}return false;}
  public void Step(Frame f){if(!Ready())return;LastMoveBudget=0;
   if(!f.allowed||!f.focused||!CameraMotionSafety.IsFinite(f.mouse.x)||!CameraMotionSafety.IsFinite(f.mouse.y)){CancelMotion();previousMouse=f.mouse;return;}
   float dt=Mathf.Clamp(CameraMotionSafety.IsFinite(f.dt)?f.dt:0,0,.05f);if(dt<=0)return;
   float altitude=Mathf.Max(RequiredClearance,cam.transform.position.y-Floor(cam.transform.position));float budget=Mathf.Clamp(altitude*.9f,12,160)*dt;LastMoveBudget=budget;
   bool press=f.middleDown||f.rightDown||(f.alt&&f.leftDown);
   if(f.home){CancelMotion();SuppressFollow=true;returning=true;}
   if(press){SuppressFollow=true;CancelMotion();previousMouse=f.mouse;if(f.middleDown)mode=DragMode.Pan;else if(f.rightDown)mode=f.alt?DragMode.Dolly:DragMode.Look;else if(FindAnchor(f.mouse))mode=DragMode.Orbit;return;}
   if((mode==DragMode.Pan&&!f.middle)||(mode==DragMode.Look&&(!f.right||f.alt))||(mode==DragMode.Dolly&&(!f.right||!f.alt))||(mode==DragMode.Orbit&&(!f.left||!f.alt)))mode=DragMode.None;
   var pixels=f.mouse-previousMouse;previousMouse=f.mouse;
   if(pixels.magnitude>Mathf.Max(160,f.screenHeight*.16f)){pixels=Vector2.zero;RejectedPointerSpikes++;}
   pixels=Vector2.ClampMagnitude(pixels,60)*(1080/Mathf.Max(480,f.screenHeight));
   float wheel=CameraMotionSafety.IsFinite(f.wheel)?Mathf.Clamp(f.wheel,-2,2):0;
   if(mode!=DragMode.None||Mathf.Abs(wheel)>.0001f)returning=false;
   if(returning){Move(initialPosition-cam.transform.position,budget);cam.transform.rotation=Quaternion.RotateTowards(cam.transform.rotation,initialRotation,120*dt);if(Vector3.Distance(cam.transform.position,initialPosition)<.05f&&Quaternion.Angle(cam.transform.rotation,initialRotation)<.2f)returning=false;return;}
   Vector3 move=Vector3.zero;float fine=f.slow?.35f:1;
   if(mode==DragMode.Pan){float units=2*altitude*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f)/1080;var right=Vector3.ProjectOnPlane(cam.transform.right,Vector3.up).normalized;move=(-right*pixels.x-FlatForward()*pixels.y)*(units*panSensitivity*fine);}
   if(mode==DragMode.Look){var a=cam.transform.eulerAngles;float turn=Mathf.Min(8,220*dt);float yaw=a.y+Mathf.Clamp(pixels.x*lookDegreesPerPixel*fine,-turn,turn),pitch=Mathf.Clamp(CameraMotionSafety.NormalizeSignedAngle(a.x)-Mathf.Clamp(pixels.y*lookDegreesPerPixel*fine,-turn,turn),12,85);cam.transform.rotation=Quaternion.Euler(pitch,yaw,0);var keys=CameraMotionSafety.IsFinite(f.keys)?Vector3.ClampMagnitude(f.keys,1):Vector3.zero;float speed=Mathf.Clamp(altitude*.25f,6,60)*(f.shift?1.8f:1)*fine;move+=(cam.transform.right*keys.x+Vector3.up*keys.y+FlatForward()*keys.z)*(speed*dt);}
   if(mode==DragMode.Orbit){var a=cam.transform.eulerAngles;float turn=220*dt;float pitch=Mathf.Clamp(CameraMotionSafety.NormalizeSignedAngle(a.x)-Mathf.Clamp(pixels.y*lookDegreesPerPixel*fine,-turn,turn),12,85);float yaw=a.y+Mathf.Clamp(pixels.x*lookDegreesPerPixel*fine,-turn,turn);float distance=Vector3.Distance(cam.transform.position,anchor);var target=anchor+Quaternion.Euler(pitch,yaw,0)*Vector3.back*distance;Move(target-cam.transform.position,budget);cam.transform.LookAt(anchor);return;}
   if(mode==DragMode.Dolly)wheel+=Mathf.Clamp((pixels.x+pixels.y)*.02f,-1,1);
   if(Mathf.Abs(wheel)>.0001f)SuppressFollow=true;
   if(Mathf.Abs(wheel)>.0001f)zoomRemaining=Mathf.Clamp(zoomRemaining+wheel*Mathf.Clamp(altitude*wheelFraction,1,18)*fine,-40,40);
   if(Mathf.Abs(zoomRemaining)>.001f){float dz=Mathf.Clamp(zoomRemaining*(1-Mathf.Exp(-18*dt)),-Mathf.Clamp(altitude*.5f,12,110)*dt,Mathf.Clamp(altitude*.5f,12,110)*dt);zoomRemaining-=dz;if(dz>0){var end=cam.transform.position+cam.transform.forward*dz;if(end.y<Floor(end)+RequiredClearance){float lo=0,hi=dz;for(int k=0;k<10;k++){float mid=(lo+hi)*.5f;var q=cam.transform.position+cam.transform.forward*mid;if(q.y>=Floor(q)+RequiredClearance)lo=mid;else hi=mid;}dz=lo;zoomRemaining=0;}}move+=cam.transform.forward*dz;}
   Move(move,budget);
  }
 }
}
