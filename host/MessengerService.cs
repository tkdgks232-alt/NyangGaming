using System;using System.Collections.Generic;using System.Linq;using System.IO;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Net.WebSockets;using System.Web.Script.Serialization;
internal sealed class MessengerView {
 public bool connected,busy,notifications=true,hasMore,loadingOlder;
 public string message="채팅 연결을 준비하고 있어요.",room,sentId,failedId,uploadState="";
 public object[] rooms=new object[0],profiles=new object[0],messages=new object[0];
 public Dictionary<string,string> avatars=new Dictionary<string,string>();
 public Dictionary<string,string> images=new Dictionary<string,string>();
 public long unread;public bool pageVisible;
}
internal sealed class MessengerPrefs {public bool notifications=true;}
internal sealed class MessengerService:IDisposable {
 internal readonly MessengerView View=new MessengerView();internal event Action Changed;internal event Action<string,string,string> Notification;internal event Action Navigate;
 readonly FriendsService owner;readonly bool verify;readonly string root;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=1024*1024};
 readonly SemaphoreSlim actions=new SemaphoreSlim(1,1);readonly CancellationTokenSource stop=new CancellationTokenSource();
 Task socketTask;ClientWebSocket socket;bool disposed,refreshing,dirty=true,initialized,foreground;int roomVersion;DateTime syncDue=DateTime.MinValue,avatarDue=DateTime.MinValue;string lastReadSent;readonly Dictionary<string,long> seen=new Dictionary<string,long>();
 internal MessengerService(FriendsService service,string folder,bool test){owner=service;root=folder;verify=test;if(!test)try{View.notifications=json.Deserialize<MessengerPrefs>(File.ReadAllText(Path.Combine(root,"chat-settings.json"))).notifications;}catch{}}
 void Notify(){if(!disposed&&Changed!=null)Changed();}
 internal void Settings(bool enabled){View.notifications=enabled;if(!verify)LeagueJson.AtomicBytes(Path.Combine(root,"chat-settings.json"),Encoding.UTF8.GetBytes(json.Serialize(new MessengerPrefs{notifications=enabled})));Notify();}
 internal void Visible(bool value){View.pageVisible=value;}
 internal bool Viewing(string room){return foreground&&View.pageVisible&&View.room==room;}
 internal async Task Tick(bool active){foreground=active;if(verify||disposed||owner.ChatUser=="")return;
  if(socketTask==null||socketTask.IsCompleted)socketTask=SocketLoop();
  if(dirty||DateTime.UtcNow>=syncDue)await Refresh();
 }
 internal async Task Refresh(){if(verify||disposed||refreshing||owner.ChatUser=="")return;refreshing=true;dirty=false;
  try{
   var state=await owner.ChatRpc("nyang_chat_state",new{});var rooms=ErJson.Rows(ErJson.Get(state,"rooms")).Cast<object>().ToArray();
   foreach(var r in rooms){string id=ErJson.Text(r,"id");long seq=ErJson.Long(r,"lastSeq"),previous;bool known=seen.TryGetValue(id,out previous);var latest=ErJson.Get(r,"latest");
    if(initialized&&(!known||seq>previous)&&latest!=null&&ErJson.Text(latest,"sender")!=owner.ChatUser&&ErJson.Long(r,"unread")>0&&View.notifications&&object.Equals(ErJson.Get(r,"notifications"),true)&&!Viewing(id)){
     string sender=ErJson.Text(latest,"sender");var person=ErJson.Rows(ErJson.Get(r,"members")).FirstOrDefault(p=>ErJson.Text(p,"id")==sender);string title=ErJson.Text(person,"nickname");if(ErJson.Text(r,"kind")=="group")title=ErJson.Text(r,"name")+" · "+title;
     if(Notification!=null)Notification(id,title,ErJson.Text(latest,"body"));
    }seen[id]=Math.Max(previous,seq);
   }
   initialized=true;View.rooms=rooms;View.profiles=ErJson.Rows(ErJson.Get(state,"profiles")).Cast<object>().ToArray();View.unread=rooms.Sum(r=>ErJson.Long(r,"unread"));
   if(View.room!=null&&!rooms.Any(r=>ErJson.Text(r,"id")==View.room)){View.room=null;View.messages=new object[0];roomVersion++;}
   if(View.room!=null)await LoadLatest();await Avatars();await Images();syncDue=DateTime.UtcNow.AddSeconds(60);View.message=View.connected?"실시간으로 연결됐어요.":"실시간 연결을 복구하고 있어요. 대화는 안전하게 저장됩니다.";
  }catch{View.message="연결이 끊겼거나 채팅 서버 설정이 필요해요. 재연결 중…";syncDue=DateTime.UtcNow.AddSeconds(30);}
  finally{refreshing=false;Notify();}
 }
 async Task LoadLatest(){string room=View.room;int version=roomVersion;var data=await owner.ChatRpc("nyang_chat_page",new{p_room=room,p_before=(long?)null});if(room!=View.room||version!=roomVersion)return;
  var incoming=ErJson.Rows(data).Cast<object>().ToArray();bool first=View.messages.Length==0;
  // Merge live results with pages already loaded. A reconnect may have skipped >50 messages;
  // preserve a contiguous latest page and allow older pages to load again in that case.
  long oldMax=View.messages.Select(m=>ErJson.Long(m,"seq")).DefaultIfEmpty(0).Max();long newMin=incoming.Select(m=>ErJson.Long(m,"seq")).DefaultIfEmpty(0).Min();
  if(!first&&incoming.Length==50&&newMin>oldMax){View.messages=incoming;View.hasMore=true;}
  else{View.messages=View.messages.Concat(incoming).GroupBy(m=>ErJson.Text(m,"id")).Select(g=>g.Last()).OrderBy(m=>ErJson.Long(m,"seq")).ToArray();if(first)View.hasMore=incoming.Length==50;}
 }
 async Task Avatars(){var paths=View.profiles.Select(p=>ErJson.Text(p,"avatarPath")).Concat(View.rooms.Select(r=>ErJson.Text(r,"avatarPath"))).Concat(View.rooms.SelectMany(r=>ErJson.Rows(ErJson.Get(r,"members")).Select(p=>ErJson.Text(p,"avatarPath")))).Where(p=>p.Length>0).Distinct().ToArray();
  if(paths.Length==0)return;if(DateTime.UtcNow<avatarDue&&paths.All(p=>View.avatars.ContainsKey(p)))return;
  try{var signed=await owner.ChatPost("storage/v1/object/sign/nyang-avatars",new{paths=paths,expiresIn=300});var map=new Dictionary<string,string>();foreach(var row in ErJson.Rows(signed)){string path=ErJson.Text(row,"path"),url=ErJson.Text(row,"signedURL");if(path.Length>0&&url.StartsWith("/object/sign/"))map[path]=owner.ChatUrl+"storage/v1"+url;}View.avatars=map;avatarDue=DateTime.UtcNow.AddMinutes(4);}catch{}
 }
 internal async Task Open(string room){Guid id;if(!Guid.TryParse(room,out id))return;dirty=true;View.room=room;View.messages=new object[0];View.hasMore=false;roomVersion++;lastReadSent=null;View.pageVisible=true;if(Navigate!=null)Navigate();Notify();await Refresh();}
 DateTime imageDue=DateTime.MinValue;
 internal async Task ReloadImages(){imageDue=DateTime.MinValue;await Images();Notify();}
 async Task Images(){string room=View.room;int version=roomVersion;var paths=View.messages.Select(m=>ErJson.Text(m,"image_path")).Where(p=>p.Length>0).Distinct().ToArray();if(paths.Length==0){View.images=new Dictionary<string,string>();return;}if(DateTime.UtcNow<imageDue&&paths.All(p=>View.images.ContainsKey(p)))return;
  try{var map=new Dictionary<string,string>();for(int i=0;i<paths.Length;i+=100){var rows=await owner.ChatPost("storage/v1/object/sign/nyang-chat-images",new{paths=paths.Skip(i).Take(100).ToArray(),expiresIn=300});foreach(var row in ErJson.Rows(rows)){string path=ErJson.Text(row,"path"),url=ErJson.Text(row,"signedURL");if(path.Length>0&&url.StartsWith("/object/sign/"))map[path]=owner.ChatUrl+"storage/v1"+url;}}if(room==View.room&&version==roomVersion){View.images=map;imageDue=DateTime.UtcNow.AddMinutes(4);}}catch{}
 }
 internal Task SendImage(string room,string body,string id,string data){return Work(async()=>{Guid r,n;if(!Guid.TryParse(room,out r)||!Guid.TryParse(id,out n))return;View.failedId=null;try{
  if(data==null||!data.StartsWith("data:image/jpeg;base64,")||data.Length>2800000||(body??"").Length>1000)throw new InvalidOperationException();byte[] bytes=Convert.FromBase64String(data.Substring(23));int width,height;
  if(bytes.Length>2097152)throw new InvalidOperationException();using(var stream=new MemoryStream(bytes))using(var picture=System.Drawing.Image.FromStream(stream)){width=picture.Width;height=picture.Height;if(width<1||height<1||width>1920||height>1920||picture.RawFormat.Guid!=System.Drawing.Imaging.ImageFormat.Jpeg.Guid)throw new InvalidOperationException();}
  string path=r.ToString()+"/"+owner.ChatUser+"/"+n.ToString()+".jpg";await owner.ChatUpload(path,bytes,"nyang-chat-images");await owner.ChatRpc("nyang_chat_image",new{p_room=room,p_id=id,p_body=body??"",p_width=width,p_height=height});View.sentId=id;dirty=true;imageDue=DateTime.MinValue;await Refresh();
 }catch{View.failedId=id;View.message="사진을 전송하지 못했어요. 이미지 서버 설정과 연결 상태를 확인하고 다시 보내 주세요.";}});}
 async Task Work(Func<Task> job){if(verify||disposed)return;await actions.WaitAsync();View.busy=true;Notify();try{await job();}catch{View.message="요청을 완료하지 못했어요. 연결 상태와 참여 권한을 확인해 주세요.";}finally{View.busy=false;actions.Release();Notify();}}
 internal Task Direct(string user){return Work(async()=>{Guid id;if(!Guid.TryParse(user,out id))return;var room=await owner.ChatRpc("nyang_chat_direct",new{p_user=user});await Open(Convert.ToString(room));});}
 internal Task Group(string name,string[] users,string id){return Work(async()=>{Guid nonce;if(!Guid.TryParse(id,out nonce)||users.Length<1||users.Length>19)return;var room=await owner.ChatRpc("nyang_chat_group",new{p_name=name,p_users=users,p_id=id});await Open(Convert.ToString(room));});}
 internal Task Manage(string room,string action,string name,string user,bool? enabled){return Work(async()=>{await owner.ChatRpc("nyang_chat_manage",new{p_room=room,p_action=action,p_name=name,p_user=string.IsNullOrEmpty(user)?null:user,p_enabled=enabled});dirty=true;await Refresh();});}
 internal Task SendMessage(string room,string body,string id){return Work(async()=>{Guid r,n;if(!Guid.TryParse(room,out r)||!Guid.TryParse(id,out n)||string.IsNullOrWhiteSpace(body)||body.Length>1000)return;
  View.failedId=null;try{await owner.ChatRpc("nyang_chat_send",new{p_room=room,p_body=body,p_id=id});View.sentId=id;dirty=true;await Refresh();}catch{View.failedId=id;View.message="전송에 실패했어요. 다시 보내기를 눌러 주세요.";}
 });}
 internal Task Older(){return Work(async()=>{if(View.room==null||!View.hasMore||View.messages.Length==0)return;string room=View.room;int version=roomVersion;View.loadingOlder=true;Notify();try{var data=await owner.ChatRpc("nyang_chat_page",new{p_room=room,p_before=View.messages.Min(m=>ErJson.Long(m,"seq"))});if(room!=View.room||version!=roomVersion)return;var rows=ErJson.Rows(data).Cast<object>().ToArray();View.messages=rows.Concat(View.messages).GroupBy(m=>ErJson.Text(m,"id")).Select(g=>g.Last()).OrderBy(m=>ErJson.Long(m,"seq")).ToArray();View.hasMore=rows.Length==50;await Images();}finally{View.loadingOlder=false;}});}
 internal async Task Read(string room,long seq){if(verify||!Viewing(room)||seq<=0||!View.messages.Any(m=>ErJson.Long(m,"seq")==seq)||lastReadSent==room+":"+seq)return;lastReadSent=room+":"+seq;try{await owner.ChatRpc("nyang_chat_read",new{p_room=room,p_seq=seq});dirty=true;}catch{lastReadSent=null;}}
 internal Task Avatar(string data,string room){return Work(async()=>{View.uploadState="이미지 저장 중…";Notify();try{
  if(data==null||!data.StartsWith("data:image/jpeg;base64,")||data.Length>180000)throw new InvalidOperationException();byte[] bytes=Convert.FromBase64String(data.Substring(23));
  if(bytes.Length>131072)throw new InvalidOperationException();using(var stream=new MemoryStream(bytes))using(var image=System.Drawing.Image.FromStream(stream)){if(image.Width!=256||image.Height!=256||image.RawFormat.Guid!=System.Drawing.Imaging.ImageFormat.Jpeg.Guid)throw new InvalidOperationException();}
  string path=owner.ChatUser+"/"+Guid.NewGuid()+".jpg";await owner.ChatUpload(path,bytes);await owner.ChatRpc("nyang_chat_avatar",new{p_path=path,p_room=string.IsNullOrEmpty(room)?null:room});avatarDue=DateTime.MinValue;dirty=true;await Refresh();View.uploadState="프로필 이미지를 저장했어요.";
 }catch{View.uploadState="이미지를 저장하지 못했어요. 연결과 파일을 확인해 주세요.";}});}
 async Task Push(ClientWebSocket ws,string topic,string ev,object payload,string reference,CancellationToken token){var packet=new Dictionary<string,object>{{"topic",topic},{"event",ev},{"payload",payload},{"ref",reference},{"join_ref","1"}};byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(packet));await ws.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,token);}
 async Task<string> Receive(ClientWebSocket ws,CancellationToken token){var buffer=new byte[8192];using(var memory=new MemoryStream()){WebSocketReceiveResult result;do{result=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),token);if(result.MessageType==WebSocketMessageType.Close)throw new IOException();memory.Write(buffer,0,result.Count);if(memory.Length>65536)throw new IOException();}while(!result.EndOfMessage);return Encoding.UTF8.GetString(memory.ToArray());}}
 async Task SocketLoop(){int backoff=3;while(!stop.IsCancellationRequested){try{
   string token=await owner.ChatToken();using(var ws=new ClientWebSocket()){socket=ws;ws.Options.KeepAliveInterval=TimeSpan.FromSeconds(20);var url=new Uri(owner.ChatUrl.Replace("https://","wss://")+"realtime/v1/websocket?apikey="+Uri.EscapeDataString(owner.ChatKey)+"&vsn=1.0.0");
    using(var connect=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){connect.CancelAfter(15000);await ws.ConnectAsync(url,connect.Token);}
    string topic="realtime:nyang-inbox";await Push(ws,topic,"phx_join",new{config=new{broadcast=new{ack=false,self=false},presence=new{enabled=false},postgres_changes=new[]{new{@event="INSERT",schema="public",table="nyang_chat_events",filter="recipient=eq."+owner.ChatUser}}},access_token=token},"1",stop.Token);
    var receive=Receive(ws,stop.Token);DateTime last=DateTime.UtcNow;int reference=2;
    while(!stop.IsCancellationRequested&&ws.State==WebSocketState.Open){var winner=await Task.WhenAny(receive,Task.Delay(25000,stop.Token));
     if(winner!=receive){if(DateTime.UtcNow-last>TimeSpan.FromSeconds(75))throw new IOException();string refreshed=await owner.ChatToken();if(refreshed!=token){token=refreshed;await Push(ws,topic,"access_token",new{access_token=token},(++reference).ToString(),stop.Token);}await Push(ws,"phoenix","heartbeat",new{},(++reference).ToString(),stop.Token);continue;}
     var packet=json.DeserializeObject(await receive);last=DateTime.UtcNow;string ev=ErJson.Text(packet,"event");var payload=ErJson.Get(packet,"payload");
     if(ev=="phx_error"||ev=="phx_close"||(ev=="phx_reply"&&ErJson.Text(payload,"status")=="error")||(ev=="system"&&ErJson.Text(payload,"status")=="error"))throw new IOException();
     if((ev=="phx_reply"&&ErJson.Text(packet,"ref")=="1"&&ErJson.Text(payload,"status")=="ok")||(ev=="system"&&ErJson.Text(payload,"status")=="ok")){View.connected=true;backoff=3;dirty=true;Notify();await Refresh();}
     if(ev=="postgres_changes"){dirty=true;await Refresh();}
     receive=Receive(ws,stop.Token);
    }
   }
  }catch{}finally{socket=null;View.connected=false;View.message="실시간 연결이 끊겼어요. 재연결 중…";dirty=true;Notify();}
  try{await Task.Delay(backoff*1000,stop.Token);}catch{}backoff=Math.Min(30,backoff*2);
 }}
 internal void DisconnectForTest(){if(socket!=null)socket.Abort();}
 public void Dispose(){disposed=true;stop.Cancel();if(socket!=null)socket.Abort();}
}

