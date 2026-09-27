using System;
using System.Collections.Generic;

// Future authenticated Riot requests belong here, never in the WebView.
internal sealed class RiotProfile {
    public string state="unavailable",riotId,rank,updatedAt,message,source="valking";
    public bool busy,stale;
    public int? points,wins,losses;
    public double? headshotPercent;
    public RiotMatch[] matches=new RiotMatch[0];
    public RiotCharacter[] characters=new RiotCharacter[0];
}
internal sealed class RiotMatch {
    public string id,result,characterName,imageUrl,playedAt,map,score;
    public int? kills,deaths,assists,cs,pointsChange;
}
internal sealed class RiotCharacter {
    public string name,imageUrl;
    public int? games;
    public double? winRate;
}
internal interface IRiotProvider { RiotProfile Read(); }
internal sealed class RiotService {
    internal readonly ValorantService Valorant;
    internal readonly LeagueService League;
    internal RiotService(string root,bool verify){League=new LeagueService(root,verify);Valorant=new ValorantService(root,verify);}
    internal object Snapshot(){return new {valorant=Valorant.Read(),league=League.View};}
    internal static string ClientState(string id,bool running,HashSet<string> names){
        if(running)return "playing";
        if(id!="valorant"&&id!="league")return "stopped";
        if(id=="league"&&(names.Contains("LeagueClient")||names.Contains("LeagueClientUx")))return "gameClient";
        if(names.Contains("RiotClientServices")||names.Contains("RiotClientUx"))return "riotClient";
        return "stopped";
    }
}
