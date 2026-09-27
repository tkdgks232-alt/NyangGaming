using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;

// Official API 2026-07-24: UID routes, NOT retired userNum routes.
// Only explicit DTOs cross the bridge. Neither the key nor raw responses are serialized.
internal sealed class ErMatch {
    public long id; public int? season,mode,rank,kills,assists,seconds,level,damage,received,healing,teamKills;
    public string character,startedAt; public int? delta;
}
internal sealed class ErSeasonCharacter {public string name;public int games,wins;public double? averageRank;}
internal sealed class ErData {
    public double? seasonAverageRank,seasonAverageKills,seasonAverageAssists;public List<ErSeasonCharacter> seasonCharacters=new List<ErSeasonCharacter>();
    public string nickname,seasonName,updatedAt,rankMessage,metadataMessage;
    public string source="api",sourceUrl,sourceUpdatedAt;
    public int? seasonId,rp,ranking,seasonGames,seasonWins;
    public string tier; // No guessed thresholds: official userStats currently has no tier name.
    public List<ErMatch> matches=new List<ErMatch>();
}
internal sealed class ErView {
    public bool connected,hasKey,busy,stale;
    public string source="ercraft";
    public string state="disconnected",message="닉네임으로 ERCraft 공개 전적을 연결해 주세요. API 키는 필요 없어요.",nextRefreshAt;
    public ErData data,preview;
}
internal sealed class ErStored { public string nickname,source; public ErData data; }
internal sealed class ErFailure:Exception {
    internal readonly string State; internal readonly int WaitSeconds;
    internal ErFailure(string state,string message,int wait=0):base(message){State=state;WaitSeconds=wait;}
}
internal static class ErJson {
    internal static Dictionary<string,object> Obj(object o){return o as Dictionary<string,object>??new Dictionary<string,object>();}
    internal static object Get(object o,string k){object v;return Obj(o).TryGetValue(k,out v)?v:null;}
    internal static string Text(object o,string k){return Convert.ToString(Get(o,k),CultureInfo.InvariantCulture);}
    internal static int? Int(object o,string k){int n;return int.TryParse(Text(o,k),NumberStyles.Integer,CultureInfo.InvariantCulture,out n)?(int?)n:null;}
    internal static long Long(object o,string k){long n;return long.TryParse(Text(o,k),out n)?n:0;}
    internal static IEnumerable<Dictionary<string,object>> Rows(object o){var list=o as IEnumerable;if(list==null||o is string||o is IDictionary)yield break;foreach(var r in list)if(r is Dictionary<string,object>)yield return (Dictionary<string,object>)r;}
}
internal interface IEternalReturnDataProvider:IDisposable {
    Task<ErData> Read(string key,string nickname,CancellationToken cancel);
}
internal sealed class EternalReturnApiProvider:IEternalReturnDataProvider {
    readonly HttpClient http;
    internal EternalReturnApiProvider(HttpMessageHandler handler=null,string root=null){http=new HttpClient(new ProviderHttpPolicy("bser",root,handler)){Timeout=TimeSpan.FromSeconds(18)};}
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
    DateTime requestAt=DateTime.MinValue,metaUntil=DateTime.MinValue;
    List<Dictionary<string,object>> seasons=new List<Dictionary<string,object>>();
    Dictionary<int,string> characters=new Dictionary<int,string>();
    async Task<Dictionary<string,object>> Request(string path,string key,CancellationToken caller){
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(caller)){
        timeout.CancelAfter(TimeSpan.FromSeconds(20));var cancel=timeout.Token;
        var delay=(requestAt-DateTime.UtcNow).TotalMilliseconds;if(delay>0)await Task.Delay((int)delay,cancel);
        requestAt=DateTime.UtcNow.AddMilliseconds(1100); // serial <= 1 request/sec, no burst retries
        using(var request=new HttpRequestMessage(HttpMethod.Get,"https://open-api.bser.io"+path)){
            request.Headers.Add("x-api-key",key);
            try{using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel)){
                int status=(int)response.StatusCode;
                if(status!=200)throw Failure(status,response.Headers.RetryAfter==null?0:(int)Math.Min(int.MaxValue,Math.Max(60,response.Headers.RetryAfter.Delta.HasValue?response.Headers.RetryAfter.Delta.Value.TotalSeconds:900)));
                var text=await Limited(response.Content,8*1024*1024,cancel);
                Dictionary<string,object> obj;try{obj=json.Deserialize<Dictionary<string,object>>(text);}catch{throw new ErFailure("format","공식 API 응답 형식을 확인할 수 없어요.");}
                int? code=ErJson.Int(obj,"code");if(code!=200)throw Failure(code??500,0);
                return (Dictionary<string,object>)ProviderHttpPolicy.Stamp(obj,response);
            }}catch(OperationCanceledException){if(caller.IsCancellationRequested)throw;throw new ErFailure("network","API 응답 시간이 초과됐어요. 마지막 정상 기록은 유지해요.");}
            catch(HttpRequestException){throw new ErFailure("network","인터넷 또는 공식 API에 연결할 수 없어요.");}
        }
        }
    }
    internal static ErFailure Failure(int code,int retry){
        if(code==401)return new ErFailure("key","API 키를 확인해 주세요. 자동 갱신을 잠시 중지했어요.",900);
        if(code==403)return new ErFailure("forbidden","API 접근이 거부됐어요. 키 권한 또는 호출 제한을 확인해 주세요.",Math.Max(900,retry));
        if(code==429)return new ErFailure("rateLimit","호출 제한으로 갱신을 쉬고 있어요. 잠시 후 자동으로 다시 확인해요.",Math.Max(60,retry==0?900:retry));
        if(code==404)return new ErFailure("notFound","해당 계정 또는 기록을 찾을 수 없어요. 닉네임을 확인해 주세요.");
        return new ErFailure("service","공식 API가 요청을 처리하지 못했어요. 마지막 정상 기록은 유지해요.",120);
    }
    static async Task<string> Limited(HttpContent content,int max,CancellationToken token){
        if(content.Headers.ContentLength>max)throw new ErFailure("format","API 응답 크기가 허용 범위를 넘었어요.");
        using(var input=await content.ReadAsStreamAsync())using(var output=new MemoryStream()){
            var buffer=new byte[8192];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,token))>0){if(output.Length+count>max)throw new ErFailure("format","API 응답 크기가 허용 범위를 넘었어요.");output.Write(buffer,0,count);}return Encoding.UTF8.GetString(output.ToArray());
        }
    }
    async Task Metadata(string key,CancellationToken cancel){
        if(DateTime.UtcNow<metaUntil)return;
        var s=await Request("/v2/data/Season",key,cancel);seasons=ErJson.Rows(ErJson.Get(s,"data")).ToList();
        var c=await Request("/v2/data/Character",key,cancel);var names=new Dictionary<int,string>();
        foreach(var r in ErJson.Rows(ErJson.Get(c,"data"))){var id=ErJson.Int(r,"code");var name=ErJson.Text(r,"name");if(id.HasValue&&!string.IsNullOrWhiteSpace(name))names[id.Value]=name;}
        characters=names;metaUntil=DateTime.UtcNow.AddHours(12);
    }
    internal static Dictionary<string,object> CurrentSeason(IEnumerable<Dictionary<string,object>> rows,DateTime now){
        // Never choose max seasonID: data can contain future seasons.
        var active=rows.Where(r=>ErJson.Text(r,"isCurrent")=="1"||string.Equals(ErJson.Text(r,"isCurrent"),"true",StringComparison.OrdinalIgnoreCase)).ToList();
        if(active.Count==1)return active[0];
        return null; // Ambiguous metadata must be shown as unavailable, not inferred from latest match.
    }
    internal static ErMatch ParseMatch(Dictionary<string,object> r,Dictionary<int,string> names){
        int? ch=ErJson.Int(r,"characterNum"),mode=ErJson.Int(r,"matchingMode");string name;
        if(!ch.HasValue||!names.TryGetValue(ch.Value,out name))name=ch.HasValue?"캐릭터 #"+ch.Value:"캐릭터 정보 없음";
        // mmrGain is documented. Missing restricted mmrBefore/mmrAfter never become zero.
        return new ErMatch{id=ErJson.Long(r,"gameId"),season=ErJson.Int(r,"seasonId"),mode=mode,rank=ErJson.Int(r,"gameRank"),kills=ErJson.Int(r,"playerKill"),assists=ErJson.Int(r,"playerAssistant"),seconds=ErJson.Int(r,"playTime"),character=name,startedAt=ErJson.Text(r,"startDtm"),delta=mode==3?ErJson.Int(r,"mmrGain"):null,level=ErJson.Int(r,"characterLevel"),damage=ErJson.Int(r,"damageToPlayer"),received=ErJson.Int(r,"damageFromPlayer"),healing=ErJson.Int(r,"healAmount"),teamKills=ErJson.Int(r,"teamKill")};
    }
    public async Task<ErData> Read(string key,string nickname,CancellationToken cancel){
        var user=ErJson.Get(await Request("/v1/user/nickname?query="+Uri.EscapeDataString(nickname),key,cancel),"user");
        string uid=ErJson.Text(user,"uid"),found=ErJson.Text(user,"nickname");
        if(string.IsNullOrWhiteSpace(uid)||string.IsNullOrWhiteSpace(found))throw new ErFailure("notFound","닉네임으로 계정을 찾을 수 없어요.");
        var result=new ErData{nickname=found,tier=null};
        try{await Metadata(key,cancel);}catch(ErFailure ex){if(ex.State=="key"||ex.State=="forbidden"||ex.State=="rateLimit")throw;result.metadataMessage="시즌/캐릭터 메타데이터를 확인하지 못했어요.";}
        var season=CurrentSeason(seasons,DateTime.UtcNow);
        result.seasonId=ErJson.Int(season,"seasonID")??ErJson.Int(season,"seasonId");
        result.seasonName=ErJson.Text(season,"seasonName");
        if(result.seasonId.HasValue){
            if(string.IsNullOrWhiteSpace(result.seasonName))result.seasonName="시즌 ID "+result.seasonId;
            try{
                var stats=await Request("/v2/user/stats/uid/"+Uri.EscapeDataString(uid)+"/"+result.seasonId+"/3",key,cancel);
                if(ErJson.Get(stats,"userStats")==null)throw new ErFailure("format","랭크 통계의 응답 형식이 바뀌었어요. 이전 정상 기록을 유지해요.");
                var row=ErJson.Rows(ErJson.Get(stats,"userStats")).FirstOrDefault(r=>ErJson.Int(r,"matchingTeamMode")==3&&ErJson.Int(r,"matchingMode")==3&&ErJson.Int(r,"seasonId")==result.seasonId);
                if(row!=null){result.rp=ErJson.Int(row,"mmr");result.ranking=ErJson.Int(row,"rank");result.seasonGames=ErJson.Int(row,"totalGames");result.seasonWins=ErJson.Int(row,"totalWins");}
                result.rankMessage=row==null?"현재 시즌 랭크 기록이 없어요.":"공식 랭크 통계의 mmr 필드는 현재 RP예요. 티어명은 별도 제공되지 않아요.";
            }catch(ErFailure ex){if(ex.State!="notFound")throw;result.rankMessage="현재 시즌 랭크 기록이 없어요.";}
        }else result.rankMessage="공식 메타데이터에서 현재 시즌을 확정하지 못했어요. RP는 표시하지 않아요.";
        Dictionary<string,object> games;
        try{games=await Request("/v1/user/games/uid/"+Uri.EscapeDataString(uid),key,cancel);}catch(ErFailure ex){if(ex.State!="notFound")throw;games=new Dictionary<string,object>{{"userGames",new object[0]}};}
        if(ErJson.Get(games,"userGames")==null)throw new ErFailure("format","최근 경기 응답 형식이 바뀌었어요. 이전 정상 기록을 유지해요.");
        result.matches=ErJson.Rows(ErJson.Get(games,"userGames")).Select(r=>ParseMatch(r,characters)).Where(m=>m.id>0).GroupBy(m=>m.id).Select(g=>g.First()).OrderByDescending(m=>m.id).Take(20).ToList();
        result.updatedAt=ProviderHttpPolicy.DataTime(games);return result;
    }
    public void Dispose(){http.Dispose();}
}
internal sealed class EternalReturnService:IDisposable {
    internal Func<DateTime> Now=()=>DateTime.UtcNow;
    internal ErView View=new ErView();internal event Action Changed;
    readonly string keyPath,accountPath,sourcePath;readonly bool verify;readonly IEternalReturnDataProvider provider,website,hippy;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2*1024*1024};
    string key,nickname;CancellationTokenSource cancel=new CancellationTokenSource();int revision;
    DateTime due=DateTime.MinValue,cooldown=DateTime.MinValue,exitDue=DateTime.MaxValue;int exitAttempt;
    long exitMatch;bool disposed,siteBlocked;
    int RefreshMinutes {get{return 15;}}
    internal EternalReturnService(string root,bool test,IEternalReturnDataProvider injected=null,IEternalReturnDataProvider siteInjected=null){
        verify=test;provider=injected??new EternalReturnApiProvider(null,root);website=siteInjected??new EternalCraftProvider(root);hippy=new EternalHippyProvider(root);sourcePath=Path.Combine(root,"eternal-source.txt");keyPath=Path.Combine(root,"eternal-key.dpapi");accountPath=Path.Combine(root,"eternal-account.json");
        if(verify)return;
        try{if(File.Exists(keyPath))key=Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(keyPath),null,DataProtectionScope.CurrentUser));}catch{View.message="저장된 API 키를 읽을 수 없어요. 다시 등록해 주세요.";}
        View.hasKey=!string.IsNullOrWhiteSpace(key);View.source=View.hasKey?"api":"ercraft";try{string selected=File.ReadAllText(sourcePath);if(selected=="api"||selected=="ercraft"||selected=="hippy")View.source=selected;}catch{}
        try{var saved=json.Deserialize<ErStored>(File.ReadAllText(accountPath));if(saved!=null&&!string.IsNullOrWhiteSpace(saved.nickname)){nickname=saved.nickname;View.source=saved.source=="hippy"?"hippy":saved.source=="ercraft"||saved.source=="dak"?"ercraft":"api";if(saved.source=="dak")saved.data=null;View.connected=true;View.data=saved.data;if(View.source=="ercraft")EternalCraftProvider.ResolveCharacters(View.data);View.stale=saved.data!=null;View.state="restoring";View.message="저장된 계정의 최신 기록을 확인하고 있어요.";}}catch{}
    }
    void Notify(){if(!disposed&&Changed!=null)Changed();}
    internal static bool ValidKey(string value){return !string.IsNullOrWhiteSpace(value)&&value.Length<=2048&&!value.Any(char.IsControl)&&!value.Any(char.IsWhiteSpace);}
    internal void SetKey(string value){
        if(!ValidKey(value))throw new InvalidOperationException();
        var encrypted=ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser);Atomic(keyPath,encrypted);
        Reset();key=value;View.hasKey=true;View.source="api";Atomic(sourcePath,Encoding.UTF8.GetBytes("api"));View.state=View.connected?"restoring":"disconnected";View.message="API 키를 저장했어요. 닉네임으로 계정을 찾아 주세요.";due=DateTime.MinValue;cooldown=DateTime.MinValue;Notify();
    }
    internal void UseSource(string source){
        if(View.connected||View.busy||(source!="ercraft"&&source!="api"&&source!="hippy"))return;
        Atomic(sourcePath,Encoding.UTF8.GetBytes(source));Reset();siteBlocked=false;View.source=source;View.state="disconnected";View.message=source=="hippy"?"닉네임으로 히피지지 시즌 통계를 연결해 주세요. API 키는 필요 없어요.":source=="ercraft"?"닉네임으로 ERCraft 공개 전적을 연결해 주세요. API 키는 필요 없어요.":"공식 API 키를 등록한 뒤 닉네임으로 계정을 찾아 주세요.";Notify();
    }
    internal async Task Search(string query){
        query=(query??"").Trim();if(View.source=="hippy"){try{query=EternalHippyProvider.Normalize(query);}catch(ErFailure ex){View.state=ex.State;View.message=ex.Message;Notify();return;}}if(View.source=="ercraft"){try{query=EternalCraftProvider.Normalize(query);}catch(ErFailure ex){View.state=ex.State;View.message=ex.Message;Notify();return;}}if(query.Length<1||query.Length>80||query.Any(char.IsControl))return;
        await Fetch(query,true,false);
    }
    internal void Connect(){
        if(View.busy||View.preview==null)return;
        var data=View.preview;Save(data.nickname,data);nickname=data.nickname;View.data=data;View.preview=null;View.connected=true;View.stale=false;View.state="connected";View.message=View.source=="hippy"?"히피지지 시즌 통계 연결을 저장했어요.":View.source=="ercraft"?"ERCraft 전적 연결을 저장했어요. 닉네임만으로 다시 불러와요.":"공식 API 계정 연결을 저장했어요.";due=Now().AddMinutes(RefreshMinutes);Notify();
    }
    internal void Disconnect(){
        // Delete first: a storage failure must not falsely report successful removal.
        if(File.Exists(accountPath))File.Delete(accountPath);if(File.Exists(keyPath))File.Delete(keyPath);if(File.Exists(sourcePath))File.Delete(sourcePath);
        Reset();key=null;nickname=null;siteBlocked=false;View=new ErView{message="이터널 리턴 계정과 API 키를 삭제했어요. 원신 연결은 유지돼요."};Notify();
    }
    void Reset(){revision++;cancel.Cancel();cancel.Dispose();cancel=new CancellationTokenSource();View.busy=false;View.preview=null;exitAttempt=0;exitDue=DateTime.MaxValue;}
    internal void GameExited(){if(!View.connected)return;exitMatch=View.data==null?0:View.data.matches.Select(m=>m.id).DefaultIfEmpty(0).Max();exitAttempt=0;exitDue=Now().AddSeconds(View.source=="ercraft"?60:30);View.message="게임 종료를 감지했어요. 잠시 후 새 경기 반영을 확인해요.";Notify();}
    internal async Task Tick(){
        if(verify||disposed||!View.connected||View.busy||siteBlocked)return;
        DateTime now=Now();
        if(now>=exitDue&&now>=cooldown){
            await Fetch(nickname,false,false);if(!View.connected||disposed)return;
            bool newer=View.data!=null&&View.data.matches.Any(m=>m.id>exitMatch);
            exitAttempt++;exitDue=newer||exitAttempt>=3?DateTime.MaxValue:Now().AddSeconds(exitAttempt==1?90:180);
            if(!newer&&exitDue==DateTime.MaxValue&&View.state=="connected")View.message="새 경기는 아직 반영되지 않았어요. 이후 정기 갱신에서 다시 확인해요.";Notify();
        }else if(exitDue==DateTime.MaxValue&&now>=due)await Fetch(nickname,false,false);
    }
    internal Task Refresh(bool manual){return Fetch(nickname,false,manual);}
    async Task Fetch(string query,bool search,bool manual){
        if(verify||disposed||View.busy||(!search&&!View.connected))return;
        if(View.source=="api"&&!View.hasKey){View.state="key";View.message="공식 개발자 포털에서 발급한 API 키를 등록해 주세요.";Notify();return;}
        if(Now()<cooldown){if(manual||search){View.message="요청 간격 또는 API 호출 제한으로 잠시 기다려 주세요.";Notify();}return;}
        if(siteBlocked){View.state="siteBlocked";View.message="데이터 조회 실패 · 이 Provider의 조회가 중단됐어요.";Notify();return;}
        int run=revision;var token=cancel.Token;cooldown=Now().AddSeconds(View.source=="ercraft"?60:30);due=Now().AddMinutes(RefreshMinutes);View.nextRefreshAt=due.ToString("o");View.busy=true;if(search)View.preview=null;Notify();
        try{
            var data=await (View.source=="hippy"?hippy:View.source=="ercraft"?website:provider).Read(View.source=="api"?key:null,query,token);if(run!=revision||disposed)return;
            if(search){View.preview=data;View.message="찾은 계정을 확인한 뒤 ‘이 계정 연결’을 눌러 주세요.";}
            else{Save(nickname,data);View.data=data;View.stale=false;View.message=View.source=="hippy"?"히피지지 시즌 통계를 불러왔어요. 동일 데이터는 최소 15분 동안 캐시를 사용해요.":View.source=="ercraft"?"ERCraft에 저장된 기록을 불러왔어요. 원본 사이트의 갱신 시각을 확인해 주세요.":"공식 API의 최신 기록이에요.";}
            View.state="connected";siteBlocked=false;
        }catch(OperationCanceledException){}
        catch(ErFailure ex){if(run!=revision||disposed)return;View.state=ex.State;View.message=ex.Message;View.stale=View.data!=null;if(ex.State=="siteBlocked"||ex.State=="key"||ex.State=="forbidden"){siteBlocked=true;exitDue=DateTime.MaxValue;}if(ex.WaitSeconds>0){cooldown=Now().AddSeconds(ex.WaitSeconds);due=cooldown;View.nextRefreshAt=due.ToString("o");}}
        catch{if(run!=revision||disposed)return;View.state="storage";View.message="응답 처리 또는 저장을 완료하지 못했어요. 이전 정상 기록을 유지해요.";View.stale=View.data!=null;}
        finally{if(run==revision&&!disposed){View.busy=false;Notify();}}
    }
    void Save(string name,ErData data){Atomic(accountPath,Encoding.UTF8.GetBytes(json.Serialize(new ErStored{nickname=name,source=View.source,data=data})));}
    static void Atomic(string path,byte[] bytes){string temp=path+".tmp";File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
    internal void SetFixture(ErView fixture){if(!verify)throw new InvalidOperationException();View=fixture;Notify();}
    public void Dispose(){disposed=true;cancel.Cancel();cancel.Dispose();provider.Dispose();website.Dispose();hippy.Dispose();}
}
internal sealed class EternalKeyDialog:Form {
    readonly TextBox input=new TextBox{UseSystemPasswordChar=true,Dock=DockStyle.Fill,MaxLength=2048};
    internal string KeyValue {get{return input.Text.Trim();}}
    internal EternalKeyDialog(){
        Text="이터널 리턴 API 키 등록";AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("맑은 고딕",10);StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(520,210);MinimumSize=new Size(460,240);MaximizeBox=false;MinimizeBox=false;
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=4,AutoSize=true};
        panel.Controls.Add(new Label{Text="공식 개발자 포털 → My Dashboard의 API 키\n이 Windows 계정에만 암호화 저장됩니다. 채팅에 공유하지 마세요.",AutoSize=true,Dock=DockStyle.Fill});
        panel.Controls.Add(input);var show=new CheckBox{Text="입력 내용 보기",AutoSize=true};show.CheckedChanged+=delegate{input.UseSystemPasswordChar=!show.Checked;};panel.Controls.Add(show);
        var save=new Button{Text="안전하게 저장",AutoSize=true,Padding=new Padding(10,4,10,4)};save.Click+=delegate{if(EternalReturnService.ValidKey(KeyValue)){DialogResult=DialogResult.OK;Close();}else MessageBox.Show(this,"공백/줄바꿈 없이 API 키를 입력해 주세요.");};panel.Controls.Add(save);Controls.Add(panel);AcceptButton=save;
    }
}
