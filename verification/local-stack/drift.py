"""Deliberately change presentation to distinguish checksum skip from reconciliation."""
import json, urllib.request
from check import ROOT, CTX, admin_token, snapshot
req = urllib.request.Request(ROOT+'/admin/realms/city-picks', data=json.dumps({'realm':'city-picks','displayName':'Manual drift'}).encode(), headers={'Authorization':'Bearer '+admin_token(),'Content-Type':'application/json'}, method='PUT')
with urllib.request.urlopen(req, context=CTX) as response: assert response.status == 204
assert snapshot()['realm']['displayName'] == 'Manual drift'
print('Manual display-name drift applied; reapply the unchanged presentation file next.')
