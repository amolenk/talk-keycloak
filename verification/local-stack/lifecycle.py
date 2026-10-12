"""Aspire lifecycle, explicit apply preservation and initializer failure evidence."""
import json, os, subprocess, time
from check import request, snapshot, login

HOST = os.path.abspath('AppHost/AppHost.csproj')
VOLUME = os.environ.get('PROBE_VOLUME', 'wayfinder-stack-probe-fresh-20261012')
def cli(*args, env=None, ok=True):
    r = subprocess.run(['aspire',*args,'--apphost',HOST], text=True, capture_output=True, env=env)
    if ok: assert r.returncode == 0, r.stdout+r.stderr
    return r
def resources():
    r = cli('describe','--format','Json')
    return json.loads(r.stdout)['resources']
def containers():
    return {r['displayName']:r['properties']['container.id'] for r in resources() if r['resourceType']=='Container'}
def docker(*args):
    return subprocess.run(['docker',*args],text=True,capture_output=True)
def apply(ids, password='demo'):
    kc = json.loads(docker('inspect',ids['keycloak']).stdout)[0]
    network = next(iter(kc['NetworkSettings']['Networks']))
    r = docker('run','--rm','--network',network,
        '-e','KEYCLOAK_URL=http://keycloak.dev.internal:8080',
        '-e','KEYCLOAK_USER=admin','-e','KEYCLOAK_PASSWORD='+password,
        '-e','IMPORT_FILES_LOCATIONS=/config/presentation.json',
        '-e','IMPORT_MANAGED_CLIENTS=no-delete','-e','IMPORT_MANAGED_ROLES=no-delete','-e','IMPORT_MANAGED_USERS=no-delete',
        '-e','LOGGING_LEVEL_DE_ADORSYS_KEYCLOAK_CONFIG=DEBUG',
        '-v',os.path.abspath('assets')+':/config:ro','adorsys/keycloak-config-cli:6.5.1-26.5.5')
    return r

ids = containers()
before = snapshot()
assert request('https://localhost:7100/api/state/edit',data=b'')[0] == 200
r = apply(ids)
assert r.returncode == 0, r.stdout+r.stderr
r = apply(ids)
assert r.returncode == 0 and 'import checksum same' in r.stdout+r.stderr, r.stdout+r.stderr
after = snapshot()
for key in ['users','clients','roles','credentials']:
    assert before[key] == after[key], key
assert after['realm']['displayName'] == 'City Picks — Own Your Identity'
print('PASS private Aspire-network HTTP apply/reapply; checksum skip; IDs and credentials preserved')
r = apply(ids,password='deliberately-wrong')
assert r.returncode != 0
assert snapshot() == after
print('PASS wrong-admin-password apply exits nonzero ('+str(r.returncode)+'); snapshot unchanged')
assert apply(ids).returncode == 0
print('PASS explicit apply recovers with correct credential; no forced realm recreation')

cli('stop','--non-interactive')
deadline = time.monotonic()+15
while any(docker('inspect',identifier).returncode == 0 for identifier in ids.values()) and time.monotonic()<deadline:
    time.sleep(.25)
for name, identifier in ids.items():
    assert docker('inspect',identifier).returncode != 0, name+' container still exists'
assert docker('volume','inspect',VOLUME).returncode == 0
print('PASS AppHost stop removes exact session containers, named volume survives')

env = dict(os.environ, PROBE_VOLUME=VOLUME,PROBE_INIT_FAIL='1')
cli('start','--non-interactive',env=env)
cli('wait','keycloak','--timeout','90')
r = cli('wait','probe','--timeout','10',ok=False)
assert r.returncode != 0
res = resources()
initializer = next(r for r in res if r['displayName']=='initializer')
probe = next(r for r in res if r['displayName']=='probe')
assert initializer.get('exitCode') == 23, initializer
assert probe['state'] != 'Running', probe['state']
try:
    request('https://localhost:7100/health')
    raise AssertionError('API listener exists after failed initializer')
except OSError:
    pass
print('PASS initializer exits 23; probe state '+probe['state']+'; health wait fails and no API listener')
cli('stop','--non-interactive')
env['PROBE_INIT_FAIL']='0'
env['PROBE_INIT_DELAY_MS']='15000'
cli('start','--non-interactive',env=env)
cli('wait','probe','--timeout','120')
res = resources()
initializer = next(r for r in res if r['displayName']=='initializer')
probe = next(r for r in res if r['displayName']=='probe')
assert initializer['exitCode'] == 0
assert probe['startTimestamp'] >= initializer['stopTimestamp']
new_ids = containers()
assert all(new_ids[n] != ids[n] for n in ids)
assert snapshot() == after
assert 'retained-curator-edit' in request('https://localhost:7100/api/state')[1]
for user in ['traveler','curator','observer']: login(user)
logs = docker('logs',new_ids['keycloak'])
assert "Realm 'city-picks' already exists. Import skipped" in logs.stdout+logs.stderr
pg = new_ids['postgres']
r = docker('exec','-e','PGPASSWORD=demo',pg,'psql','-U','citypicks','-d','citypicks','-tAc','SELECT COUNT(*) FROM "SeedMarkers";')
assert r.returncode == 0 and r.stdout.strip() == '1', r.stdout+r.stderr
print('PASS 15s delayed initializer finishes before API start; HTTP health verifies marker and issuer')
print('PASS new container IDs; startup import skips; exact realm/identity/credential snapshots and application edit retained; one marker')
port = next(r for r in res if r['displayName']=='postgres')['urls'][0]['url'].rsplit(':',1)[1]
env['PROBE_DB']=f'Host=localhost;Port={port};Database=citypicks;Username=citypicks;Password=demo'
r = subprocess.run(['dotnet-ef','database','update','--project','Probe/Probe.csproj','--no-build'],env=env,text=True,capture_output=True)
assert r.returncode == 0, r.stdout+r.stderr
assert 'retained-curator-edit' in request('https://localhost:7100/api/state')[1]
print('PASS synchronous EF tooling seed preserves edited state after async initializer')
