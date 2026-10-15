// Throwaway, dependency-free visual exploration. Identity and mutations are in-memory simulations.
const $ = selector => document.querySelector(selector);
const params = new URLSearchParams(location.search);
const variants = { A: 'Editorial · warm card grid', B: 'Field guide · compact list', C: 'After hours · featured place' };
let variant = Object.hasOwn(variants, params.get('variant')) ? params.get('variant') : 'A';
let actor = 'anonymous', screen = 'browse', theme = 'branded', selected = 1;
let returnScreen = 'browse';
let returnPlace = null;
const saved = new Set([2]);
const places = [
  {id:1,name:'Café Letka',category:'Coffee',description:'A slow coffee, a sunny window, and a little time away from the conference.',address:'Letohradská 44, Prague',kind:'cafe'},
  {id:2,name:'Letná Gardens',category:'Outdoors',description:'Take the long way back. Open paths and a view across the city’s bridges.',address:'Letenské sady, Prague',kind:'park'},
  {id:3,name:'Kunsthalle Praha',category:'Culture',description:'A contemporary art stop in a former electrical substation.',address:'Klárov 5, Prague',kind:'gallery'}
];
const signedIn = () => actor !== 'anonymous';
const traveler = () => ['traveler','both'].includes(actor);
const curator = () => ['curator','both'].includes(actor);
const esc = value => String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const roles = () => actor === 'both' ? 'traveler, curator' : actor === 'none' ? 'None' : actor;
const name = () => actor === 'both' || actor === 'curator' ? 'Alex Curator' : actor === 'none' ? 'Sam Visitor' : 'Jamie Traveler';

