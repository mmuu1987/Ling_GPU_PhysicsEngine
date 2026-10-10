"""Windows low-frequency read-only counters for one owned Unity PID; no process enumeration or settings changes."""
import ctypes,csv,os,threading
from ctypes import wintypes
class OsSampler:
 def __init__(self,pid):
  self.pid=pid;self.rows=[];self.errors=[];self.stop_event=threading.Event();self.thread=threading.Thread(target=self.loop,daemon=True)
 def start(self):self.thread.start()
 def stop(self,folder):
  self.stop_event.set();self.thread.join(timeout=5)
  if self.thread.is_alive():self.errors.append('sampler thread did not finish within5s')
  if self.rows:
   with (folder/'os-counters.csv').open('w',newline='',encoding='utf-8') as f:
    w=csv.DictWriter(f,fieldnames=list(self.rows[0]));w.writeheader();w.writerows(self.rows)
  import json
  (folder/'os-counter-status.json').write_text(json.dumps({'pid':self.pid,'logicalCpuCount':os.cpu_count(),'errors':self.errors,'samples':len(self.rows),'intervalSeconds':1,'gpuClockCollected':False,'scope':'GetSystemTimes and GetProcessTimes for owned Unity PID only; no process names or user data. CPU utilization is not main-thread blocked time. Windows system CPU totals may cover only caller processor group on >64 processors.'},indent=2),encoding='utf-8')
 def loop(self):
  handle=None
  try:
   k=ctypes.WinDLL('kernel32',use_last_error=True)
   k.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];k.OpenProcess.restype=wintypes.HANDLE
   k.GetSystemTimes.argtypes=[ctypes.POINTER(wintypes.FILETIME)]*3;k.GetSystemTimes.restype=wintypes.BOOL
   k.GetProcessTimes.argtypes=[wintypes.HANDLE]+[ctypes.POINTER(wintypes.FILETIME)]*4;k.GetProcessTimes.restype=wintypes.BOOL
   k.QueryPerformanceCounter.argtypes=[ctypes.POINTER(ctypes.c_longlong)];k.QueryPerformanceFrequency.argtypes=[ctypes.POINTER(ctypes.c_longlong)]
   k.CloseHandle.argtypes=[wintypes.HANDLE]
   handle=k.OpenProcess(0x1000,False,self.pid)
   if not handle:raise ctypes.WinError(ctypes.get_last_error())
   freq=ctypes.c_longlong();assert k.QueryPerformanceFrequency(ctypes.byref(freq))
   value=lambda f:(int(f.dwHighDateTime)<<32)|int(f.dwLowDateTime)
   while not self.stop_event.is_set():
    q0=ctypes.c_longlong();q1=ctypes.c_longlong();k.QueryPerformanceCounter(ctypes.byref(q0))
    idle,kernel,user,created,exited,pk,pu=[wintypes.FILETIME() for _ in range(7)]
    if not k.GetSystemTimes(ctypes.byref(idle),ctypes.byref(kernel),ctypes.byref(user)):raise ctypes.WinError(ctypes.get_last_error())
    if not k.GetProcessTimes(handle,ctypes.byref(created),ctypes.byref(exited),ctypes.byref(pk),ctypes.byref(pu)):raise ctypes.WinError(ctypes.get_last_error())
    k.QueryPerformanceCounter(ctypes.byref(q1))
    self.rows.append(dict(qpc=q0.value,frequency=freq.value,readCostTicks=q1.value-q0.value,systemIdle100ns=value(idle),systemKernel100ns=value(kernel),systemUser100ns=value(user),unityKernel100ns=value(pk),unityUser100ns=value(pu)))
    self.stop_event.wait(1)
  except Exception as e:self.errors.append(str(e))
  finally:
   if handle:k.CloseHandle(handle)
