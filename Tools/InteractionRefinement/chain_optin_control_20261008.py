import subprocess,sys
for s in ['Tools/InteractionRefinement/p3_burst_optin_finalshape_20261008.py','Tools/InteractionRefinement/p3_burst_cold_tail_control_20261008.py']:
    r=subprocess.run([sys.executable,'-X','utf8',s],cwd='/home/user')
    print('STEP',s,'exit',r.returncode,flush=True)
    if r.returncode!=0: sys.exit(r.returncode)
