using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Only the public GET resources used by Valking's profile UI. No cookies or Riot credentials.
internal sealed class ValorantService:IRiotProvider,IDisposable {
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
    readonly HttpClient http;
    readonly string file;
    readonly bool verify;
    RiotProfile view=new RiotProfile{message="Valking의 공개 Riot ID를 연결해 주세요."};
    DateTime next=DateTime.MinValue,manualAfter=DateTime.MinValue;
    bool blocked;
    internal event Action Changed;
    internal ValorantService(string root=null,bool verify=true,HttpMessageHandler handler=null){
        this.verify=verify;http=new HttpClient(new ProviderHttpPolicy("valking",root,handler)){Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=8*1024*1024};
        if(root!=null){file=Path.Combine(root,"valorant-valking.json");try{if(File.Exists(file)){var saved=json.Deserialize<RiotProfile>(File.ReadAllText(file));if(saved!=null&&RiotApiProvider.ValidId(saved.riotId)){view=saved;view.busy=false;view.stale=true;view.message="저장된 Valking 기록을 확인하고 있어요.";}}}catch{}}
    }
    public RiotProfile Read(){return view;}
    void Notify(){if(Changed!=null)Changed();}
    internal string SiteUrl {get{var parts=(view.riotId??"").Split('#');return parts.Length==2?"https://valking.gg/ko/player/"+Uri.EscapeDataString(parts[0])+"/"+Uri.EscapeDataString(parts[1]):"https://valking.gg/ko";}}
    internal async Task Connect(string id){
        if(view.busy)return;id=(id??"").Trim();
        if(!RiotApiProvider.ValidId(id)){view.message="Riot ID를 이름#태그 형식으로 입력해 주세요.";Notify();return;}
        if(DateTime.UtcNow<manualAfter){view.message="요청 간격을 지키기 위해 잠시 후 다시 연결해 주세요.";Notify();return;}
        if(!string.Equals(view.riotId,id,StringComparison.OrdinalIgnoreCase))view=new RiotProfile{riotId=id};
        if(blocked){view.state="siteBlocked";view.message="데이터 조회 실패 · Valking Provider의 조회가 중단됐어요.";Notify();return;}await Refresh();
    }
    internal Task Tick(){return !verify&&!blocked&&!view.busy&&view.riotId!=null&&DateTime.UtcNow>=next?Refresh():Task.FromResult(0);}
    internal void GameExited(){if(view.riotId!=null&&!blocked)next=DateTime.UtcNow.AddSeconds(60);}
    internal void Disconnect(){if(view.busy)return;if(file!=null&&File.Exists(file))File.Delete(file);view=new RiotProfile{message="연결을 해제했어요."};blocked=false;Notify();}
    internal void SetFixture(RiotProfile p){if(verify)view=p;}
    internal async Task Refresh(){
        if(view.busy||view.riotId==null||verify||blocked)return;
        if(DateTime.UtcNow<manualAfter){view.message="잠시 후 다시 조회해 주세요.";Notify();return;}
        view.busy=true;view.message="Valking 공개 전적을 불러오고 있어요.";Notify();
        next=DateTime.UtcNow.AddMinutes(30);manualAfter=DateTime.UtcNow.AddSeconds(60);
        try{
            string id=view.riotId,escaped=Uri.EscapeDataString(id);
            var profile=await Get("/api/profile/v1/"+escaped+"?version=300");
            ValidateProfile(id,profile);
            var history=await Get("/api/history/v1/"+escaped+"?version=300&queue=competitive&lang=ko");
            var parsed=Parse(id,profile,history);
            if(file!=null){string temp=file+".tmp";File.WriteAllText(temp,json.Serialize(parsed),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
            view=parsed;blocked=false;
        }catch(ErFailure e){view.state=e.State;view.stale=view.updatedAt!=null;view.message=e.Message;next=DateTime.UtcNow.AddSeconds(Math.Max(1800,e.WaitSeconds));manualAfter=DateTime.UtcNow.AddSeconds(Math.Max(60,e.WaitSeconds));blocked=e.State=="siteBlocked"||e.State=="private";}
        catch{view.state="error";view.stale=view.updatedAt!=null;view.message="Valking 조회 또는 저장에 실패했어요. 이전 정상 기록을 유지합니다.";}
        finally{view.busy=false;Notify();}
    }
    async Task<object> Get(string path){
        using(var request=new HttpRequestMessage(HttpMethod.Get,"https://valking.gg"+path)){
            request.Headers.UserAgent.ParseAdd("NyangGaming/0.1");
            using(var response=await http.SendAsync(request)){
                int status=(int)response.StatusCode;
                if(status==401||status==403)throw new ErFailure("siteBlocked","Valking에서 조회를 거부해 자동 조회를 중지했어요. 원본 사이트에서 확인해 주세요.",1800);
                if(status==429)throw new ErFailure("rateLimit","Valking 요청 제한으로 대기 중이에요.",LeagueIplolProvider.RetryAfter(response));
                if(status==404)throw new ErFailure("notFound","Valking에서 계정을 찾지 못했어요.",1800);
                if(!response.IsSuccessStatusCode)throw new ErFailure("service","Valking 연결에 실패했어요. 이전 정상 기록을 유지합니다.",1800);
                if(response.Content.Headers.ContentType==null||!response.Content.Headers.ContentType.MediaType.Contains("json"))throw Bad();
                string text=await response.Content.ReadAsStringAsync();if(text.Length>8*1024*1024)throw Bad();return ProviderHttpPolicy.Stamp(json.DeserializeObject(text),response);
            }
        }
    }
    static ErFailure Bad(){return new ErFailure("format","Valking 응답이 불완전하거나 형식이 바뀌었어요. 이전 정상 기록을 유지합니다.",1800);}
    static void ValidateProfile(string id,object data){
        var p=ErJson.Get(data,"profile");
        if(p==null||!string.Equals(ErJson.Text(p,"gameName")+"#"+ErJson.Text(p,"tagLine"),id,StringComparison.OrdinalIgnoreCase))throw Bad();
        if(!object.Equals(ErJson.Get(p,"isPrivate"),false))throw new ErFailure("private","비공개 프로필이에요. Valking에서 Riot 로그인 후 통계 공개가 필요합니다.",1800);
    }
    internal static RiotProfile Parse(string id,object data,object history){
        ValidateProfile(id,data);var r=ErJson.Get(data,"ranked");
        if(r==null||!(ErJson.Get(history,"history") is System.Collections.IList))throw Bad();
        int? tier=ErJson.Int(r,"rank");if(!tier.HasValue||tier<0||tier>27)throw Bad();
        string[] names={"Unranked","Unused","Unused","Iron 1","Iron 2","Iron 3","Bronze 1","Bronze 2","Bronze 3","Silver 1","Silver 2","Silver 3","Gold 1","Gold 2","Gold 3","Platinum 1","Platinum 2","Platinum 3","Diamond 1","Diamond 2","Diamond 3","Ascendant 1","Ascendant 2","Ascendant 3","Immortal 1","Immortal 2","Immortal 3","Radiant"};
        var p=new RiotProfile{state="connected",riotId=id,rank=names[tier.Value],wins=ErJson.Int(r,"rankedWins"),losses=ErJson.Int(r,"rankedLosses"),updatedAt=ProviderHttpPolicy.DataTime(data),message="Valking 공개 전적 · 자동 조회 30분 간격"};
        // The current public response does not include RR. Do not infer it from tier or history.
        int? head=ErJson.Int(r,"hitsHead"),body=ErJson.Int(r,"hitsBody"),legs=ErJson.Int(r,"hitsLegs");
        if(head.HasValue&&body.HasValue&&legs.HasValue&&head+body+legs>0)p.headshotPercent=Math.Round(100.0*head.Value/(head.Value+body.Value+legs.Value),1);
        var matches=new List<RiotMatch>();var seen=new HashSet<string>();
        foreach(var m in ErJson.Rows(ErJson.Get(history,"history"))){
            string mid=ErJson.Text(m,"matchID");int? k=ErJson.Int(m,"kills"),d=ErJson.Int(m,"deaths"),a=ErJson.Int(m,"assists"),outcome=ErJson.Int(m,"outcome"),ts=ErJson.Int(m,"whenTs");
            if(string.IsNullOrEmpty(mid)||!k.HasValue||!d.HasValue||!a.HasValue||!ts.HasValue||!outcome.HasValue)throw Bad();
            if(!seen.Add(mid))continue;
            matches.Add(new RiotMatch{id=mid,result=outcome==1?"승리":outcome==0?"패배":"무승부",characterName=ErJson.Text(m,"agent"),kills=k,deaths=d,assists=a,playedAt=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(ts.Value).ToString("o"),map=ErJson.Text(m,"mapLabel"),score=ErJson.Text(m,"roundWins")+" : "+ErJson.Text(m,"roundLosses")});
            if(matches.Count>=20)break;
        }
        p.matches=matches.ToArray();var agents=new List<RiotCharacter>();
        foreach(var x in ErJson.Rows(ErJson.Get(ErJson.Get(data,"agentsRecent"),"list"))){double value;var name=ErJson.Text(x,"name");if(string.IsNullOrEmpty(name))continue;agents.Add(new RiotCharacter{name=name,games=ErJson.Int(x,"games"),winRate=double.TryParse(ErJson.Text(x,"winrate"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)?(double?)value:null});}
        p.characters=agents.ToArray();return p;
    }
    public void Dispose(){http.Dispose();}
}
