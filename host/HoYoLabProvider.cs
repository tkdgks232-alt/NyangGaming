using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Only these sanitized DTOs cross the React bridge. Sessions never do.
internal sealed class GenshinRole { public string uid,server,serverName,nickname; }
internal sealed class ExpeditionNote { public string status; public int? remainingSeconds; }
internal sealed class DailyNotes {
    public int? resin,resinMax,recoverySeconds,dailyFinished,dailyTotal,expeditionCount,expeditionMax;
    public bool? rewardReceived;
    public List<ExpeditionNote> expeditions=new List<ExpeditionNote>();
    public string updatedAt;
}
internal sealed class GenshinView {
    public string source="hoyolab",state="disconnected",message="HoYoLAB 계정이 연결되지 않았어요.";
    public bool connected,busy,stale;
    public GenshinRole selected;
    public List<GenshinRole> roles=new List<GenshinRole>();
    public DailyNotes notes;
}
// Serialization allowed ONLY into DPAPI ciphertext, never into a bridge DTO or log.
internal sealed class HoyoSession { public Dictionary<string,string> cookies=new Dictionary<string,string>(); public string uid,server; }
internal sealed class HoyoFailure:Exception {
    internal readonly string Kind;
    internal HoyoFailure(string kind,string safeMessage):base(safeMessage){Kind=kind;}
}
internal sealed class HoyoVault {
    readonly string path;
    internal HoyoVault(string root){path=Path.Combine(root,"hoyolab-session.dpapi");}
    internal bool Exists {get{return File.Exists(path);}}
    internal HoyoSession Load(){
        byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
        try{return new JavaScriptSerializer().Deserialize<HoyoSession>(Encoding.UTF8.GetString(plain));}finally{Array.Clear(plain,0,plain.Length);}
    }
    internal void Save(HoyoSession session){
        byte[] plain=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(session));byte[] encrypted;
        try{encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);}finally{Array.Clear(plain,0,plain.Length);}
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path+".tmp",encrypted);
        if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
    }
    internal void Delete(){if(File.Exists(path))File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp");}
}
internal interface IGenshinDataProvider {
    Task<List<GenshinRole>> GetRoles(Dictionary<string,string> cookies,CancellationToken token);
    Task<DailyNotes> GetNotes(Dictionary<string,string> cookies,GenshinRole role,CancellationToken token);
}
internal sealed partial class HoYoLabProvider:IGenshinDataProvider {
    readonly string cacheRoot;internal HoYoLabProvider(string root=null){cacheRoot=root??ProviderHttpPolicy.DefaultRoot;}
    internal const string RolesUrl="https://api-os-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_global";
    internal const string NotesBase="https://sg-public-api.hoyolab.com/event/game_record/genshin/api/dailyNote";
    static readonly HashSet<string> AllowedCookies=new HashSet<string>{"ltoken","ltuid","ltmid","ltoken_v2","ltuid_v2","ltmid_v2"};
    internal static Dictionary<string,string> FilterCookies(IEnumerable<KeyValuePair<string,string>> cookies){
        var safe=new Dictionary<string,string>();
        foreach(var c in cookies)if(AllowedCookies.Contains(c.Key)&&!string.IsNullOrWhiteSpace(c.Value)&&c.Value.Length<12000&&!c.Value.Any(ch=>char.IsControl(ch)||ch==';'))safe[c.Key]=c.Value;
        // Do not mix legacy and v2 credentials from two logins.
        if(safe.ContainsKey("ltoken_v2")){safe.Remove("ltoken");safe.Remove("ltuid");safe.Remove("ltmid");}
        return safe;
    }
    internal static bool HasSession(Dictionary<string,string> cookies){return cookies!=null&&((cookies.ContainsKey("ltoken_v2")&&cookies.ContainsKey("ltuid_v2"))||(cookies.ContainsKey("ltoken")&&cookies.ContainsKey("ltuid")));}
    internal static string ServerName(string server){switch(server){case "os_asia":return "Asia";case "os_usa":return "America";case "os_euro":return "Europe";case "os_cht":return "TW / HK / MO";default:return null;}}
    internal static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
    internal static object Value(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)?v:null;}
    internal static string Text(Dictionary<string,object> d,string k){return Convert.ToString(Value(d,k),CultureInfo.InvariantCulture);}
    internal static int? Num(Dictionary<string,object> d,string k){int v;return int.TryParse(Text(d,k),NumberStyles.Integer,CultureInfo.InvariantCulture,out v)&&v>=0?(int?)v:null;}
    static bool? Bool(Dictionary<string,object> d,string k){var v=Value(d,k);return v is bool?(bool?)v:null;}
    internal static List<Dictionary<string,object>> Rows(object value){var rows=new List<Dictionary<string,object>>();var seq=value as IEnumerable;if(seq!=null&&!(value is string))foreach(var v in seq)if(v is Dictionary<string,object>)rows.Add((Dictionary<string,object>)v);return rows;}
    internal static HoyoFailure ApiError(int code){
        if(new[]{-100,10001,10103}.Contains(code))return new HoyoFailure("expired","HoYoLAB 로그인이 만료되었거나 유효하지 않아요. 다시 연결해 주세요.");
        if(code==10102)return new HoyoFailure("private","HoYoLAB에서 실시간 노트 사용 설정을 확인해 주세요. 앱이 공개 설정을 임의로 바꾸지는 않아요.");
        if(code==10104)return new HoyoFailure("account","이 계정으로 선택한 캐릭터의 노트를 볼 수 없어요. 계정을 다시 확인해 주세요.");
        if(new[]{10101,1028,-110}.Contains(code))return new HoyoFailure("rateLimit","HoYoLAB 요청 제한으로 대기 중이에요. 서버 대기 시간과 재시도 간격을 지켜요.");
        if(new[]{10035,5003,10041,1034}.Contains(code))return new HoyoFailure("verification","HoYoLAB 공식 화면에서 추가 인증을 완료해 주세요. 자동으로 우회하지 않아요.");
        if(code==-10002||code==1009)return new HoyoFailure("noCharacter","연결된 원신 캐릭터를 찾을 수 없어요.");
        return new HoyoFailure("service","HoYoLAB 요청 실패 (코드 "+code.ToString(CultureInfo.InvariantCulture)+"). 서비스 변경 또는 요청 오류일 수 있어요.");
    }
    async Task<Dictionary<string,object>> Request(string url,Dictionary<string,string> cookies,CancellationToken token,object payload=null){
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try{return await RequestCore(url,cookies,deadline.Token,payload);}catch(OperationCanceledException){if(token.IsCancellationRequested)throw;throw new HoyoFailure("network","HoYoLAB 응답 시간이 초과됐어요. 인터넷 연결을 확인해 주세요.");}
        }
    }
    async Task<Dictionary<string,object>> RequestCore(string url,Dictionary<string,string> cookies,CancellationToken token,object payload){
        if(!HasSession(cookies))throw ApiError(10001);
        // URL is constructed ONLY from the two provider constants and a validated role.
        using(var handler=new ProviderHttpPolicy("hoyolab",cacheRoot))using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(15)})using(var request=new HttpRequestMessage(payload==null?HttpMethod.Get:HttpMethod.Post,url)){
            if(payload!=null)request.Content=new StringContent(new JavaScriptSerializer().Serialize(payload),Encoding.UTF8,"application/json");
            request.Headers.TryAddWithoutValidation("Cookie",string.Join("; ",FilterCookies(cookies).Select(c=>c.Key+"="+c.Value)));
            request.Headers.TryAddWithoutValidation("User-Agent","KoruGamingNext/0.2");request.Headers.Referrer=new Uri("https://www.hoyolab.com/");
            request.Headers.TryAddWithoutValidation("x-rpc-app_version","1.5.0");request.Headers.TryAddWithoutValidation("x-rpc-client_type","5");request.Headers.TryAddWithoutValidation("x-rpc-language","ko-kr");request.Headers.TryAddWithoutValidation("x-rpc-lang","ko-kr");
            string t=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),r=Guid.NewGuid().ToString("N").Substring(0,6);
            // Public protocol salt from current maintained genshin.py; not a user's secret.
            using(var hash=MD5.Create())request.Headers.TryAddWithoutValidation("DS",t+","+r+","+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("salt=6s25p5ox5y14umn1p61aqyyvbvvl3lrt&t="+t+"&r="+r))).Replace("-","").ToLowerInvariant());
            try{using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token)){
                if((int)response.StatusCode==429)throw ApiError(10101);
                if(response.StatusCode==HttpStatusCode.Unauthorized)throw ApiError(10001);
                if(response.StatusCode==HttpStatusCode.Forbidden)throw new HoyoFailure("verification","HoYoLAB이 요청을 거부했어요. 공식 화면의 인증 상태를 확인해 주세요.");
                if(response.StatusCode!=HttpStatusCode.OK)throw new HoyoFailure("server","HoYoLAB 서버 응답을 확인할 수 없어요. 잠시 후 다시 시도해 주세요.");
                using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                    var buffer=new byte[4096];int read;
                    while((read=await stream.ReadAsync(buffer,0,buffer.Length,token))>0){if(memory.Length+read>1024*1024)throw new HoyoFailure("format","응답 크기가 예상 범위를 벗어났어요.");memory.Write(buffer,0,read);}
                    var body=Obj(new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray())));
                    int code;if(!int.TryParse(Text(body,"retcode"),out code))throw new HoyoFailure("format","HoYoLAB 응답 형식이 변경되었어요.");
                    if(code!=0)throw ApiError(code);
                    if(!(Value(body,"data") is Dictionary<string,object>))throw new HoyoFailure("format","HoYoLAB 데이터가 비어 있어요.");
                    return (Dictionary<string,object>)ProviderHttpPolicy.Stamp(Obj(Value(body,"data")),response);
                }
            }}catch(HoyoFailure){throw;}catch(OperationCanceledException){if(token.IsCancellationRequested)throw;throw new HoyoFailure("network","HoYoLAB 응답 시간이 초과됐어요. 인터넷 연결을 확인해 주세요.");}catch(HttpRequestException){throw new HoyoFailure("network","HoYoLAB에 연결하지 못했어요. 인터넷 연결을 확인해 주세요.");}catch(Exception){throw new HoyoFailure("format","HoYoLAB 응답을 처리하지 못했어요. 인증정보는 기록하지 않았어요.");}
        }
    }
    public async Task<List<GenshinRole>> GetRoles(Dictionary<string,string> cookies,CancellationToken token){return ParseRoles(await Request(RolesUrl,cookies,token));}
    public async Task<DailyNotes> GetNotes(Dictionary<string,string> cookies,GenshinRole role,CancellationToken token){
        if(!Regex.IsMatch(role.uid??"",@"^\d{9,10}$")||ServerName(role.server)==null)throw new HoyoFailure("account","지원하는 글로벌 원신 캐릭터를 선택해 주세요.");
        return ParseNotes(await Request(NotesBase+"?role_id="+role.uid+"&server="+Uri.EscapeDataString(role.server),cookies,token));
    }
    internal static List<GenshinRole> ParseRoles(Dictionary<string,object> data){
        if(!(Value(data,"list") is IEnumerable))throw new HoyoFailure("format","원신 계정 목록 응답을 확인하지 못했어요.");
        return Rows(Value(data,"list")).Where(r=>Text(r,"game_biz")=="hk4e_global"&&ServerName(Text(r,"region"))!=null&&Regex.IsMatch(Text(r,"game_uid"),@"^\d{9,10}$")).Select(r=>new GenshinRole{uid=Text(r,"game_uid"),server=Text(r,"region"),serverName=ServerName(Text(r,"region")),nickname=Text(r,"nickname")}).GroupBy(r=>r.uid+"/"+r.server).Select(g=>g.First()).ToList();
    }
    internal static DailyNotes ParseNotes(Dictionary<string,object> data){
        int? resin=Num(data,"current_resin"),max=Num(data,"max_resin");
        if(!resin.HasValue||!max.HasValue||max<=0||resin>max)throw new HoyoFailure("format","레진 데이터가 누락되었거나 형식이 변경됐어요.");
        var daily=Obj(Value(data,"daily_task"));
        var notes=new DailyNotes{resin=resin,resinMax=max,recoverySeconds=Num(data,"resin_recovery_time"),dailyFinished=Num(daily,"finished_num")??Num(data,"finished_task_num"),dailyTotal=Num(daily,"total_num")??Num(data,"total_task_num"),rewardReceived=Bool(daily,"is_extra_task_reward_received")??Bool(data,"is_extra_task_reward_received"),expeditionCount=Num(data,"current_expedition_num"),expeditionMax=Num(data,"max_expedition_num"),updatedAt=ProviderHttpPolicy.DataTime(data)};
        foreach(var e in Rows(Value(data,"expeditions")))notes.expeditions.Add(new ExpeditionNote{status=Text(e,"status"),remainingSeconds=Num(e,"remained_time")});
        return notes;
    }
}