function art(kind) {
  // Deliberate local placeholders, not acquired place photos. No remote requests/fonts.
  const scene = kind === 'cafe'
    ? '<rect width="600" height="350" fill="#dbc4a1"/><rect x="45" y="35" width="215" height="250" rx="100" fill="#819d8e"/><path d="M150 35v250M45 155h215" stroke="#3f6153" stroke-width="12"/><rect x="330" y="50" width="220" height="235" fill="#a46949"/><path d="M350 285V90h180v195" fill="#473e36"/><path d="M100 300h400" stroke="#5e4436" stroke-width="18"/><path d="M285 235h65v35h-65z" fill="#f8efda"/><path d="M350 240c35 0 35 25 0 25" fill="none" stroke="#f8efda" stroke-width="8"/>'
    : kind === 'park'
    ? '<rect width="600" height="350" fill="#aec7c1"/><circle cx="470" cy="70" r="37" fill="#f5dfaa"/><path d="M0 190Q170 140 340 190T600 175v175H0" fill="#638a6e"/><path d="M0 250q180-50 350-10t250-20v65H0" fill="#8faeaf"/><path d="M0 280h600M120 270v50m90-50v50m90-50v50m90-50v50m90-50v50" stroke="#c8c7af" stroke-width="12"/><path d="M65 350V70m-40 55 40-70 45 70M530 350V130m-45 55 45-85 45 85" fill="#385e48" stroke="#385e48" stroke-width="14"/>'
    : '<rect width="600" height="350" fill="#c6bcb0"/><path d="M50 310V70l250-50 250 50v240" fill="#e5dccb"/><path d="M50 70l250 35 250-35" fill="none" stroke="#8b8177" stroke-width="7"/><path d="M300 105v205" stroke="#b7ae9e" stroke-width="5"/><rect x="100" y="130" width="140" height="125" fill="#415d65"/><rect x="360" y="130" width="140" height="125" fill="#a05a42"/><circle cx="170" cy="190" r="38" fill="#dba957"/><path d="M365 245l80-100 50 100" fill="#c3b39c"/><path d="M0 315h600" stroke="#928a7d" stroke-width="15"/>';
  return `<svg class="art" viewBox="0 0 600 350" preserveAspectRatio="xMidYMid slice" aria-hidden="true">${scene}</svg>`;
}
function brand() { return '<a class="brand" href="#browse" data-screen="browse"><span class="brand-mark" aria-hidden="true">✦</span>City Picks</a>'; }
function header() {
  return `<header class="header">${brand()}<nav class="nav" aria-label="Main"><a href="#browse" data-screen="browse" ${screen==='browse'?'aria-current="page"':''}>Discover</a><a href="#favourites" data-screen="favourites" ${screen==='favourites'?'aria-current="page"':''}>My favourites</a>${signedIn()?`<a href="#profile" data-screen="profile" ${screen==='profile'?'aria-current="page"':''}>My profile</a>`:''}</nav><div class="account">${signedIn()?`<span><strong>${esc(name())}</strong><br><span class="badge">${esc(roles())}</span></span><button data-action="logout">Sign out</button>`:'<button class="primary" data-screen="login">Sign in</button>'}</div></header>`;
}
function saveButton(place) {
  if (!signedIn()) return `<button class="save" data-save="${place.id}">Sign in to save favourites</button>`;
  if (!traveler()) return '<span class="muted">Traveler role required to save</span>';
  return `<button class="save" data-save="${place.id}" aria-pressed="${saved.has(place.id)}" aria-label="${saved.has(place.id)?'Remove':'Save'} ${esc(place.name)} ${saved.has(place.id)?'from':'to'} favourites">${saved.has(place.id)?'♥ Saved':'♡ Save'}</button>`;
}
function card(place) {
  return `<article class="card">${art(place.kind)}<div class="card-body"><div class="category">${esc(place.category)}</div><h3>${esc(place.name)}</h3><p>${esc(place.description)}</p><div class="card-actions"><button class="link" data-details="${place.id}">View place<span class="sr-only"></span> ↗</button>${saveButton(place)}${curator()?`<button data-edit="${place.id}">Edit</button>`:''}</div></div></article>`;
}
function browse() {
  const intro = '<span class="eyebrow">The Prague edition</span><h1>Good places.<br>Great little detours.</h1>';
  const copy = '<p>A few local favourites for your time in Prague. Find a coffee, stretch your legs, make the most of being here.</p>';
  const title = `<div class="section-heading"><h2>Worth a visit</h2>${curator()?'<button class="primary" data-action="add">+ Add place</button>':'<small>Three places, picked for you</small>'}</div>`;
  if (variant === 'B') return `<div class="guide-layout"><aside class="guide-intro">${intro}${copy}<p class="muted">A small guide for the hours<br>between everything else.</p></aside><section class="guide-list" aria-label="Prepared places">${title}${places.map(card).join('')}</section></div>`;
  return `<section class="hero">${variant==='C'?`<div>${intro}</div>${copy}`:intro+copy}</section><section aria-label="Prepared places">${title}<div class="cards">${places.map(card).join('')}</div></section>`;
}
function gate(permission, content) {
  if (!signedIn()) return '<h1>Make it your own.</h1><p>Sign in to see your profile and save your own favourite places.</p><button class="primary" data-screen="login">Sign in</button>';
  if (permission && !permission()) return `<h1>Access denied</h1><div class="notice error"><strong>You’re signed in, but this action needs the ${screen==='edit'?'curator':'traveler'} role.</strong><br>Your identity does not automatically grant every permission.</div><button data-screen="browse">Back to places</button>`;
  return content();
}
function favourites() {
  return gate(traveler, () => `<span class="eyebrow">Your private collection</span><h1>My favourites</h1><p>Saved as ${esc(name())}. Only your own favourites appear here.</p>${saved.size?`<div class="cards">${places.filter(p=>saved.has(p.id)).map(card).join('')}</div>`:'<div class="empty"><h2>A little room for discovery.</h2><p>No favourites yet. Find a place you’d like to return to.</p><button class="primary" data-screen="browse">Discover places</button></div>'}`);
}
function profile() {
  const rows = values => `<dl class="claims">${values.map(([key,value])=>`<div><dt>${key}</dt><dd>${esc(value)}</dd></div>`).join('')}</dl>`;
  return gate(null, () => `<span class="eyebrow">Same person. Any sign-in method.</span><h1>My profile</h1><p>Two views of your identity. The API decides what you’re allowed to do.</p><div class="profile-grid"><section class="panel"><h2>Browser identity</h2><p class="muted">Selected sign-in identity claims, for display.</p>${rows([['Display name',name()],['Username',actor==='both'?'curator':actor],['Email','Not provided'],['Subject','ae2158b0-79e2-4dc9-93ad-7f3ef18f6b92'],['Issuer','https://localhost:8443/realms/city-picks']])}</section><section class="panel"><h2>API-validated identity</h2><p class="muted">Simulated response from the API’s identity endpoint.</p>${rows([['Subject','ae2158b0-79e2-4dc9-93ad-7f3ef18f6b92'],['Issuer','https://localhost:8443/realms/city-picks'],['Application roles',roles()],['Can save favourites',traveler()?'Yes':'No'],['Can add / edit places',curator()?'Yes':'No']])}</section></div><p class="auth-note">Mock claim values. No raw tokens. Authentication method is not inferred from claims.</p>`);
}
function editor() {
  const p = places.find(p=>p.id===selected) || {name:'',category:'Coffee',description:'',address:'',kind:'cafe'};
  return gate(curator, () => `<span class="eyebrow">Curator workspace</span><h1>${selected?'Edit place':'Add a place'}</h1><p>Simple, shared recommendations. All fields are required.</p><form id="editor" class="form"><label>Name<input name="name" value="${esc(p.name)}" required></label><label>Category<input name="category" value="${esc(p.category)}" required></label><label>Short description<textarea name="description" required>${esc(p.description)}</textarea></label><label>Address<input name="address" value="${esc(p.address)}" required></label><label>Prepared local image<select name="kind">${['cafe','park','gallery'].map(k=>`<option ${p.kind===k?'selected':''}>${k}</option>`).join('')}</select></label><small>Prototype illustrations stand in for the prepared photo set. No uploads.</small><div class="row"><button class="primary" type="submit">Save place</button><button type="button" data-screen="browse">Cancel</button></div></form>`);
}
function auth() {
  let content = '';
  if (screen === 'passkey') content = '<h1>Sign in with a passkey</h1><p>Continue with your browser or device.</p><div class="prompt"><strong>Native prompt boundary</strong><p>Chrome and the credential provider take over here. This placeholder is not a preview of Touch ID or a verified Keycloak page.</p><button class="primary" data-action="passkey-success">Simulate successful sign-in</button></div><button class="link" data-screen="cancelled">Simulate cancel / timeout</button>';
  else if (screen === 'register') content = '<h1>Create your account</h1><p>Your account starts with the traveler role.</p><form id="registration" class="form"><label>Username<input autocomplete="username" required></label><label>Email <small>Optional in this mockup; final fields follow realm configuration</small><input type="email" autocomplete="email"></label><label>Password<input type="password" autocomplete="new-password" required></label><label>Confirm password<input type="password" autocomplete="new-password" required></label><button class="primary">Create account</button></form><button class="link" data-screen="login">Back to sign in</button>';
  else content = `<h1>Welcome back</h1><p>Sign in to City Picks.</p>${screen==='error'?'<div class="notice error" role="alert">Invalid username or password. Please try again.</div>':''}${screen==='cancelled'?'<div class="notice" role="status">The passkey attempt didn’t complete. Try again, or sign in with your password.</div>':''}<form id="login" class="form"><label>Username<input autocomplete="username" value="traveler" required></label><label>Password<input autocomplete="current-password" type="password" required></label><button class="primary">Sign in</button></form><div class="divider">or</div><button class="passkey" data-screen="passkey">Sign in with Passkey</button><p class="auth-note">New here? <button class="link" data-screen="register">Create an account</button></p><button class="link" data-screen="error">Preview invalid credentials</button>`;
  const frame = `<div class="auth"><aside class="auth-story"><span class="eyebrow">City Picks</span><h1>Your next<br>little discovery.</h1><p>One account. Your favourites.<br>A few good places to come back to.</p><small>Shared branding · no city-specific theme</small></aside><section class="auth-content">${content}<p class="auth-note">Identity is handled by Keycloak. This is a visual mockup, not a working authentication form.</p><button class="link" data-action="back">← Back to City Picks</button></section></div>`;
  return theme==='stock'?`<div class="stock"><h2 class="stock-title">CITY-PICKS</h2>${frame}<p class="stock-caption">Approximate stock-style comparison, not a pixel-accurate capture of Keycloak 26.8.0. Account Console stays stock in every direction.</p></div>`:frame;
}
function render() {
  document.body.className = `variant-${variant}`;
  $('#actor').value=actor; $('#screen').value=screen; $('#theme').value=theme;
  const authScreen = ['login','register','passkey','cancelled','error'].includes(screen);
  const pages = {browse,favourites,profile,edit:editor};
  $('#app').innerHTML = `<div class="shell">${authScreen?`<header class="header">${brand()}<span class="muted">Keycloak sign-in surface · visual mockup</span></header>`:header()}<main id="main" tabindex="-1">${authScreen?auth():screen==='browse'?browse():`<section class="body-page">${pages[screen]()}</section>`}</main><footer class="footer">City Picks · ${authScreen?'Shared login branding':'Prague edition'} · Illustration placeholders, not final place photography · In-memory prototype only</footer></div>`;
  $('#variant-name').textContent = `${variant} — ${variants[variant]}`;
  $('#state').textContent = `${actor} · ${screen} · ${theme} login · ${saved.size} mock favourite${saved.size===1?'':'s'}`;
}
function navigate(next) { if(['login','register','passkey'].includes(next) && !['login','register','passkey','cancelled','error'].includes(screen)) returnScreen=screen; screen=next; render(); $('#main').focus({preventScroll:true}); }
function announce(text) { $('#announcement').textContent=text; clearTimeout(announce.timer); announce.timer=setTimeout(()=>$('#announcement').textContent='',5000); }
function cycle(delta) { variant=['A','B','C'][(['A','B','C'].indexOf(variant)+delta+3)%3]; const url=new URL(location.href);url.searchParams.set('variant',variant);history.replaceState({},'',url);render(); }
$('#previous').onclick=()=>cycle(-1);$('#next').onclick=()=>cycle(1);
$('#actor').onchange=e=>{actor=e.target.value;render();};
$('#screen').onchange=e=>navigate(e.target.value);
$('#theme').onchange=e=>{theme=e.target.value;render();};
document.addEventListener('keydown', e=>{if(e.target.closest('input,textarea,select,[contenteditable]')||e.altKey||e.metaKey||e.ctrlKey||$('dialog[open]'))return;if(e.key==='ArrowLeft'||e.key==='ArrowRight'){e.preventDefault();cycle(e.key==='ArrowLeft'?-1:1);}});
document.addEventListener('click', e=>{
  const button=e.target.closest('[data-screen],[data-save],[data-details],[data-edit],[data-action]'); if(!button)return;
  e.preventDefault();
  if(button.dataset.screen){navigate(button.dataset.screen);return;}
  if(button.dataset.save){
    selected=Number(button.dataset.save);
    if(!signedIn()){ returnPlace=selected;$('dialog')?.remove();navigate('login');announce('Sign in, then explicitly save the place.');return;}
    if(traveler()){saved.has(selected)?saved.delete(selected):saved.add(selected);const stillOpen=$('dialog[open]');if(stillOpen)stillOpen.remove();render();announce(saved.has(selected)?'Saved to your favourites.':'Removed from your favourites.');}return;
  }
  if(button.dataset.details){
    selected=Number(button.dataset.details);const p=places.find(p=>p.id===selected);$('dialog')?.remove();
    const dialog=document.createElement('dialog');dialog.className='details-dialog';dialog.setAttribute('aria-label',p.name);
    dialog.innerHTML=`${art(p.kind)}<div class="content"><div class="category">${esc(p.category)}</div><h2>${esc(p.name)}</h2><p>${esc(p.description)}</p><p class="address">${esc(p.address)}</p><div class="row">${saveButton(p)}${curator()?`<button data-edit="${p.id}">Edit place</button>`:''}<button data-action="close-dialog">Close</button></div></div>`;document.body.append(dialog);dialog.showModal();return;
  }
  if(button.dataset.edit){selected=Number(button.dataset.edit);$('dialog')?.remove();navigate('edit');return;}
  const action=button.dataset.action;
  if(action==='close-dialog'){$('dialog').close();return;}
  if(action==='logout'){actor='anonymous';navigate('browse');announce('Simulated sign-out. Saved state is retained in memory.');}
  if(action==='add'){selected=0;navigate('edit');}
  if(action==='back')navigate(returnScreen);
  if(action==='passkey-success'){actor='traveler';navigate(returnScreen);restorePlace();announce('Simulated passkey sign-in: same traveler, same favourite.');}
});
function restorePlace() { if(returnPlace){const id=returnPlace;returnPlace=null;document.querySelector(`[data-details="${id}"]`)?.click();} }
document.addEventListener('submit',e=>{
  e.preventDefault();
  if(e.target.id==='login'||e.target.id==='registration'){actor='traveler';navigate(returnScreen);restorePlace();announce('Simulated sign-in. Nothing was saved automatically.');}
  if(e.target.id==='editor'){
    const values=Object.fromEntries(new FormData(e.target));
    if(Object.values(values).some(v=>!v.trim())){announce('All place fields must contain text.');return;}
    if(selected)Object.assign(places.find(p=>p.id===selected),values);else places.push({id:Date.now(),...values});
    navigate('browse');announce('Place saved in memory only.');
  }
});
render();
