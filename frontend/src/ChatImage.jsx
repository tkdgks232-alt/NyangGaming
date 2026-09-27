import React,{useEffect,useState} from 'react';
export async function prepareChatImage(file){
 if(!file||!['image/png','image/jpeg','image/webp'].includes(file.type))throw Error('PNG, JPG, WebP 이미지 파일을 선택해 주세요.');
 if(file.size>10*1024*1024)throw Error('10MB 이하의 이미지를 선택해 주세요.');
 const url=URL.createObjectURL(file);
 try{
  const image=new Image();await new Promise((resolve,reject)=>{image.onload=resolve;image.onerror=()=>reject(Error('이미지를 읽을 수 없어요.'));image.src=url;});
  if(!image.naturalWidth||image.naturalWidth>8192||image.naturalHeight>8192||image.naturalWidth*image.naturalHeight>32000000)throw Error('이미지 크기가 너무 커요. 8192px·3,200만 화소 이하로 줄여 주세요.');
  const scale=Math.min(1,1920/Math.max(image.naturalWidth,image.naturalHeight)),canvas=document.createElement('canvas');canvas.width=Math.max(1,Math.round(image.naturalWidth*scale));canvas.height=Math.max(1,Math.round(image.naturalHeight*scale));
  const ctx=canvas.getContext('2d');ctx.fillStyle='#fff';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.drawImage(image,0,0,canvas.width,canvas.height);
  let data;for(const quality of [.88,.76,.62,.45]){data=canvas.toDataURL('image/jpeg',quality);if((data.length-23)*3/4<=2*1024*1024)break;}
  if((data.length-23)*3/4>2*1024*1024)throw Error('이미지를 더 작게 줄여서 선택해 주세요.');
  return {data,name:file.name||'붙여넣은 이미지',width:canvas.width,height:canvas.height};
 }finally{URL.revokeObjectURL(url);}
}
export function ChatImage({src,onOpen,onReload}){
 const [failed,setFailed]=useState(false);useEffect(()=>setFailed(false),[src]);
 return src&&!failed?<button type="button" className="chat-image-button" aria-label="사진 크게 보기" onClick={()=>onOpen(src)}><img src={src} alt="전송한 사진" loading="lazy" onError={()=>setFailed(true)}/></button>:<button type="button" className="button secondary" onClick={onReload}>사진 다시 불러오기</button>;
}