internal sealed partial class GenshinService:IDisposable {
    readonly HoyoVault vault;readonly IGenshinDataProvider provider;readonly bool verify;
    HoyoSession session;CancellationTokenSource cancellation=new CancellationTokenSource();
    int revision;bool running,paused;DateTime due=DateTime.MinValue,manualAfter=DateTime.MinValue;
    internal GenshinView View=new GenshinView();
    internal event Action Changed;
    internal GenshinService(string root,bool test,IGenshinDataProvider dataProvider=null){vault=new HoyoVault(root);provider=dataProvider??new HoYoLabProvider(root);verify=test;
        if(!test&&vault.Exists)try{session=vault.Load();session.cookies=HoYoLabProvider.FilterCookies(session.cookies);if(!HoYoLabProvider.HasSession(session.cookies))throw new InvalidOperationException();View.state="restoring";View.message="저장된 HoYoLAB 연결을 확인하고 있어요.";}catch{session=null;View.state="expired";View.message="저장된 인증정보를 읽을 수 없어요. 다시 연결해 주세요.";}
    }
    void Notify(){if(Changed!=null)Changed();}
    internal async Task Accept(Dictionary<string,string> cookies,List<GenshinRole> roles){
        ResetRequests();session=new HoyoSession{cookies=HoYoLabProvider.FilterCookies(cookies)};paused=false;manualAfter=DateTime.MinValue;
        View=new GenshinView{connected=true,roles=roles,state=roles.Count==0?"noCharacter":"selectRole",message=roles.Count==0?"이 HoYoLAB 계정에 글로벌 원신 캐릭터가 없어요.":"연결할 원신 캐릭터를 선택해 주세요."};
        if(roles.Count==1){View.selected=roles[0];session.uid=roles[0].uid;session.server=roles[0].server;}
        try{vault.Save(session);}catch{session=null;View=new GenshinView{state="storage",message="인증정보를 암호화 저장하지 못했어요. 연결을 다시 시도해 주세요."};Notify();return;}
        due=DateTime.MinValue;Notify();if(View.selected!=null)await Refresh(true);
    }
    internal async Task Select(string uid,string server){
        if(session==null)return;var role=View.roles.FirstOrDefault(r=>r.uid==uid&&r.server==server);if(role==null)return;
        ResetRequests();session.uid=uid;session.server=server;View.selected=role;View.notes=null;View.stale=false;paused=false;manualAfter=DateTime.MinValue;vault.Save(session);await Refresh(true);
    }
    internal Task Tick(){return verify||paused||session==null||DateTime.UtcNow<due?Task.FromResult(0):Refresh(false);}
    internal async Task Refresh(bool manual){
        if(session==null||running||paused)return;
        if(manual&&DateTime.UtcNow<manualAfter){View.message="요청 간격을 보호하고 있어요. 잠시 후 다시 시도해 주세요.";Notify();return;}
        running=true;int version=revision;var token=cancellation.Token;var active=session;
        manualAfter=DateTime.UtcNow.AddSeconds(30);due=DateTime.UtcNow.AddMinutes(15);View.busy=true;Notify();
        try{
            var roles=await provider.GetRoles(active.cookies,token);if(version!=revision)return;
            View.roles=roles;View.connected=true;
            var role=roles.FirstOrDefault(r=>r.uid==active.uid&&r.server==active.server);
            if(role==null&&roles.Count==1)role=roles[0];
            if(View.selected==null||role==null||View.selected.uid!=role.uid||View.selected.server!=role.server){View.notes=null;View.stale=false;ClearCharacters();}
            View.selected=role;
            if(role==null){View.notes=null;View.state=roles.Count==0?"noCharacter":"selectRole";View.message=roles.Count==0?"이 계정에 글로벌 원신 캐릭터가 없어요.":"연결할 원신 캐릭터를 선택해 주세요.";return;}
            active.uid=role.uid;active.server=role.server;vault.Save(active);
            var notes=await provider.GetNotes(active.cookies,role,token);if(version!=revision)return;
            View.notes=notes;View.stale=false;View.state="connected";View.message="실시간 노트를 불러왔어요. 5분 간격으로 갱신해요.";
        }catch(OperationCanceledException){}catch(Exception ex){if(version!=revision)return;var failure=ex as HoyoFailure;
            View.state=failure==null?"storage":failure.Kind;View.message=failure==null?"데이터 또는 인증정보를 처리하지 못했어요. 다른 기능은 계속 사용할 수 있어요.":failure.Message;View.stale=View.notes!=null;
            if(View.state=="expired"||View.state=="verification"||View.state=="private"){View.connected=false;paused=true;}
            if(View.state=="rateLimit"){due=DateTime.UtcNow.AddMinutes(15);manualAfter=due;}
        }finally{if(version==revision){running=false;View.busy=false;Notify();}}
    }
    void ResetRequests(){revision++;cancellation.Cancel();cancellation.Dispose();cancellation=new CancellationTokenSource();running=false;ClearCharacters();}
    internal void Disconnect(){ResetRequests();session=null;paused=true;View=new GenshinView();try{vault.Delete();}catch{View.state="storage";View.message="연동은 중지했지만 암호화 인증 파일 삭제에 실패했어요. 파일 권한을 확인해 주세요.";}Notify();}
    internal void SetFixture(GenshinView fixture){if(verify){View=fixture;Notify();}}
    public void Dispose(){ResetRequests();cancellation.Dispose();session=null;}
}
