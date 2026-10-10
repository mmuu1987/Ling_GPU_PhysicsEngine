from common_p2 import *
sys=__import__('sys');sys.stdout.reconfigure(encoding='utf-8')
p=ROOT/'Docs/InteractionRefinement-20261007/IMPLEMENTATION.md';actual=p.read_text(encoding='utf-8');expected=json.loads((ROOT/'Tools/InteractionRefinement/p2-report-payload.json').read_text(encoding='utf-8'))['oldImplementation']
print(json.dumps({'actualLength':len(actual),'expectedLength':len(expected),'rawSha256':sha(p),'sameExceptTrailingNewlines':actual.rstrip('\n')==expected.rstrip('\n'),'actualStart':repr(actual[:25]),'expectedStart':repr(expected[:25]),'actualEnd':repr(actual[-70:]),'expectedEnd':repr(expected[-70:]),'firstDifference':next((i for i,(x,y) in enumerate(zip(actual,expected)) if x!=y),None)},ensure_ascii=False,indent=2))
