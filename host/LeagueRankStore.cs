using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class LeagueSnapshot { public string timestamp,queue,tier,division;public int? points; }
// Windows 10+ inbox SQLite; no external executable or SQL built from account values.
internal sealed class LeagueRankStore:IDisposable {
    IntPtr db;readonly string databasePath;
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_backup_init(IntPtr dest,byte[] destName,IntPtr src,byte[] srcName);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_backup_step(IntPtr backup,int pages);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_backup_finish(IntPtr backup);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr stmt,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_text(IntPtr stmt,int index,byte[] value,int length,IntPtr destructor);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_int(IntPtr stmt,int index,int value);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_bind_null(IntPtr stmt,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr stmt,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_int(IntPtr stmt,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_type(IntPtr stmt,int index);
    static byte[] Utf(string value){return Encoding.UTF8.GetBytes(value+"\0");}
    static void Check(int result){if(result!=0)throw new InvalidOperationException("Rank snapshot storage failed");}
    internal LeagueRankStore(string path){databasePath=path;try{Check(sqlite3_open_v2(Utf(path),out db,6,IntPtr.Zero));int version=SchemaVersion;if(version>1)throw new InvalidOperationException("Newer database schema; data was not modified.");if(version<1)Migrate(1,delegate{Execute("CREATE TABLE IF NOT EXISTS snapshots (id INTEGER PRIMARY KEY, puuid TEXT NOT NULL, platform TEXT NOT NULL, timestamp TEXT NOT NULL, queue TEXT NOT NULL, tier TEXT NOT NULL, division TEXT NOT NULL, lp INTEGER)");Execute("CREATE INDEX IF NOT EXISTS snapshot_account ON snapshots(puuid,platform,id)");});}catch{Dispose();throw;}}
    internal int SchemaVersion {get{var s=Prepare("PRAGMA user_version");try{if(sqlite3_step(s)!=100)throw new InvalidOperationException("Invalid database; not reset.");return sqlite3_column_int(s,0);}finally{sqlite3_finalize(s);}}}
    internal void Migrate(int target,Action action){
        if(target<=SchemaVersion)return;
        // SQLite backup API also includes committed WAL pages. Never copy a live DB as raw bytes.
        string backupPath=databasePath+".before-v"+target+"-"+Guid.NewGuid().ToString("N")+".bak";IntPtr dest=IntPtr.Zero;
        try{Check(sqlite3_open_v2(Utf(backupPath),out dest,6,IntPtr.Zero));IntPtr copy=sqlite3_backup_init(dest,Utf("main"),db,Utf("main"));if(copy==IntPtr.Zero)throw new InvalidOperationException("Database backup failed; migration cancelled.");try{if(sqlite3_backup_step(copy,-1)!=101)throw new InvalidOperationException("Database backup incomplete; migration cancelled.");}finally{Check(sqlite3_backup_finish(copy));}}finally{if(dest!=IntPtr.Zero)sqlite3_close(dest);}
        Execute("BEGIN IMMEDIATE");try{action();Execute("PRAGMA user_version="+target.ToString(System.Globalization.CultureInfo.InvariantCulture));Execute("COMMIT");}catch{try{Execute("ROLLBACK");}catch{}throw;}
    }
    IntPtr Prepare(string sql){IntPtr s;Check(sqlite3_prepare_v2(db,Utf(sql),-1,out s,IntPtr.Zero));return s;}
    void Execute(string sql){var s=Prepare(sql);try{if(sqlite3_step(s)!=101)throw new InvalidOperationException();}finally{sqlite3_finalize(s);}}
    static void Bind(IntPtr s,int index,string value){var bytes=Utf(value);Check(sqlite3_bind_text(s,index,bytes,bytes.Length-1,new IntPtr(-1)));}
    internal void Append(LeagueIdentity identity,LeagueData data,DateTime now){
        Execute("BEGIN IMMEDIATE");try{
            foreach(var rank in data.ranks){var s=Prepare("INSERT INTO snapshots(puuid,platform,timestamp,queue,tier,division,lp) VALUES(?,?,?,?,?,?,?)");try{Bind(s,1,identity.puuid);Bind(s,2,identity.platform);Bind(s,3,now.ToUniversalTime().ToString("o"));Bind(s,4,rank.queue);Bind(s,5,rank.tier);Bind(s,6,rank.division);Check(rank.points.HasValue?sqlite3_bind_int(s,7,rank.points.Value):sqlite3_bind_null(s,7));if(sqlite3_step(s)!=101)throw new InvalidOperationException();}finally{sqlite3_finalize(s);}}
            Execute("COMMIT");
        }catch{try{Execute("ROLLBACK");}catch{}throw;}
    }
    static string Text(IntPtr s,int index){var p=sqlite3_column_text(s,index);if(p==IntPtr.Zero)return "";int len=0;while(Marshal.ReadByte(p,len)!=0)len++;var bytes=new byte[len];Marshal.Copy(p,bytes,0,len);return Encoding.UTF8.GetString(bytes);}
    internal LeagueSnapshot[] Read(LeagueIdentity identity){
        var rows=new List<LeagueSnapshot>();var s=Prepare("SELECT timestamp,queue,tier,division,lp FROM snapshots WHERE puuid=? AND platform=? ORDER BY id DESC LIMIT 40");
        try{Bind(s,1,identity.puuid);Bind(s,2,identity.platform);int result;while((result=sqlite3_step(s))==100)rows.Add(new LeagueSnapshot{timestamp=Text(s,0),queue=Text(s,1),tier=Text(s,2),division=Text(s,3),points=sqlite3_column_type(s,4)==5?(int?)null:sqlite3_column_int(s,4)});if(result!=101)throw new InvalidOperationException();return rows.ToArray();}finally{sqlite3_finalize(s);}
    }
    public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
}
