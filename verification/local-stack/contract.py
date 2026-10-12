"""Real-server identity contract checks. Deliberate mutations are restored in finally."""
import copy, json, time, urllib.parse, urllib.request, urllib.error
from check import ROOT, REALM, request, admin_token, login, snapshot

admin = admin_token()
def api(path, data=None, method=None, expected=200):
    status, body = request(ROOT+'/admin/realms/city-picks'+path, data, admin, method=method)
    assert status == expected, (method,path,status,body[:300])
    return json.loads(body) if body else None

client = api('/clients?clientId=city-picks-web')[0]
client_path = '/clients/'+client['id']
roles = {r['name']:r for r in api('/roles')}
users = {u['username']:u for u in api('/users')}
default_role = api('/roles/default-roles-city-picks')
try:
    # Define exact application role scope, then assign an unrelated role to curator.
    if 'unrelated-probe' not in roles:
        api('/roles', {'name':'unrelated-probe'}, 'POST', 201)
    unrelated = api('/roles/unrelated-probe')
    api('/users/'+users['curator']['id']+'/role-mappings/realm', [unrelated], 'POST', 204)
    client['fullScopeAllowed'] = False
    client['attributes']['post.logout.redirect.uris'] = 'https://localhost:7100/authentication/logout-callback'
    api(client_path, client, 'PUT', 204)
    api(client_path+'/scope-mappings/realm', [roles['traveler'],roles['curator']], 'POST', 204)
    api('/roles-by-id/'+default_role['id']+'/composites', [roles['traveler']], 'POST', 204)
    assert api('/roles/curator/composites') == [], 'curator must not imply traveler'
    for username in ['traveler','curator','observer']:
        result = login(username)
        assert set(result['claims'].get('roles',[])) <= {'traveler','curator'}
    print('PASS strict role scope excludes unrelated-probe; observer retains API audience; curator not composite')

    # Create through the registration screen, rather than administrative provisioning.
    from check import CTX, NoRedirect
    import http.cookiejar, re
    from html import unescape
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=CTX), urllib.request.HTTPCookieProcessor(jar), NoRedirect())
    query = urllib.parse.urlencode({'client_id':'city-picks-web','redirect_uri':'https://localhost:7100/authentication/login-callback','response_type':'code','scope':'openid','code_challenge':'v'*43,'code_challenge_method':'S256','state':'registration-probe'})
    status, body = request(REALM+'/protocol/openid-connect/registrations?'+query, opener=opener)
    assert status == 200
    action = unescape(re.search(r'<form[^>]+action="([^"]+)"',body).group(1))
    username = 'registered-probe-'+str(int(time.time()))
    status, body = request(action, {'username':username,'email':username+'@example.test','firstName':'Registered','lastName':'Probe','password':'demo','password-confirm':'demo'}, opener=opener)
    assert status == 302, (status,body[:400])
    registered = api('/users?username='+username)[0]
    try:
        r = login(username, verify=False)
        assert r['statuses'] == {'identity':200,'traveler':200,'curator':403}, r['statuses']
        assert r['claims']['roles'] == ['traveler'], r['claims']['roles']
        assert registered['requiredActions'] == [], registered['requiredActions']
        print('PASS actual self-registration gets traveler only, no email/required-action barrier')
    finally:
        api('/users/'+registered['id'], method='DELETE', expected=204)

    # RP initiated logout validates exact callback and removes the cookie-backed SSO session.
    result = login('traveler')
    q = urllib.parse.urlencode({'id_token_hint':result['tokens']['id_token'],'post_logout_redirect_uri':'https://localhost:7100/authentication/logout-callback','state':'logout-probe'})
    try:
        result['opener'].open(REALM+'/protocol/openid-connect/logout?'+q)
        raise AssertionError('expected callback redirect')
    except urllib.error.HTTPError as e:
        assert e.code == 302, (e.code,e.read().decode()[:200])
        assert e.headers['Location'] == 'https://localhost:7100/authentication/logout-callback?state=logout-probe', e.headers['Location']
    q = urllib.parse.urlencode({'client_id':'city-picks-web','redirect_uri':'https://localhost:7100/authentication/login-callback','response_type':'code','scope':'openid','code_challenge':'v'*43,'code_challenge_method':'S256','prompt':'none','state':'post-logout'})
    try:
        result['opener'].open(REALM+'/protocol/openid-connect/auth?'+q)
        raise AssertionError('expected no-session redirect')
    except urllib.error.HTTPError as e:
        assert e.code == 302
        assert urllib.parse.parse_qs(urllib.parse.urlparse(e.headers['Location']).query)['error'] == ['login_required']
    print('PASS logout exact callback/state; subsequent prompt=none requires login')

    # Genuine Keycloak-signed wrong-audience token, not payload tampering.
    wrong = copy.deepcopy(client)
    next(m for m in wrong['protocolMappers'] if m['name']=='api-audience')['config']['included.client.audience'] = 'account'
    api(client_path, wrong, 'PUT', 204)
    result = login('traveler', verify=False)
    assert result['claims'].get('aud') == 'account', result['claims'].get('aud')
    assert result['statuses'] == {'identity':401,'traveler':401,'curator':401}, result['statuses']
    print('PASS genuine Keycloak-signed wrong-audience token returns 401')
    api(client_path, client, 'PUT', 204)
    short = copy.deepcopy(client)
    short['attributes']['access.token.lifespan'] = '2'
    api(client_path, short, 'PUT', 204)
    result = login('traveler')
    remaining = result['claims']['exp'] - time.time() + 7
    assert remaining < 15, remaining
    time.sleep(max(0,remaining))
    assert request('https://localhost:7100/api/identity', token=result['tokens']['access_token'])[0] == 401
    print('PASS genuine expired Keycloak token returns 401 (probe clock skew 5 seconds)')
finally:
    # Retain approved scope/default/logout settings; restore lifespan/audience and remove artificial role.
    if 'unrelated' in locals():
        api('/users/'+users['curator']['id']+'/role-mappings/realm', [unrelated], 'DELETE', 204)
        api('/roles/unrelated-probe', method='DELETE', expected=204)
    api(client_path, client, 'PUT', 204)
