from pathlib import Path
import json,csv
R=Path(__file__).resolve().parents[2];D=R/'outputs/P9ColdFix-20261009-01';rows={}
runs=[('P11','cold-attack-'+n,'original-'+n) for n in ['warm-02','cold-01','restored-01']]+[('P12','cold-fix-fixed-cold-'+str(i).zfill(2),'fixed-cold-'+str(i).zfill(2)) for i in range(1,9)]
for project,folder,label in runs:
 V=R/'outputs'/project/'Logs/InteractionRefinement-20261007/P8'/folder
 values={r[2]:float(r[1]) for r in csv.reader((V/'stages-P9ColdAttackExactJointPrefix.csv').read_text(encoding='utf-8-sig').splitlines()) if r}
 rows[label]={'entryIntervalSeconds':values['entered']-values['radius-save-reenter'],'entryToPlanAppliedSeconds':values['plan-applied-gpu']-values['entered'],'planAppliedToMenuReleasedSeconds':values['menu-resources-released']-values['plan-applied-gpu'],'menuReadyToCompleteSeconds':values['cold-diagnostic-complete']-values['menu-ready']}
s={'definition':'Entry interval is radius-save-reenter -> entered; includes scene entry, loading and initialization, not a pure shader profiler measurement. Single samples; no proof of zero cost migration.','intervals':rows}
(D/'host-r03-stage-intervals.json').write_text(json.dumps(s,indent=2),encoding='utf-8');print(json.dumps(s,indent=2))
