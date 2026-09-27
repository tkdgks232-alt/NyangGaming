using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class FriendsOptions {
    public bool shareGame=true,appearOffline=false,autoAway=true,shareHistory=false;
    public int awayMinutes=10;
}
internal sealed class FriendsConfig {public string url,key,nickname;}
internal sealed class FriendsSession {public string access_token,refresh_token;public long expires_at;}
internal sealed class FriendsView {
    public bool configured,connected,busy,stale;
    public string message="친구 서비스 초기 설정이 필요해요.",localStatus="offline",gameId,gameStartedAt,lastUpdated;
    public FriendsOptions options=new FriendsOptions();
    public object me;
    public object[] friends=new object[0],requests=new object[0];
    public object[] messages=new object[0];public string chatUser,sentId;
    public object history;public string historyUser,historyMessage="플레이 기록은 이 버전부터 쌓입니다.";public bool historyPending;
}
// Strict outbound DTO: no GameState, process list, input timing or window details.
internal sealed class PresencePayload {
    public string p_status,p_game_id,p_game_started_at;
}
internal static class FriendPresence {
    static readonly HashSet<string> Supported=new HashSet<string>{"genshin","eternal","league","valorant"};
    [StructLayout(LayoutKind.Sequential)] struct LastInput {public uint cbSize,dwTime;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput value);
    internal static double IdleSeconds(){var value=new LastInput{cbSize=(uint)Marshal.SizeOf(typeof(LastInput))};return GetLastInputInfo(ref value)?unchecked((uint)Environment.TickCount-value.dwTime)/1000.0:0;}
    internal static PresencePayload Decide(FriendsOptions options,string game,string started,double idle){
        if(options.appearOffline)return new PresencePayload{p_status="offline"};
        if(options.shareGame&&game!=null&&Supported.Contains(game))return new PresencePayload{p_status="gaming",p_game_id=game,p_game_started_at=started};
        return new PresencePayload{p_status=options.autoAway&&idle>=options.awayMinutes*60?"away":"online"};
    }
}
internal sealed class FriendsService:IDisposable {
    internal readonly MessengerService Chat;
    readonly System.Threading.SemaphoreSlim authLock=new System.Threading.SemaphoreSlim(1,1);
    internal string ChatUser {get{return ErJson.Text(View.me,"id");}}
    internal string ChatUrl {get{return config==null?"":config.url;}}
    internal string ChatKey {get{return config==null?"":config.key;}}
    internal async Task<string> ChatToken(){if(config==null||string.IsNullOrEmpty(config.nickname))throw new InvalidOperationException();await Authenticate();return session.access_token;}
    internal async Task<object> ChatPost(string path,object data){await ChatToken();return await Post(path,data,true);}
    internal Task<object> ChatRpc(string method,object data){return ChatPost("rest/v1/rpc/"+method,data);}
    internal async Task ChatUpload(string path,byte[] bytes,string bucket="nyang-avatars"){if(bucket!="nyang-avatars"&&bucket!="nyang-chat-images")throw new ArgumentException();await ChatToken();using(var request=new HttpRequestMessage(HttpMethod.Post,config.url+"storage/v1/object/"+bucket+"/"+path)){request.Headers.Add("apikey",config.key);request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",session.access_token);request.Content=new ByteArrayContent(bytes);request.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");using(var response=await http.SendAsync(request)){if(!response.IsSuccessStatusCode&&bucket=="nyang-chat-images"){var error=json.DeserializeObject(await response.Content.ReadAsStringAsync());if(ErJson.Text(error,"error")=="Duplicate"||ErJson.Text(error,"code")=="Duplicate")return;}response.EnsureSuccessStatusCode();}}}
    internal readonly FriendsView View=new FriendsView();internal event Action Changed;
    readonly string root;readonly bool verify;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=1024*1024};
    readonly HttpClient http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=1024*1024};
    FriendsConfig config;FriendsSession session;bool busy,disposed;DateTime next=DateTime.MinValue,retry=DateTime.MinValue;
    string lastSent,activeGame,started;int revision;bool pageVisible;DateTime chatDue=DateTime.MinValue;
    string lastGames;bool historyPrivacyDirty=true,historyDirty;DateTime historyRetry=DateTime.MinValue,historyDue=DateTime.MinValue;int historyVersion;
    int historyYear=DateTime.UtcNow.AddHours(9).Year;string historyPeriod="30",historyGame,historyDay;
    internal static string[] SessionGames(GameState[] games){return games.Where(g=>g.running&&(g.id=="genshin"||g.id=="eternal"||g.id=="league"||g.id=="valorant")).Select(g=>g.id).Distinct().OrderBy(x=>x).ToArray();}
    internal void HistoryPrivacy(bool share){View.options.shareHistory=share;if(!verify)Save("friends-options.json",View.options);historyPrivacyDirty=true;historyRetry=DateTime.MinValue;View.historyPending=true;View.historyMessage="기록 공유 설정을 서버에 반영 중이에요.";Notify();}
    internal async Task History(string user,int year,string period,string game,string day){
        if(verify)return;Guid id;if(!Guid.TryParse(user,out id)||year<2000||year>2100||!new[]{"today","7","30","month","year","all"}.Contains(period))return;
        if(!string.IsNullOrEmpty(game)&&!new[]{"genshin","eternal","league","valorant"}.Contains(game))return;
        DateTime date;if(!string.IsNullOrEmpty(day)&&!DateTime.TryParseExact(day,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out date))return;
        View.historyUser=user;historyYear=year;historyPeriod=period;historyGame=string.IsNullOrEmpty(game)?null:game;historyDay=string.IsNullOrEmpty(day)?null:day;historyVersion++;historyDirty=true;View.history=null;View.historyMessage="플레이 기록을 불러오고 있어요.";Notify();
        if(config!=null)await Run(async()=>{await Authenticate();await LoadHistory();});
    }
    internal void CloseHistory(){View.historyUser=null;View.history=null;historyDirty=false;historyVersion++;Notify();}
    async Task LoadHistory(){
        if(View.historyUser==null)return;int version=historyVersion;
        try{var result=await Rpc("nyang_history",new{p_user=View.historyUser,p_year=historyYear,p_period=historyPeriod,p_game=historyGame,p_day=historyDay});if(version!=historyVersion)return;View.history=result;View.historyMessage=object.Equals(ErJson.Get(result,"allowed"),true)?"완료된 게임 세션 기준 · 한국 시간(KST) · 동시 실행 게임은 각각 합산해요.":ErJson.Text(result,"message");historyDirty=false;historyDue=DateTime.UtcNow.AddSeconds(30);}
        catch{if(version==historyVersion){View.history=null;View.historyMessage="기록을 조회하지 못했어요. 연결 상태 또는 서버 기록 기능 설정을 확인해 주세요.";historyDirty=false;historyDue=DateTime.UtcNow.AddSeconds(30);}}
    }
    async Task RecordSessions(string[] games){
        if(DateTime.UtcNow<historyRetry)return;
        try{if(historyPrivacyDirty){bool share=View.options.shareHistory;await Rpc("nyang_history_privacy",new{p_share=share});historyPrivacyDirty=share!=View.options.shareHistory;View.historyPending=historyPrivacyDirty;}
            await Rpc("nyang_session_pulse",new{p_games=games});lastGames=string.Join(",",games);historyRetry=DateTime.MinValue;
            if(pageVisible&&View.historyUser!=null&&(historyDirty||DateTime.UtcNow>=historyDue))await LoadHistory();
        }catch{historyRetry=DateTime.UtcNow.AddSeconds(30);View.historyMessage="기록 서버 연결 대기 중이에요. 기록 공유 변경은 서버 연결 후 반영됩니다.";}
    }
    internal void PageVisible(bool value){pageVisible=value;}
    internal async Task SelectChat(string id){Guid parsed;if(!Guid.TryParse(id,out parsed)||!View.friends.Any(f=>ErJson.Text(f,"id")==id))return;View.chatUser=id;View.messages=new object[0];Notify();await Action("refresh",null,false);}
    internal async Task SendMessage(string user,string body,string messageId){
        Guid id,nonce;if(verify||config==null||!Guid.TryParse(user,out id)||!Guid.TryParse(messageId,out nonce)||string.IsNullOrWhiteSpace(body)||body.Length>1000)return;
        await Run(async()=>{await Authenticate();await Rpc("nyang_send",new{p_user=id.ToString(),p_body=body,p_id=nonce.ToString()});View.sentId=messageId;await LoadChat();View.message="메시지를 보냈어요.";});
    }
    async Task LoadChat(){if(View.chatUser==null)return;string selected=View.chatUser;var result=await Rpc("nyang_messages",new{p_user=selected});if(selected==View.chatUser)View.messages=ErJson.Rows(result).Cast<object>().ToArray();chatDue=DateTime.UtcNow.AddSeconds(5);}
    internal FriendsService(string folder,bool test){root=folder;verify=test;Chat=new MessengerService(this,folder,test);if(test)return;
        try{string saved=Path.Combine(root,"friends-config.json");config=json.Deserialize<FriendsConfig>(File.ReadAllText(File.Exists(saved)?saved:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"friends-backend.json")));if(!ValidConfig(config.url,config.key))config=null;}catch{}
        try{View.options=json.Deserialize<FriendsOptions>(File.ReadAllText(Path.Combine(root,"friends-options.json")))??new FriendsOptions();View.options.awayMinutes=Math.Max(1,Math.Min(240,View.options.awayMinutes));}catch{}
        try{session=json.Deserialize<FriendsSession>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(root,"friends-session.dpapi")),null,DataProtectionScope.CurrentUser)));}catch{}
        View.configured=config!=null;View.message=View.configured?"친구 서비스 연결을 준비하고 있어요.":View.message;
    }
    internal static bool ValidConfig(string url,string key){Uri u;return Uri.TryCreate(url,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Port==443&&u.Host.EndsWith(".supabase.co",StringComparison.OrdinalIgnoreCase)&&u.Host.Split('.').Length==3&&u.UserInfo==""&&u.AbsolutePath=="/"&&u.Query==""&&u.Fragment==""&&key!=null&&key.StartsWith("sb_publishable_")&&key.Length<512&&!key.Any(char.IsWhiteSpace);}
    internal Task Profile(string nickname){if(config==null){View.message="먼저 연결 설정에서 Supabase 프로젝트를 등록해 주세요.";Notify();return Task.FromResult(0);}return Configure(config.url,config.key,nickname);}
    internal void Setup(System.Windows.Forms.IWin32Window owner){
        if(busy)return;
        using(var dialog=new System.Windows.Forms.Form{Text="친구 서비스 연결 설정",Width=560,Height=270,StartPosition=System.Windows.Forms.FormStartPosition.CenterParent}){
            var panel=new System.Windows.Forms.FlowLayoutPanel{Dock=System.Windows.Forms.DockStyle.Fill,FlowDirection=System.Windows.Forms.FlowDirection.TopDown,Padding=new System.Windows.Forms.Padding(16),WrapContents=false};
            var url=new System.Windows.Forms.TextBox{Width=490,Text=config==null?"":config.url};var key=new System.Windows.Forms.TextBox{Width=490,Text=config==null?"":config.key};
            panel.Controls.Add(new System.Windows.Forms.Label{Text="Supabase 프로젝트 URL",AutoSize=true});panel.Controls.Add(url);panel.Controls.Add(new System.Windows.Forms.Label{Text="공개용 publishable 키 (secret / service_role 키 사용 금지)",AutoSize=true});panel.Controls.Add(key);
            var save=new System.Windows.Forms.Button{Text="연결 정보 저장",AutoSize=true};save.Click+=delegate{string u=url.Text.Trim().TrimEnd('/')+"/";if(!ValidConfig(u,key.Text.Trim())){System.Windows.Forms.MessageBox.Show(dialog,"프로젝트 URL과 sb_publishable_ 키를 확인해 주세요.");return;}if(config!=null&&config.url!=u&&session!=null){System.Windows.Forms.MessageBox.Show(dialog,"이미 연결된 계정의 서버는 여기서 변경할 수 없어요.");return;}config=new FriendsConfig{url=u,key=key.Text.Trim(),nickname=config==null?null:config.nickname};Save("friends-config.json",config);View.configured=true;View.message="연결 정보를 저장했어요. 닉네임을 입력하고 친구 기능을 시작하세요.";Notify();dialog.Close();};panel.Controls.Add(save);dialog.Controls.Add(panel);dialog.ShowDialog(owner);
        }
    }
    void Notify(){if(!disposed&&Changed!=null)Changed();}
    void Save(string name,object value){LeagueJson.AtomicBytes(Path.Combine(root,name),Encoding.UTF8.GetBytes(json.Serialize(value)));}
    void SaveSession(){LeagueJson.AtomicBytes(Path.Combine(root,"friends-session.dpapi"),ProtectedData.Protect(Encoding.UTF8.GetBytes(json.Serialize(session)),null,DataProtectionScope.CurrentUser));}
    internal async Task Configure(string url,string key,string nickname){
        if(busy||verify)return;url=(url??"").Trim().TrimEnd('/')+"/";nickname=(nickname??"").Trim();
        if(!ValidConfig(url,key)||nickname.Length<1||nickname.Length>24||nickname.Any(char.IsControl)){View.message="Supabase 프로젝트 URL, publishable 키, 1~24자 닉네임을 확인해 주세요.";Notify();return;}
        if(config!=null&&!string.Equals(config.url,url,StringComparison.OrdinalIgnoreCase)){View.message="다른 서버로 변경하면 친구 계정이 분리됩니다. 현재 프로젝트 URL을 사용해 주세요.";Notify();return;}
        config=new FriendsConfig{url=url,key=key,nickname=nickname};Save("friends-config.json",config);View.configured=true;
        await Run(async()=>{await Authenticate();View.me=await Rpc("nyang_profile",new{p_nickname=nickname});await Snapshot();View.connected=true;View.message="친구 코드를 공유해서 서로 친구 요청을 보내세요.";next=DateTime.MinValue;});
    }
    internal void Options(bool share,bool offline,bool away,int minutes){
        View.options=new FriendsOptions{shareGame=share,appearOffline=offline,autoAway=away,awayMinutes=Math.Max(1,Math.Min(240,minutes)),shareHistory=View.options.shareHistory};
        if(!verify)Save("friends-options.json",View.options);revision++;lastSent=null;next=DateTime.MinValue;retry=DateTime.MinValue;
        View.message="설정을 저장했어요. 연결되면 변경된 공개 상태를 즉시 반영합니다.";Notify();
    }
    internal async Task Action(string action,string value,bool accept){
        if(verify||config==null)return;
        await Run(async()=>{await Authenticate();switch(action){
            case "request":await Rpc("nyang_request",new{p_code=(value??"").Trim().ToUpperInvariant()});break;
            case "respond":Guid id;if(!Guid.TryParse(value,out id))return;await Rpc("nyang_respond",new{p_user=id.ToString(),p_accept=accept});break;
            case "remove":Guid remove;if(!Guid.TryParse(value,out remove))return;await Rpc("nyang_remove",new{p_user=remove.ToString()});break;
        }await Snapshot();View.message="친구 목록을 갱신했어요.";});
    }
    internal async Task Tick(GameState[] games,bool foreground=true){
        // Only use already classified supported game IDs. Never inspect other PC activity.
        var game=games.FirstOrDefault(g=>g.running&&(g.id=="genshin"||g.id=="eternal"||g.id=="league"||g.id=="valorant"));
        string current=game==null?null:game.id;
        if(current!=activeGame){activeGame=current;started=current==null?null:DateTime.UtcNow.ToString("o");}
        var presence=FriendPresence.Decide(View.options,current,started,View.options.autoAway?FriendPresence.IdleSeconds():0);
        View.localStatus=presence.p_status;View.gameId=presence.p_game_id;View.gameStartedAt=presence.p_game_started_at;
        if(verify||config==null||string.IsNullOrWhiteSpace(config.nickname)||busy||disposed||DateTime.UtcNow<retry)return;
        var sessionGames=SessionGames(games);string state=json.Serialize(presence);
        bool historyChanged=DateTime.UtcNow>=historyRetry&&(string.Join(",",sessionGames)!=lastGames||historyPrivacyDirty);
        if(state==lastSent&&!historyChanged&&DateTime.UtcNow<next){if(foreground&&pageVisible&&historyDirty)await Run(async()=>{await Authenticate();await LoadHistory();});else if(foreground&&pageVisible&&View.chatUser!=null&&DateTime.UtcNow>=chatDue)await Run(async()=>{await Authenticate();await LoadChat();});return;}
        int sentRevision=revision;
        await Run(async()=>{await Authenticate();if(View.me==null)View.me=await Rpc("nyang_profile",new{p_nickname=config.nickname});await Rpc("nyang_heartbeat",presence);lastSent=sentRevision==revision?state:null;next=DateTime.UtcNow.AddSeconds(30);await RecordSessions(sessionGames);await Snapshot();View.connected=true;View.message="친구 상태를 공유하고 있어요.";});
    }
    async Task Run(Func<Task> task){if(busy||disposed)return;busy=true;View.busy=true;Notify();try{await task();retry=DateTime.MinValue;View.stale=false;}catch{View.stale=true;View.connected=false;retry=DateTime.UtcNow.AddSeconds(30);View.message="친구 서버에 연결하지 못했어요. 인터넷 연결과 서버 초기 설정을 확인해 주세요. 다른 친구에게는 신호가 끊긴 뒤 2분 이내에 오프라인으로 표시됩니다.";}finally{busy=false;View.busy=false;Notify();}}
    async Task Authenticate(){await authLock.WaitAsync();try{await AuthenticateCore();}finally{authLock.Release();}}
    async Task AuthenticateCore(){
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();if(session!=null&&session.expires_at>now+90)return;
        object result;
        if(session!=null&&!string.IsNullOrEmpty(session.refresh_token))result=await Post("auth/v1/token?grant_type=refresh_token",new{refresh_token=session.refresh_token},false);
        else result=await Post("auth/v1/signup",new{},false);
        string token=ErJson.Text(result,"access_token"),refresh=ErJson.Text(result,"refresh_token");if(token.Length<10||refresh.Length<5)throw new InvalidOperationException();
        session=new FriendsSession{access_token=token,refresh_token=refresh,expires_at=now+ErJson.Long(result,"expires_in")};SaveSession();
    }
    async Task<object> Post(string path,object data,bool auth){
        using(var request=new HttpRequestMessage(HttpMethod.Post,config.url+path)){
            request.Headers.Add("apikey",config.key);if(auth)request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",session.access_token);
            request.Content=new StringContent(json.Serialize(data),Encoding.UTF8,"application/json");
            using(var response=await http.SendAsync(request)){if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Friends request failed");string text=await response.Content.ReadAsStringAsync();return string.IsNullOrWhiteSpace(text)?null:json.DeserializeObject(text);}
        }
    }
    Task<object> Rpc(string method,object data){return Post("rest/v1/rpc/"+method,data,true);}
    async Task Snapshot(){var result=await Rpc("nyang_snapshot",new{});View.me=ErJson.Get(result,"me");View.friends=ErJson.Rows(ErJson.Get(result,"friends")).Cast<object>().ToArray();View.requests=ErJson.Rows(ErJson.Get(result,"requests")).Cast<object>().ToArray();View.lastUpdated=DateTime.UtcNow.ToString("o");if(View.chatUser!=null&&!View.friends.Any(f=>ErJson.Text(f,"id")==View.chatUser)){View.chatUser=null;View.messages=new object[0];}if(pageVisible)await LoadChat();}
    internal async Task Offline(){if(verify||config==null||session==null||disposed)return;retry=DateTime.MaxValue;for(int i=0;i<20&&busy;i++)await Task.Delay(100);if(busy)return;try{await Task.WhenAny(Task.WhenAll(Rpc("nyang_heartbeat",new PresencePayload{p_status="offline"}),Rpc("nyang_session_pulse",new{p_games=new string[0]})),Task.Delay(2000));}catch{}}
    public void Dispose(){disposed=true;Chat.Dispose();http.Dispose();}
}
