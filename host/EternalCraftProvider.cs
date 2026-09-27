using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Experimental reader of the same unauthenticated GET resources used by ERCraft's
// public profile page. This is NOT a supported public developer API. No login,
// cookies, API keys, synchronization RPCs, challenge bypasses, or bulk crawling.
internal sealed class EternalCraftProvider:IEternalReturnDataProvider {
    readonly HttpClient http;
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024,RecursionLimit=100};
    readonly SemaphoreSlim gate=new SemaphoreSlim(1);
    DateTime next=DateTime.MinValue;internal int SpacingMs=1200;
    internal EternalCraftProvider(string root,HttpMessageHandler handler=null){
        Directory.CreateDirectory(root);
        http=new HttpClient(new ProviderHttpPolicy("ercraft",root,handler)){Timeout=TimeSpan.FromSeconds(20)};
    }    internal static string Normalize(string input){
        string name=(input??"").Trim();Uri uri;
        if(name.StartsWith("https://",StringComparison.OrdinalIgnoreCase)){
            if(!Uri.TryCreate(name,UriKind.Absolute,out uri)||uri.Host!="ercraft.net"||!uri.IsDefaultPort||uri.UserInfo.Length>0)throw Input();
            var match=Regex.Match(uri.AbsolutePath,"^/player/([^/]+)/?$");if(!match.Success)throw Input();name=Uri.UnescapeDataString(match.Groups[1].Value);
        }
        if(name.Length<1||name.Length>80||name.Any(char.IsControl)||name.IndexOfAny(new[]{'/','\\','?','#'})>=0||name.Contains("://"))throw Input();
        return name;
    }
    static ErFailure Input(){return new ErFailure("input","이터널 리턴 닉네임 또는 ERCraft 프로필 주소를 입력해 주세요.");}
    static ErFailure Format(){return new ErFailure("format","ERCraft 응답 형식이 바뀌었거나 기록이 불완전해요. 마지막 정상 기록을 유지해요.",900);}
    internal static List<Dictionary<string,object>> Rows(object obj,string field,bool optional=false){
        var raw=ErJson.Get(obj,field);if(raw==null&&optional)return new List<Dictionary<string,object>>();
        if(!(raw is IEnumerable)||raw is string||raw is IDictionary)throw Format();
        var rows=new List<Dictionary<string,object>>();foreach(var value in (IEnumerable)raw){var row=value as Dictionary<string,object>;if(row==null)throw Format();rows.Add(row);}return rows;
    }
    internal static int RetryAfter(HttpResponseMessage response){var header=response.Headers.RetryAfter;double seconds=header==null?900:header.Delta.HasValue?header.Delta.Value.TotalSeconds:header.Date.HasValue?(header.Date.Value-DateTimeOffset.UtcNow).TotalSeconds:900;return (int)Math.Min(int.MaxValue,Math.Max(60,Math.Ceiling(seconds)));}
    async Task<object> Get(string path,CancellationToken caller){
        if(!path.StartsWith("/api/players/",StringComparison.Ordinal))throw new InvalidOperationException();
        double delay=(next-DateTime.UtcNow).TotalMilliseconds;if(delay>0)await Task.Delay((int)Math.Min(int.MaxValue,delay),caller);next=DateTime.UtcNow.AddMilliseconds(SpacingMs);
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(caller)){
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try{using(var request=new HttpRequestMessage(HttpMethod.Get,"https://api.ercraft.net"+path)){
                request.Headers.UserAgent.ParseAdd("NyangGaming/0.1");
                using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token)){
                    int status=(int)response.StatusCode;
                    if(status==401||status==403)throw new ErFailure("siteBlocked","ERCraft에서 자동 조회를 허용하지 않아 중지했어요. 원본 사이트에서 확인해 주세요.",86400);
                    if(status==429){int retry=RetryAfter(response);next=DateTime.UtcNow.AddSeconds(retry);throw new ErFailure("rateLimit","ERCraft 호출 제한이에요. 지정된 대기 시간 동안 요청하지 않아요.",retry);}
                    if(status==404)throw new ErFailure("notFound","ERCraft에서 이 닉네임을 찾지 못했어요. 원본 사이트에서 이름과 공개 기록을 확인해 주세요.",300);
                    if(status==202)throw new ErFailure("service","ERCraft에서 기록을 준비 중이에요. 잠시 후 다시 확인해 주세요.",900);if(!response.IsSuccessStatusCode)throw new ErFailure("service","ERCraft에 연결하지 못했어요. 이전 정상 기록을 유지해요.",900);
                    if(response.Content.Headers.ContentType!=null&&response.Content.Headers.ContentType.MediaType.IndexOf("json",StringComparison.OrdinalIgnoreCase)<0)throw Format();
                    using(var stream=await response.Content.ReadAsStreamAsync())using(var memory=new MemoryStream()){
                        var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){if(memory.Length+n>8*1024*1024)throw Format();memory.Write(buffer,0,n);}
                        try{var parsed=json.DeserializeObject(Encoding.UTF8.GetString(memory.ToArray()));if(!(parsed is Dictionary<string,object>))throw Format();return ProviderHttpPolicy.Stamp(parsed,response);}catch(ErFailure){throw;}catch{throw Format();}
                    }
                }
            }}catch(OperationCanceledException){if(caller.IsCancellationRequested)throw;throw new ErFailure("network","ERCraft 응답 시간이 초과되었어요. 이전 정상 기록을 유지해요.",900);}
            catch(HttpRequestException){throw new ErFailure("network","인터넷 또는 ERCraft 연결을 확인해 주세요.",900);}
        }
    }
    public async Task<ErData> Read(string key,string nickname,CancellationToken token){
        nickname=Normalize(nickname);await gate.WaitAsync(token);
        try{
            string path="/api/players/"+Uri.EscapeDataString(nickname);
            var profile=await Get(path+"/profile-view",token);
            var matches=await Get(path+"/matches?page=0&pageSize=20&mode=all&cacheOnly=true",token);
            var result=Parse(nickname,profile,matches);result.updatedAt=ProviderHttpPolicy.DataTime(profile);return result;
        }finally{gate.Release();}
    }
    internal static ErData Parse(string nickname,object profile,object history){
        var p=ErJson.Get(profile,"data");var identity=ErJson.Get(p,"identity");
        if(!string.Equals(ErJson.Text(identity,"nickname"),nickname,StringComparison.OrdinalIgnoreCase))throw Format();
        var availability=ErJson.Get(p,"availability");
        if(ErJson.Text(availability,"status")!="available")throw new ErFailure("service","ERCraft 기록이 아직 준비되지 않았어요. 원본 사이트에서 확인해 주세요.",900);
        var rank=ErJson.Get(p,"currentRank");var seasons=ErJson.Get(p,"seasons");int? season=ErJson.Int(seasons,"currentSeason");
        if(!season.HasValue||season<=0||ErJson.Int(rank,"seasonId")!=season)throw Format();
        string status=ErJson.Text(rank,"status");if(status!="ranked"&&status!="unranked")throw Format();
        var d=new ErData{nickname=nickname,source="ercraft",sourceUrl="https://ercraft.net/player/"+Uri.EscapeDataString(nickname),sourceUpdatedAt=ErJson.Text(rank,"verifiedAt"),updatedAt=DateTime.UtcNow.ToString("o"),seasonId=season,seasonName="시즌 "+season,rp=status=="ranked"?ErJson.Int(rank,"rp"):null,tier=status=="ranked"?ErJson.Text(rank,"tier"):"현재 시즌 랭크 기록 없음",ranking=ErJson.Int(rank,"leaderboardRank"),rankMessage="ERCraft에 저장된 기록이에요. 원본 기록 갱신 시각을 확인해 주세요."};
        var row=Rows(seasons,"seasons").FirstOrDefault(x=>ErJson.Int(x,"seasonNumber")==season);
        if(row!=null){
            if(ErJson.Text(row,"displaySeasonGamesSource")=="official-stats"){d.seasonGames=ErJson.Int(row,"officialSeasonGames");d.seasonWins=ErJson.Int(row,"wins");}
            else d.metadataMessage="시즌 전체 경기·1위 횟수 미확인 · 수집된 경기 수를 시즌 전체 통계로 표시하지 않아요.";
        }
        var data=ErJson.Get(history,"data");if(ErJson.Int(data,"seasonId")!=season)throw Format();var rows=Rows(data,"items");
        foreach(var m in rows){
            var matchSeason=ErJson.Int(m,"seasonNumber");if(matchSeason!=season)continue;
            string mode=ErJson.Text(m,"gameMode");int? modeId=mode=="rank"?(int?)3:mode=="normal"?(int?)2:null;
            long id=ErJson.Long(m,"matchId");if(id<=0)throw Format();
            d.matches.Add(new ErMatch{id=id,season=matchSeason,mode=modeId,rank=ErJson.Int(m,"placement"),kills=ErJson.Int(m,"kills"),assists=ErJson.Int(m,"assists"),seconds=ErJson.Int(m,"gameDuration"),level=ErJson.Int(m,"characterLevel"),damage=ErJson.Int(m,"damageToPlayers"),received=ErJson.Int(m,"damageReceived"),healing=ErJson.Int(m,"healing"),teamKills=ErJson.Int(m,"teamKills"),character=CharacterName(ErJson.Int(m,"characterNum"),ErJson.Text(m,"characterName")),startedAt=ErJson.Text(m,"gameStartedAt"),delta=modeId==3?ErJson.Int(m,"rpDelta"):null});
        }
        d.matches=d.matches.GroupBy(m=>m.id).Select(g=>g.First()).OrderByDescending(m=>m.id).Take(20).ToList();return d;
    }
    // Korean character labels shipped in ERCraft's public client, verified 2026-09-28.
    static readonly Dictionary<int,string> CharacterNames=new Dictionary<int,string>{{1,"재키"},{2,"아야"},{3,"피오라"},{4,"매그너스"},{5,"자히르"},{6,"나딘"},{7,"현우"},{8,"하트"},{9,"아이솔"},{10,"리 다이린"},{11,"유키"},{12,"혜진"},{13,"쇼우"},{14,"키아라"},{15,"시셀라"},{16,"실비아"},{17,"아드리아나"},{18,"쇼이치"},{19,"엠마"},{20,"레녹스"},{21,"로지"},{22,"루크"},{23,"캐시"},{24,"아델라"},{25,"버니스"},{26,"바바라"},{27,"알렉스"},{28,"수아"},{29,"레온"},{30,"일레븐"},{31,"리오"},{32,"윌리엄"},{33,"니키"},{34,"나타폰"},{35,"얀"},{36,"이바"},{37,"다니엘"},{38,"제니"},{39,"카밀로"},{40,"클로에"},{41,"요한"},{42,"비앙카"},{43,"셀린"},{44,"에키온"},{45,"마이"},{46,"에이든"},{47,"라우라"},{48,"띠아"},{49,"펠릭스"},{50,"엘레나"},{51,"프리야"},{52,"아디나"},{53,"마커스"},{54,"칼라"},{55,"에스텔"},{56,"피올로"},{57,"마르티나"},{58,"헤이즈"},{59,"아이작"},{60,"타지아"},{61,"이렘"},{62,"테오도르"},{63,"이안"},{64,"바냐"},{65,"데비&마를렌"},{66,"아르다"},{67,"아비게일"},{68,"알론소"},{69,"레니"},{70,"츠바메"},{71,"케네스"},{72,"카티야"},{73,"샬럿"},{74,"다르코"},{75,"르노어"},{76,"가넷"},{77,"유민"},{78,"히스이"},{79,"유스티나"},{80,"이슈트반"},{81,"니아"},{82,"슈린"},{83,"헨리"},{84,"블레어"},{85,"미르카"},{86,"펜리르"},{87,"코렐라인"},{88,"비형"},{89,"크레이버"},{90,"루치아"},{9998,"Dr. 하나"},{9999,"나쟈"}};
    internal static string CharacterName(int? code,string label){
        bool placeholder=string.IsNullOrWhiteSpace(label)||label.StartsWith("실험체 #")||label.StartsWith("캐릭터 #");
        if(!placeholder)return label;
        int parsed;if(!code.HasValue&&label!=null){int hash=label.LastIndexOf('#');if(hash>=0&&int.TryParse(label.Substring(hash+1),out parsed))code=parsed;}
        string name;if(code.HasValue&&CharacterNames.TryGetValue(code.Value,out name))return name;
        return code.HasValue?"실험체 #"+code.Value:(string.IsNullOrWhiteSpace(label)?"캐릭터 정보 없음":label);
    }
    internal static void ResolveCharacters(ErData data){if(data==null||data.matches==null)return;foreach(var match in data.matches)match.character=CharacterName(null,match.character);}
    public void Dispose(){http.Dispose();}
}