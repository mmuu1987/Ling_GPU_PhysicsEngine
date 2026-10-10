from pathlib import Path
import sys,json,subprocess,datetime
sys.stdout.reconfigure(encoding='utf-8');R=Path(__file__).resolve().parents[2];P=R/'Logs/InteractionRefinement-20261007/P0'
code="[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); $ErrorActionPreference='Stop'; Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Unity.exe' -or $_.Name -eq 'WarSandbox.exe' } | Select-Object ProcessId,Name,ExecutablePath | ConvertTo-Json -Compress"
r=subprocess.run(['powershell','-NoProfile','-Command',code],capture_output=True,encoding='utf-8',check=True,timeout=45)
p=json.loads(r.stdout) if r.stdout.strip() else [];p=[p] if isinstance(p,dict) else p
result={'checkedAt':datetime.datetime.now().isoformat(),'activeProcesses':p,'unityLockExists':(R/'Temp/UnityLockfile').exists(),'readyForOwnedEditor':not p and not(R/'Temp/UnityLockfile').exists()}
path=P/('environment-'+datetime.datetime.now().strftime('%H%M%S')+'.json')
path.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
