import {useEffect,useState} from 'react';
export const initial={settings:{brand:'Nyang Gaming',theme:'pink',demo:true,artworkOpacity:.65,paths:{},artwork:{}},games:[{id:'genshin',name:'원신',running:false,today:0,week:0},{id:'eternal',name:'이터널 리턴',running:false,today:0,week:0},{id:'valorant',name:'VALORANT',running:false,today:0,week:0},{id:'league',name:'League of Legends',running:false,today:0,week:0}],metrics:{}};
export function send(type,payload={}){window.chrome?.webview?.postMessage({version:1,type,...payload});}
export function useHost(){const [data,setData]=useState(initial),[notice,setNotice]=useState(''),[connected,setConnected]=useState(false);
 useEffect(()=>{const receive=e=>{if(e.data?.version!==1)return;if(e.data.type==='snapshot'){setData(e.data.payload);setConnected(true);}if(e.data.type==='notice')setNotice(e.data.message);};window.chrome?.webview?.addEventListener('message',receive);send('ready',{buildVersion:__APP_VERSION__});return()=>window.chrome?.webview?.removeEventListener('message',receive);},[]);
 useEffect(()=>{if(!notice)return;const t=setTimeout(()=>setNotice(''),6500);return()=>clearTimeout(t);},[notice]);
 return {data,notice,connected};}
export function duration(seconds){const n=Math.max(0,Math.floor(seconds||0));return n<60?'0분':n<3600?`${Math.floor(n/60)}분`:`${Math.floor(n/3600)}시간 ${Math.floor(n%3600/60)}분`;}
