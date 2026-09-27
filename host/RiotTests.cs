using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class RiotTests {
    internal static void Run(string output){
        var s=new GamingService(true);var original=s.Settings.seconds;
        var lines=new List<string>();
        Action<bool,string> check=(ok,label)=>{lines.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);};
        var now=new DateTime(2026,9,26,12,0,0);
        Action<string[],double> sample=(names,elapsed)=>{now=now.AddSeconds(elapsed);s.ApplyProcessSample(new HashSet<string>(names,StringComparer.OrdinalIgnoreCase),now,elapsed);};
        try{
            s.Settings.seconds=new Dictionary<string,double>();
            var v=s.Games.First(g=>g.id=="valorant");var l=s.Games.First(g=>g.id=="league");
            sample(new[]{"RiotClientServices","Chrome","Discord","VALORANT"},3);
            check(!v.running&&!l.running&&v.clientState=="riotClient"&&v.today==0,"launcher / unrelated processes never count");
            sample(new[]{"LeagueClient","LeagueClientUx"},3);
            check(!l.running&&l.clientState=="gameClient"&&l.today==0,"LoL client is not a match");
            sample(new[]{"valorant-win64-shipping","League of Legends"},3);
            check(v.running&&l.running&&v.session==0,"case-insensitive game start detection");
            sample(new[]{"VALORANT-Win64-Shipping","League of Legends"},3);
            check(v.today==3&&l.today==3&&v.session==3,"shared clock independently counts games");
            sample(new[]{"VALORANT-Win64-Shipping","League of Legends"},120);
            check(v.today==3&&l.today==3,"sleep / stalls excluded");
            s.Riot.League.SetFixture(new LeagueView{connected=true,data=LeagueTests.Fixture()});
            sample(new[]{"RiotClientServices","LeagueClient"},3);
            check(s.Riot.League.ExitPending,"real League process exit schedules API refresh");s.Riot.League.SetFixture(new LeagueView());
            check(!v.running&&!l.running&&v.session==0&&l.clientState=="gameClient"&&v.today==3,"exit clears session and preserves totals");
            sample(new[]{"GenshinImpact","EternalReturn"},3);sample(new[]{"GenshinImpact","EternalReturn"},3);
            check(s.Games[0].today==3&&s.Games[1].today==3,"existing Genshin and ER detection / accounting preserved");
            sample(new[]{"League of Legends"},3);
            now=new DateTime(2026,9,26,23,59,59);sample(new[]{"League of Legends"},3);
            check(s.Settings.seconds["league/2026-09-26"]==4&&s.Settings.seconds["league/2026-09-27"]==2,"midnight split");
            s.Save();var reloaded=new GamingService(true);
            check(reloaded.Settings.seconds["league/2026-09-27"]==2,"playtime persists across restart");
            check(new ValorantService().Read().rank==null&&s.Riot.League.View.data==null,"no fake Riot account data");
        }finally{s.Settings.seconds=original;s.Save();File.WriteAllLines(output,lines);}
    }
}
