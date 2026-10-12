"""Cookie-backed real PKCE exchange and minimal API authorization checks."""
import base64, glob, hashlib, http.cookiejar, json, os, re, ssl, urllib.parse, urllib.request
from html import unescape

ROOT = 'https://localhost:8443'
REALM = ROOT + '/realms/city-picks'
CTX = ssl.create_default_context()
for certificate in glob.glob(os.path.expanduser('~/.aspnet/dev-certs/trust/*.pem')):
    CTX.load_verify_locations(cafile=certificate)
if os.environ.get('PROBE_CA_FILE'): CTX.load_verify_locations(cafile=os.environ['PROBE_CA_FILE'])

def request(url, data=None, token=None, opener=None, method=None):
    headers = {'Authorization': 'Bearer ' + token} if token else {}
    if isinstance(data, list) or method in ['PUT', 'POST'] and isinstance(data, dict):
        data = json.dumps(data).encode()
        headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(url, data=urllib.parse.urlencode(data).encode() if isinstance(data, dict) else data, headers=headers, method=method)
    try:
        with (opener or urllib.request.build_opener(urllib.request.HTTPSHandler(context=CTX))).open(req) as r:
            return r.status, r.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()

def admin_token():
    status, body = request(ROOT + '/realms/master/protocol/openid-connect/token', {'grant_type':'password','client_id':'admin-cli','username':'admin','password':'demo'})
    assert status == 200, (status, body)
    return json.loads(body)['access_token']

def snapshot():
    token = admin_token()
    result = {}
    for path in ['', '/users', '/clients', '/roles']:
        status, body = request(ROOT+'/admin/realms/city-picks'+path, token=token)
        assert status == 200
        d = json.loads(body)
        if not path: result['realm'] = {k:d.get(k) for k in ['id','realm','displayName','attributes']}
        else:
            key = {'/users':'username','/clients':'clientId','/roles':'name'}[path]
            result[path[1:]] = {i[key]:i['id'] for i in d}
    result['credentials'] = {}
    for name, user_id in result['users'].items():
        status, body = request(ROOT+'/admin/realms/city-picks/users/'+user_id+'/credentials', token=token)
        assert status == 200
        result['credentials'][name] = [{'id':c['id'],'type':c['type']} for c in json.loads(body)]
    return result

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if newurl.startswith('https://localhost:7100/authentication/'): return None
        return super().redirect_request(req, fp, code, msg, headers, newurl)

def login(username, verify=True):
    verifier = 'city-picks-probe-' + username + '-' + 'v'*48
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip('=')
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=CTX), urllib.request.HTTPCookieProcessor(jar), NoRedirect())
    query = urllib.parse.urlencode({'client_id':'city-picks-web','redirect_uri':'https://localhost:7100/authentication/login-callback','response_type':'code','scope':'openid','code_challenge':challenge,'code_challenge_method':'S256','state':'probe'})
    status, body = request(REALM+'/protocol/openid-connect/auth?'+query, opener=opener)
    assert status == 200, (status, body[:200])
    action = unescape(re.search(r'<form[^>]+action="([^"]+)"', body).group(1))
    try:
        response = opener.open(urllib.request.Request(action, data=urllib.parse.urlencode({'username':username,'password':'demo','credentialId':''}).encode()))
        raise AssertionError('login did not redirect: '+response.read().decode()[:200])
    except urllib.error.HTTPError as e:
        assert e.code == 302, (e.code,e.read().decode()[:200])
        code = urllib.parse.parse_qs(urllib.parse.urlparse(e.headers['Location']).query)['code'][0]
    status, body = request(REALM+'/protocol/openid-connect/token', {'grant_type':'authorization_code','client_id':'city-picks-web','redirect_uri':'https://localhost:7100/authentication/login-callback','code':code,'code_verifier':verifier})
    assert status == 200, (status, body)
    tokens = json.loads(body)
    token = tokens['access_token']
    claims = json.loads(base64.urlsafe_b64decode(token.split('.')[1]+'=='))
    assert claims['iss'] == REALM
    if verify: assert 'city-picks-api' in claims['aud']
    statuses = {p:request('https://localhost:7100/api/'+p, token=token)[0] for p in ['identity','traveler','curator']}
    expected = {'traveler':{'identity':200,'traveler':200,'curator':403},'curator':{'identity':200,'traveler':200,'curator':200},'observer':{'identity':200,'traveler':403,'curator':403}}.get(username)
    if verify and expected: assert statuses == expected, statuses
    tampered = token.split('.')
    tampered[2] = ('A' if tampered[2][0] != 'A' else 'B') + tampered[2][1:]
    assert request('https://localhost:7100/api/identity', token='.'.join(tampered))[0] == 401
    print(json.dumps({'user':username,'issuer':claims['iss'],'audience':claims.get('aud'),'roles':claims.get('roles',[]),'statuses':statuses}))
    return {'claims':claims, 'tokens':tokens, 'opener':opener, 'statuses':statuses}

if __name__ == '__main__':
    import sys
    if len(sys.argv)>1 and sys.argv[1]=='snapshot': print(json.dumps(snapshot(),indent=2))
    else:
        assert request('https://localhost:7100/api/identity')[0] == 401
        for user in ['traveler','curator','observer']: login(user)
        print('anonymous:401; state:'+request('https://localhost:7100/api/state')[1])
