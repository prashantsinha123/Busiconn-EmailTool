import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';
type User = { id:string; name:string|null; email:string|null; mobileNumber:string|null; roles:string[]; isActive:boolean; validEmail:boolean };
type Summary = { selected:number; validRecipients:number; sent:number; failed:number; skipped:number; results:{userId:string|null;email:string|null;status:string;reason:string|null}[] };
const eligible = (u:User) => u.validEmail && u.isActive;
function App() {
 const [key,setKey]=useState(''), [users,setUsers]=useState<User[]>([]), [selected,setSelected]=useState<Set<string>>(new Set());
 const [search,setSearch]=useState(''), [role,setRole]=useState(''), [active,setActive]=useState('active'), [page,setPage]=useState(1);
 const [subject,setSubject]=useState(''), [body,setBody]=useState('<p>Hi {{name}},</p>\n<p>We have launched something new on Busiconn.</p>\n<p>Regards,<br>Team Busiconn</p>');
 const [testEmail,setTestEmail]=useState(''), [testToken,setTestToken]=useState('');
 const [sender,setSender]=useState({fromName:'Busiconn',fromEmail:'updates@busiconn.com'});
 const [busy,setBusy]=useState(false), [message,setMessage]=useState(''), [confirm,setConfirm]=useState(false), [summary,setSummary]=useState<Summary|null>(null);
 useEffect(()=>{setTestToken('')},[subject,body,key]);
 useEffect(()=>{setPage(1)},[search,role,active]);
 async function api<T>(path:string,payload?:unknown):Promise<T>{
  const response=await fetch(path,{method:payload===undefined?'GET':'POST',headers:{'X-Tool-Key':key,...(payload===undefined?{}:{'Content-Type':'application/json'})},...(payload===undefined?{}:{body:JSON.stringify(payload)})});
  const data=await response.json(); if(!response.ok) throw new Error(data.error||'Request failed.'); return data;
 }
 async function run(action:()=>Promise<void>){setBusy(true);setMessage('');try{await action()}catch(e){setMessage(e instanceof Error?e.message:'Request failed.')}finally{setBusy(false)}}
 const filtered=users.filter(u=>(!role||u.roles.includes(role))&&(!active||u.isActive===(active==='active'))&&[u.name,u.email,u.mobileNumber].some(v=>v?.toLowerCase().includes(search.toLowerCase())));
 const pages=Math.max(1,Math.ceil(filtered.length/25)), visible=filtered.slice((page-1)*25,page*25), roles=[...new Set(users.flatMap(u=>u.roles))].sort();
 const escape=(s:string)=>s.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
 const sample=users.find(u=>selected.has(u.id));
 const preview=body.replaceAll('{{name}}',escape(sample?.name||'there')).replaceAll('{{email}}',escape(sample?.email||testEmail));
 return <main className="max-w-6xl mx-auto p-6 space-y-6">
  <header><h1 className="text-2xl font-semibold">Busiconn Email Tool</h1><p className="text-sm text-slate-500 mt-1">Campaigns / Send Email</p></header>
  <div className="card flex flex-wrap gap-3 items-end"><label>Local tool access key<input type="password" autoComplete="off" value={key} onChange={e=>setKey(e.target.value)} className="block mt-1"/></label>
  <button disabled={busy||!key} onClick={()=>run(async()=>{setSender(await api<typeof sender>('/api/config'));const loaded=await api<User[]>('/api/users');setUsers(loaded);setSelected(new Set());setPage(1);setMessage('Loaded '+loaded.length+' users through the Busiconn API.');})}>Load / Refresh Users</button>
  <span className="text-sm text-slate-500">SMS / WhatsApp campaigns — Coming later</span></div>
  {message&&<p role="status" className="rounded-lg bg-blue-50 p-4 text-sm">{message}</p>}
  <section className="card space-y-4"><h2 className="text-lg font-semibold">Recipients</h2>
  <div className="grid grid-cols-2 md:grid-cols-4 gap-4">{[['Users loaded',users.length],['Valid emails',users.filter(u=>u.validEmail).length],['Missing / invalid emails',users.filter(u=>!u.validEmail).length],['Selected',selected.size]].map(([title,count])=><div key={title} className="bg-slate-50 p-4 rounded-lg"><p className="text-sm text-slate-500">{title}</p><strong className="text-2xl">{count}</strong></div>)}</div>
  <div className="flex flex-wrap gap-3"><input aria-label="Search recipients" placeholder="Search name, email, mobile" value={search} onChange={e=>setSearch(e.target.value)} className="flex-1 min-w-56"/>
  <select aria-label="Filter role" value={role} onChange={e=>setRole(e.target.value)}><option value="">All roles</option>{roles.map(r=><option key={r}>{r}</option>)}</select>
  <select aria-label="Filter status" value={active} onChange={e=>setActive(e.target.value)}><option value="active">Active</option><option value="inactive">Inactive</option><option value="">All statuses</option></select>
  <button disabled={busy} onClick={()=>setSelected(new Set([...selected,...filtered.filter(eligible).map(u=>u.id)]))}>Select all filtered</button><button disabled={busy} onClick={()=>setSelected(new Set())}>Deselect all</button></div>
  <div className="overflow-x-auto"><table className="w-full"><thead><tr><th>Select</th><th>Name</th><th>Email</th><th>Mobile</th><th>Roles / Status</th></tr></thead><tbody>
  {visible.map(u=><tr key={u.id}><td><input type="checkbox" aria-label={'Select '+(u.name||u.email||u.id)} disabled={busy||!eligible(u)} checked={selected.has(u.id)} onChange={e=>{const next=new Set(selected);if(e.target.checked)next.add(u.id);else next.delete(u.id);setSelected(next)}}/></td><td>{u.name||'—'}</td><td>{u.email||'—'}{!u.validEmail&&<span className="block text-xs text-amber-700">Unavailable for sending</span>}</td><td>{u.mobileNumber||'—'}</td><td>{u.roles.join(', ')||'—'}<span className="block text-xs text-slate-500">{u.isActive?'Active':'Inactive'}</span></td></tr>)}
  {!visible.length&&<tr><td colSpan={5}>No matching users. Load users to begin.</td></tr>}</tbody></table></div>
  <div className="flex gap-3 items-center text-sm"><button disabled={page<=1} onClick={()=>setPage(page-1)}>Previous</button><span>Page {page} of {pages} · {filtered.length} matches</span><button disabled={page>=pages} onClick={()=>setPage(page+1)}>Next</button></div></section>
  <section className="card space-y-4"><h2 className="text-lg font-semibold">Compose Campaign</h2><p className="text-sm">From: {sender.fromName} &lt;{sender.fromEmail}&gt;</p>
  <label>Subject<input className="block w-full mt-1" maxLength={200} disabled={busy} value={subject} onChange={e=>setSubject(e.target.value)}/></label>
  <label>Message (HTML)<textarea className="block w-full mt-1 font-mono" rows={8} maxLength={100000} disabled={busy} value={body} onChange={e=>setBody(e.target.value)}/></label>
  <p className="text-xs text-slate-500">Placeholders: {'{{name}}'} and {'{{email}}'}. Missing names become “there”.</p>
  <details><summary className="cursor-pointer text-sm">Preview email</summary><iframe title="Email preview" sandbox="" referrerPolicy="no-referrer" className="w-full h-64 border mt-3" srcDoc={'<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src \'unsafe-inline\'; img-src data:"><body>'+preview+'</body>'}/></details>
  <div className="flex flex-wrap gap-3 items-end"><label>Test recipient<input type="email" className="block mt-1" disabled={busy} value={testEmail} onChange={e=>setTestEmail(e.target.value)}/></label>
  <button disabled={busy||!key||!subject.trim()||!body.trim()||!testEmail} onClick={()=>run(async()=>{setTestToken('');const result=await api<{testToken:string}>('/api/campaign/test',{email:testEmail,subject,htmlContent:body});setTestToken(result.testToken);setMessage('One test email accepted by SendGrid. Review it before confirming the campaign.');})}>Send Test Email</button>
  <button className="primary ml-auto" disabled={busy||!selected.size||!testToken} onClick={()=>setConfirm(true)}>Send Campaign</button></div>
  <p className="text-xs text-slate-500">{busy?'Processing — keep this page open. Campaigns may take several minutes.':'A successful test of this subject and body is required before each campaign.'}</p></section>
  {summary&&<section className="card space-y-3"><h2 className="text-lg font-semibold">Campaign results</h2><p>Selected: {summary.selected} · Valid recipients: {summary.validRecipients} · Sent: {summary.sent} · Failed: {summary.failed} · Skipped: {summary.skipped}</p><p className="text-xs text-slate-500">Sent means accepted by SendGrid; inbox delivery is not tracked.</p><details><summary>Recipient results</summary><ul className="text-sm max-h-72 overflow-auto">{summary.results.map((r,i)=><li key={i} className="py-1">{r.email||r.userId} — {r.status}{r.reason&&': '+r.reason}</li>)}</ul></details></section>}
  <p className="text-xs text-slate-500">No existing marketing preference field was found in the backend API.</p>
  {confirm&&<div className="fixed inset-0 bg-black/40 flex items-center justify-center p-4"><div role="dialog" aria-modal="true" aria-labelledby="confirm-title" className="card w-full max-w-md space-y-4"><h2 id="confirm-title" className="text-lg font-semibold">Campaign Summary</h2><p>Recipients: {selected.size}</p><p>Subject: {subject}</p><p>From: {sender.fromName} &lt;{sender.fromEmail}&gt;</p><p className="text-sm text-slate-500">Recipient details will be refreshed before sending.</p>
  <div className="flex gap-3"><button className="primary" disabled={busy} onClick={()=>{setConfirm(false);setSummary(null);const token=testToken;setTestToken('');run(async()=>{setSummary(await api<Summary>('/api/campaign/send',{userIds:[...selected],subject,htmlContent:body,testToken:token}));setMessage('Campaign completed.');});}}>Confirm Send</button><button disabled={busy} onClick={()=>setConfirm(false)}>Cancel</button></div></div></div>}
 </main>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
