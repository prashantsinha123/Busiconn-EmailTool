// Local UI fixture only. Never used by the production server; no real messages are sent.
import http from 'node:http';
const users = [
 {id:'11111111-1111-1111-1111-111111111111',name:'Sample Rahul',email:'rahul@example.com',mobileNumber:'+919876543210',roles:['Buyer'],isActive:true,validEmail:true},
 {id:'22222222-2222-2222-2222-222222222222',name:'Sample Priya',email:'priya@example.com',mobileNumber:'+919123456789',roles:['Seller'],isActive:true,validEmail:true},
 {id:'33333333-3333-3333-3333-333333333333',name:'Sample Missing',email:null,mobileNumber:null,roles:[],isActive:true,validEmail:false}
];
http.createServer((req,res)=>{
 res.setHeader('Content-Type','application/json');
 if(req.headers['x-tool-key']!=='ui-fixture'){res.writeHead(401);res.end(JSON.stringify({error:'Fixture key required'}));return;}
 if(req.url==='/api/config'){res.end(JSON.stringify({fromName:'Busiconn',fromEmail:'updates@busiconn.com'}));return;}
 if(req.url==='/api/users'){res.end(JSON.stringify(users));return;}
 res.writeHead(400);res.end(JSON.stringify({error:'Fixture cannot send email.'}));
}).listen(5080,'localhost',()=>console.log('UI fixture http://localhost:5080; key ui-fixture; sends disabled'));
