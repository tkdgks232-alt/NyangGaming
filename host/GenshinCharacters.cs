using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal sealed class CharacterStat {public string name,value;}
internal sealed class CharacterEquipment {
    public string name,icon,slot,set;public int? level,refinement;
    public List<CharacterStat> stats=new List<CharacterStat>();
}
internal sealed class GenshinCharacter {
    public int id;public string name,element,icon;public int? level,constellation,friendship,rarity;
    public CharacterEquipment weapon;public bool detailed;
    public List<CharacterStat> stats=new List<CharacterStat>();
    public List<CharacterEquipment> artifacts=new List<CharacterEquipment>();
}
internal sealed class CharacterView {
    public string state="idle",message="캐릭터 영역을 열면 현재 연결된 계정에서 조회해요.",updatedAt,detailUpdatedAt;
    public bool busy,stale;public int? selectedId;
    public List<GenshinCharacter> items=new List<GenshinCharacter>();
    public GenshinCharacter detail;
}
internal interface IGenshinCharacterProvider {
    Task<List<GenshinCharacter>> GetCharacters(Dictionary<string,string> cookies,GenshinRole role,int? id,CancellationToken token);
}
internal sealed partial class HoYoLabProvider:IGenshinCharacterProvider {
    public async Task<List<GenshinCharacter>> GetCharacters(Dictionary<string,string> cookies,GenshinRole role,int? id,CancellationToken token){
        if(role==null||ServerName(role.server)==null||!System.Text.RegularExpressions.Regex.IsMatch(role.uid??"",@"^\d{9,10}$"))throw ApiError(10104);
        var payload=new Dictionary<string,object>{{"role_id",role.uid},{"server",role.server}};
        if(id.HasValue)payload["character_ids"]=new[]{id.Value};
        return ParseCharacters(await Request("https://sg-public-api.hoyolab.com/event/game_record/genshin/api/character/"+(id.HasValue?"detail":"list"),cookies,token,payload),id.HasValue);
    }
    static CharacterStat Stat(Dictionary<string,object> p,Dictionary<string,object> map){
        var info=Obj(Value(map,Text(p,"property_type")));string value=Text(p,"final");if(value.Length==0)value=Text(p,"value");
        return new CharacterStat{name=Text(info,"name"),value=value};
    }
    static CharacterEquipment Equipment(Dictionary<string,object> d,Dictionary<string,object> map){
        if(d.Count==0)return null;
        var e=new CharacterEquipment{name=Text(d,"name"),icon=CharacterImageCache.SafeUrl(Text(d,"icon")),level=Num(d,"level"),refinement=Num(d,"affix_level"),slot=Text(d,"pos_name"),set=Text(Obj(Value(d,"set")),"name")};
        var main=Stat(Obj(Value(d,"main_property")),map);if(main.name.Length>0&&main.value.Length>0)e.stats.Add(main);
        var sub=Stat(Obj(Value(d,"sub_property")),map);if(sub.name.Length>0&&sub.value.Length>0)e.stats.Add(sub);
        foreach(var p in Rows(Value(d,"sub_property_list"))){var s=Stat(p,map);if(s.name.Length>0&&s.value.Length>0)e.stats.Add(s);}
        return e;
    }
    internal static List<GenshinCharacter> ParseCharacters(Dictionary<string,object> data,bool detailed){
        if(!(Value(data,"list") is System.Collections.IEnumerable))throw new HoyoFailure("format","캐릭터 목록 형식이 변경됐어요.");
        var result=new List<GenshinCharacter>();var map=Obj(Value(data,"property_map"));
        foreach(var item in Rows(Value(data,"list"))){
            var b=detailed?Obj(Value(item,"base")):item;int? id=Num(b,"id");if(!id.HasValue||id==0)continue;
            var c=new GenshinCharacter{id=id.Value,name=Text(b,"name"),element=Text(b,"element"),icon=CharacterImageCache.SafeUrl(Text(b,"icon")),level=Num(b,"level"),constellation=Num(b,"actived_constellation_num"),friendship=Num(b,"fetter"),rarity=Num(b,"rarity"),detailed=detailed};
            c.weapon=Equipment(Obj(Value(item,"weapon")),map);
            if(c.icon.Length==0)c.icon=CharacterImageCache.SafeUrl(Text(b,"image"));
            if(detailed){
                foreach(string group in new[]{"base_properties","extra_properties","element_properties","selected_properties"})foreach(var p in Rows(Value(item,group))){var s=Stat(p,map);if(s.name.Length>0&&s.value.Length>0&&!c.stats.Any(x=>x.name==s.name))c.stats.Add(s);}
                foreach(var r in Rows(Value(item,"relics"))){var e=Equipment(r,map);if(e!=null)c.artifacts.Add(e);}
            }
            result.Add(c);
        }
        return result.GroupBy(c=>c.id).Select(g=>g.First()).ToList();
    }
}
internal sealed partial class GenshinService {
    internal CharacterView Characters=new CharacterView();
    readonly Dictionary<int,Tuple<DateTime,GenshinCharacter>> details=new Dictionary<int,Tuple<DateTime,GenshinCharacter>>();
    CharacterImageCache imageCache;string characterOwner="";bool charactersRunning;DateTime charactersDue=DateTime.MinValue,charactersManual=DateTime.MinValue;
    internal void SetImageCache(CharacterImageCache cache){imageCache=cache;}
    void ClearCharacters(){Characters=new CharacterView();details.Clear();characterOwner="";charactersRunning=false;charactersDue=charactersManual=DateTime.MinValue;}
    internal async Task LoadCharacters(bool manual,int? id=null){
        if(session==null||View.selected==null||paused||!View.connected){Characters.message="먼저 원신 계정을 연결하고 캐릭터 서버를 선택해 주세요.";Notify();return;}
        var providerCharacters=provider as IGenshinCharacterProvider;if(providerCharacters==null)return;
        string owner=View.selected.uid+"/"+View.selected.server;
        if(characterOwner!=owner){ClearCharacters();characterOwner=owner;}
        if(charactersRunning)return;
        if(id.HasValue&&!Characters.items.Any(c=>c.id==id.Value))return;
        if(!manual&&!id.HasValue&&DateTime.UtcNow<charactersDue)return;
        Tuple<DateTime,GenshinCharacter> cached;
        if(id.HasValue&&details.TryGetValue(id.Value,out cached)&&!manual&&DateTime.UtcNow<cached.Item1.AddMinutes(30)){
            Characters.selectedId=id;Characters.detail=cached.Item2;Characters.detailUpdatedAt=cached.Item1.ToString("o");Characters.message="캐시된 캐릭터 상세정보예요.";Notify();return;
        }
        if(DateTime.UtcNow<charactersManual&&(!id.HasValue||Characters.state!="ready")){Characters.message="요청 간격을 보호하고 있어요. 잠시 후 다시 시도해 주세요.";Notify();return;}
        charactersRunning=true;Characters.busy=true;Characters.message="현재 계정의 캐릭터 정보를 조회하고 있어요.";
        if(id.HasValue){Characters.selectedId=id;Characters.detail=null;Characters.detailUpdatedAt=null;}
        int version=revision;var token=cancellation.Token;var role=View.selected;var active=session;Notify();
        try{
            var list=await providerCharacters.GetCharacters(active.cookies,role,id,token);if(version!=revision||owner!=characterOwner)return;
            if(imageCache!=null)await imageCache.Resolve(list,token);if(version!=revision||owner!=characterOwner)return;
            if(id.HasValue){var detail=list.FirstOrDefault(c=>c.id==id.Value);if(detail==null)throw new HoyoFailure("format","선택한 캐릭터의 상세 응답을 찾지 못했어요.");details[id.Value]=Tuple.Create(DateTime.UtcNow,detail);Characters.detail=detail;Characters.detailUpdatedAt=DateTime.UtcNow.ToString("o");}
            else {Characters.items=list;Characters.updatedAt=DateTime.UtcNow.ToString("o");charactersDue=DateTime.UtcNow.AddMinutes(30);if(manual){details.Clear();Characters.detail=null;Characters.selectedId=null;}}
            Characters.state="ready";Characters.stale=false;Characters.message="실제 HoYoLAB 캐릭터 정보 · 30분 캐시";
            charactersManual=DateTime.UtcNow.AddSeconds(id.HasValue?2:5);
        }catch(OperationCanceledException){}catch(Exception ex){if(version!=revision||owner!=characterOwner)return;var f=ex as HoyoFailure;
            Characters.state=f==null?"error":f.Kind;Characters.message=f==null?"캐릭터 정보를 불러오지 못했어요. 레진 등 다른 기능은 유지됩니다.":f.Message;Characters.stale=Characters.items.Count>0;
            charactersManual=DateTime.UtcNow.AddSeconds(30);charactersDue=charactersManual;
            if(f!=null&&f.Kind=="rateLimit")charactersDue=charactersManual=DateTime.UtcNow.AddMinutes(15);
            if(f!=null&&(f.Kind=="expired"||f.Kind=="verification"||f.Kind=="private")){View.state="expired";View.connected=false;View.stale=View.notes!=null;View.message=f.Message;paused=true;}
        }finally{if(version==revision&&owner==characterOwner){charactersRunning=false;Characters.busy=false;Notify();}}
    }
}
